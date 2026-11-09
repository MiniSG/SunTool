using Microsoft.Win32;
using SunTool.Core.Logging;

namespace SunTool.Modules.SystemInfo.Logic;

/// <summary>'프로그램 및 기능'에 등록된 프로그램 한 개.</summary>
/// <param name="Hidden">
/// 목록에 보이지 않는 구성 요소(SystemComponent=1). 일부 SDK(예: pylon)는 본체를 숨김 항목으로 등록하므로,
/// 보이는 항목에서 못 찾았을 때 대신 사용합니다.
/// </param>
public sealed record InstalledProgram(
    string DisplayName,
    string DisplayVersion,
    string Publisher,
    string InstallLocation,
    string InstallDate,
    bool Hidden = false);

/// <summary>장치 관리자의 장치 한 개.</summary>
public sealed record PnpDevice(
    string Name,
    string Manufacturer,
    string PnpClass,
    string DeviceId,
    string Status,
    int ErrorCode);

public static class InstalledProgramReader
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static IReadOnlyList<InstalledProgram> ReadAll(IAppLogger? log = null)
    {
        // 이름+버전이 같으면 보이는 항목을 우선합니다.
        var programs = new Dictionary<string, InstalledProgram>(StringComparer.OrdinalIgnoreCase);

        foreach (var (hive, view) in new[]
                 {
                     (RegistryHive.LocalMachine, RegistryView.Registry64),
                     (RegistryHive.LocalMachine, RegistryView.Registry32),
                     (RegistryHive.CurrentUser, RegistryView.Registry64),
                 })
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var uninstall = baseKey.OpenSubKey(UninstallPath);
                if (uninstall is null)
                {
                    continue;
                }

                foreach (var name in uninstall.GetSubKeyNames())
                {
                    using var key = uninstall.OpenSubKey(name);
                    var displayName = (key?.GetValue("DisplayName") as string)?.Trim();
                    if (key is null || string.IsNullOrEmpty(displayName))
                    {
                        continue;
                    }

                    // 업데이트(KB) 항목은 제외
                    if (key.GetValue("ParentKeyName") is not null)
                    {
                        continue;
                    }

                    var hidden = Equals(key.GetValue("SystemComponent"), 1);
                    var version = (key.GetValue("DisplayVersion") as string)?.Trim() ?? string.Empty;
                    var id = displayName + "|" + version;
                    if (programs.TryGetValue(id, out var existing) && (!existing.Hidden || hidden))
                    {
                        continue;
                    }

                    programs[id] = new InstalledProgram(
                        displayName,
                        version,
                        (key.GetValue("Publisher") as string)?.Trim() ?? string.Empty,
                        (key.GetValue("InstallLocation") as string)?.Trim() ?? string.Empty,
                        (key.GetValue("InstallDate") as string)?.Trim() ?? string.Empty,
                        hidden);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                log?.Warn(Loc.F("설치 프로그램 목록 일부를 읽지 못했습니다 ({0}/{1}): {2}", hive, view, ex.Message));
            }
        }

        return programs.Values.ToList();
    }
}

public static class PnpDeviceReader
{
    public static IReadOnlyList<PnpDevice> ReadAll(IAppLogger? log = null)
    {
        try
        {
            return Wmi.Query("SELECT Name, Manufacturer, PNPClass, PNPDeviceID, Status, ConfigManagerErrorCode FROM Win32_PnPEntity", timeoutSeconds: 30)
                .Select(row => new PnpDevice(
                    row.Str("Name"),
                    row.Str("Manufacturer"),
                    row.Str("PNPClass"),
                    row.Str("PNPDeviceID"),
                    row.Str("Status"),
                    (int)(row.Long("ConfigManagerErrorCode") ?? 0)))
                .ToList();
        }
        catch (Exception ex)
        {
            log?.Warn(Loc.T("장치 목록(WMI)을 읽지 못했습니다."), ex);
            return Array.Empty<PnpDevice>();
        }
    }

    /// <summary>장치 관리자 오류 코드 → 상태.</summary>
    public static (InfoStatus Status, string Text) DescribeState(PnpDevice device) => device.ErrorCode switch
    {
        0 => (InfoStatus.Ok, Loc.T("정상 작동")),
        22 => (InfoStatus.Warning, Loc.T("사용 안 함 (코드 22)")),
        28 => (InfoStatus.Error, Loc.T("드라이버 없음 (코드 28)")),
        10 => (InfoStatus.Error, Loc.T("시작할 수 없음 (코드 10)")),
        45 => (InfoStatus.Warning, Loc.T("현재 연결되어 있지 않음 (코드 45)")),
        _ => (InfoStatus.Error, Loc.F("장치 오류 (코드 {0})", device.ErrorCode)),
    };

    /// <summary>장치가 어떤 버스로 연결되었는지 (장치 ID 앞부분).</summary>
    public static string BusOf(string deviceId)
    {
        var prefix = deviceId.Split('\\', 2)[0].ToUpperInvariant();
        return prefix switch
        {
            "PCI" => "PCI/PCIe",
            "USB" or "USBSTOR" or "FTDIBUS" => "USB",
            "ACPI" => Loc.T("메인보드 내장"),
            "MF" => Loc.T("다기능 확장 카드"),
            "SWD" or "ROOT" => Loc.T("소프트웨어(가상)"),
            "BTHENUM" => Loc.T("블루투스"),
            _ => prefix,
        };
    }

    /// <summary>드라이버 버전·날짜 (해당 장치만 조회).</summary>
    public static (string Version, DateTime? Date) DriverOf(string deviceId)
    {
        try
        {
            var row = Wmi.Query($"SELECT DriverVersion, DriverDate FROM Win32_PnPSignedDriver WHERE DeviceID = '{Wmi.EscapeLiteral(deviceId)}'", timeoutSeconds: 15)
                .FirstOrDefault();
            return row is null ? (string.Empty, null) : (row.Str("DriverVersion"), row.Date("DriverDate"));
        }
        catch (Exception)
        {
            return (string.Empty, null);
        }
    }
}

/// <summary>보드·BIOS 시리얼 등에서 제조사가 채우지 않은 기본값을 걸러 냅니다.</summary>
public static class ValueText
{
    private static readonly HashSet<string> Placeholders = new(StringComparer.OrdinalIgnoreCase)
    {
        "default string", "to be filled by o.e.m.", "to be filled by oem", "o.e.m.", "oem",
        "system serial number", "system product name", "system manufacturer", "system version",
        "base board serial number", "chassis serial number", "not specified", "not applicable",
        "none", "n/a", "0", "00000000", "123456789", "1234567890", "default", "serial",
        "03000200-0400-0500-0006-000700080009", "ffffffff-ffff-ffff-ffff-ffffffffffff",
    };

    public static bool IsPlaceholder(string? value) =>
        string.IsNullOrWhiteSpace(value) || Placeholders.Contains(value.Trim());

    /// <summary>시리얼처럼 비어 있을 수 있는 값: 기본값이면 '기록하지 않음' 경고로 표시.</summary>
    public static InfoItem Identity(string name, string? value) =>
        IsPlaceholder(value)
            ? new InfoItem(name, Loc.T("(제조사가 기록하지 않음)"), InfoStatus.Warning,
                string.IsNullOrWhiteSpace(value) ? null : Loc.F("장치에 기록된 값: '{0}'", value!.Trim()))
            : new InfoItem(name, value!.Trim());

    public static string OrDash(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value.Trim();
}
