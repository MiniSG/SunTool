using Microsoft.Win32;
using SunTool.Core.Logging;
using SunTool.Core.Text;

namespace SunTool.Modules.SystemInfo.Logic.Providers;

/// <summary>컴퓨터: 시스템·OS·보드/BIOS(시리얼)·CPU·메모리·저장 장치·그래픽.</summary>
public sealed class ComputerInfoProvider : ISystemInfoProvider
{
    public string Id => "computer";

    public string Title => Loc.T("컴퓨터");

    public string IconGlyph => "";

    public int Order => 10;

    public ProviderResult Collect(CollectionContext context, CancellationToken cancellationToken)
    {
        var result = new ProviderResult();
        var log = context.Log;

        var system = Section(Loc.T("시스템"), log, section =>
        {
            var cs = Wmi.Query("SELECT Manufacturer, Model, TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault() ?? new();
            var product = Wmi.Query("SELECT IdentifyingNumber, UUID FROM Win32_ComputerSystemProduct").FirstOrDefault() ?? new();
            section.Items.Add(new InfoItem(Loc.T("PC 이름"), Environment.MachineName));
            section.Items.Add(new InfoItem(Loc.T("제조사"), ValueText.OrDash(cs.Str("Manufacturer"))));
            section.Items.Add(new InfoItem(Loc.T("모델"), ValueText.OrDash(cs.Str("Model"))));
            section.Items.Add(ValueText.Identity(Loc.T("시스템 시리얼"), product.Str("IdentifyingNumber")));
            section.Items.Add(ValueText.Identity(Loc.T("시스템 UUID"), product.Str("UUID")));
        });
        result.Sections.Add(system);
        cancellationToken.ThrowIfCancellationRequested();

        var osSection = Section(Loc.T("운영체제"), log, section =>
        {
            var row = Wmi.Query("SELECT Caption, Version, BuildNumber, OSArchitecture, InstallDate, LastBootUpTime FROM Win32_OperatingSystem").FirstOrDefault() ?? new();
            var (displayVersion, ubr) = ReadOsDisplayVersion();
            var caption = row.Str("Caption").Replace("Microsoft ", string.Empty);
            section.Items.Add(new InfoItem(Loc.T("이름"), caption + (displayVersion is null ? string.Empty : $" ({displayVersion})")));
            section.Items.Add(new InfoItem(Loc.T("빌드"), row.Str("Version") + (ubr is null ? string.Empty : $".{ubr}")));
            section.Items.Add(new InfoItem(Loc.T("아키텍처"), ValueText.OrDash(row.Str("OSArchitecture"))));
            section.Items.Add(new InfoItem(Loc.T("설치한 날짜"), row.Date("InstallDate")?.ToString("yyyy-MM-dd") ?? "-"));
            var boot = row.Date("LastBootUpTime");
            section.Items.Add(new InfoItem(Loc.T("마지막 부팅"), boot is null ? "-" : Loc.F("{0:yyyy-MM-dd HH:mm} (가동 {1})", boot, FormatUptime(DateTime.Now - boot.Value))));
            result.Summary.Add(new InfoItem(Loc.T("운영체제"), caption + (displayVersion is null ? string.Empty : $" {displayVersion}")));
        });
        result.Sections.Add(osSection);

        var board = Section(Loc.T("메인보드 · BIOS"), log, section =>
        {
            var bb = Wmi.Query("SELECT Manufacturer, Product, SerialNumber FROM Win32_BaseBoard").FirstOrDefault() ?? new();
            var bios = Wmi.Query("SELECT Manufacturer, SMBIOSBIOSVersion, ReleaseDate, SerialNumber FROM Win32_BIOS").FirstOrDefault() ?? new();
            section.Items.Add(new InfoItem(Loc.T("보드 제조사"), ValueText.OrDash(bb.Str("Manufacturer"))));
            section.Items.Add(new InfoItem(Loc.T("보드 모델"), ValueText.OrDash(bb.Str("Product"))));
            var boardSerial = ValueText.Identity(Loc.T("보드 시리얼"), bb.Str("SerialNumber"));
            section.Items.Add(boardSerial);
            section.Items.Add(new InfoItem("BIOS", $"{ValueText.OrDash(bios.Str("Manufacturer"))} {bios.Str("SMBIOSBIOSVersion")}".Trim()));
            section.Items.Add(new InfoItem(Loc.T("BIOS 날짜"), bios.Date("ReleaseDate")?.ToString("yyyy-MM-dd") ?? "-"));
            var biosSerial = ValueText.Identity(Loc.T("BIOS 시리얼"), bios.Str("SerialNumber"));
            section.Items.Add(biosSerial);
            result.Summary.Add(boardSerial with { Name = Loc.T("보드 시리얼") });
        });
        result.Sections.Add(board);
        cancellationToken.ThrowIfCancellationRequested();

        var cpu = Section("CPU", log, section =>
        {
            var rows = Wmi.Query("SELECT Name, NumberOfCores, NumberOfLogicalProcessors, MaxClockSpeed, SocketDesignation FROM Win32_Processor");
            for (var i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                var prefix = rows.Count > 1 ? $"CPU {i + 1} " : string.Empty;
                section.Items.Add(new InfoItem(prefix + Loc.T("이름"), row.Str("Name")));
                section.Items.Add(new InfoItem(prefix + Loc.T("코어 / 스레드"), Loc.F("{0}코어 / {1}스레드", row.Long("NumberOfCores"), row.Long("NumberOfLogicalProcessors"))));
                section.Items.Add(new InfoItem(prefix + Loc.T("최대 클럭"), $"{(row.Long("MaxClockSpeed") ?? 0) / 1000.0:0.00} GHz"));
                if (i == 0)
                {
                    result.Summary.Add(new InfoItem("CPU", Loc.F("{0} ({1}코어/{2}스레드)", row.Str("Name"), row.Long("NumberOfCores"), row.Long("NumberOfLogicalProcessors"))));
                }
            }
        });
        result.Sections.Add(cpu);

        var memory = Section(Loc.T("메모리 (RAM)"), log, section =>
        {
            var cs = Wmi.Query("SELECT TotalPhysicalMemory FROM Win32_ComputerSystem").FirstOrDefault() ?? new();
            var osRow = Wmi.Query("SELECT FreePhysicalMemory FROM Win32_OperatingSystem").FirstOrDefault() ?? new();
            var modules = Wmi.Query("SELECT DeviceLocator, Capacity, ConfiguredClockSpeed, Speed, Manufacturer, PartNumber FROM Win32_PhysicalMemory");
            var installed = modules.Sum(m => m.Long("Capacity") ?? 0);
            var total = cs.Long("TotalPhysicalMemory") ?? 0;
            var free = (osRow.Long("FreePhysicalMemory") ?? 0) * 1024;

            section.Items.Add(new InfoItem(Loc.T("설치된 용량"), Loc.F("{0} ({1}개 모듈)", SizeFormatter.Format(installed), modules.Count)));
            section.Items.Add(new InfoItem(Loc.T("사용 가능 / 전체"), $"{SizeFormatter.Format(free)} / {SizeFormatter.Format(total)}"));
            foreach (var module in modules)
            {
                var speed = module.Long("ConfiguredClockSpeed") ?? module.Long("Speed");
                var part = $"{module.Str("Manufacturer")} {module.Str("PartNumber")}".Trim();
                section.Items.Add(new InfoItem(
                    ValueText.OrDash(module.Str("DeviceLocator")),
                    $"{SizeFormatter.Format(module.Long("Capacity") ?? 0)}{(speed is > 0 ? $" · {speed} MT/s" : string.Empty)}",
                    Detail: part.Length > 0 ? part : null));
            }

            result.Summary.Add(new InfoItem(Loc.T("메모리"), Loc.F("{0} ({1}개 모듈)", SizeFormatter.Format(installed), modules.Count)));
        });
        result.Sections.Add(memory);
        cancellationToken.ThrowIfCancellationRequested();

        var storage = Section(Loc.T("저장 장치"), log, section =>
        {
            foreach (var disk in Wmi.Query("SELECT Model, Size, InterfaceType, SerialNumber, MediaType FROM Win32_DiskDrive"))
            {
                section.Items.Add(new InfoItem(
                    ValueText.OrDash(disk.Str("Model")),
                    $"{SizeFormatter.Format(disk.Long("Size") ?? 0)} · {disk.Str("InterfaceType")}",
                    Detail: ValueText.IsPlaceholder(disk.Str("SerialNumber")) ? null : Loc.F("시리얼: {0}", disk.Str("SerialNumber").Trim('.', ' '))));
            }

            foreach (var drive in DriveInfo.GetDrives().Where(d => d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable))
            {
                var usedRatio = drive.TotalSize == 0 ? 0 : 1 - ((double)drive.AvailableFreeSpace / drive.TotalSize);
                section.Items.Add(new InfoItem(
                    $"{drive.Name.TrimEnd('\\')} {drive.VolumeLabel}".Trim(),
                    Loc.F("여유 {0} / 전체 {1} ({2})", SizeFormatter.Format(drive.AvailableFreeSpace), SizeFormatter.Format(drive.TotalSize), drive.DriveFormat),
                    usedRatio > 0.9 ? InfoStatus.Warning : InfoStatus.None,
                    usedRatio > 0.9 ? Loc.T("남은 공간이 10% 미만입니다.") : null));
            }
        });
        result.Sections.Add(storage);

        var gpu = Section(Loc.T("그래픽"), log, section =>
        {
            foreach (var video in Wmi.Query("SELECT Name, DriverVersion, DriverDate, CurrentHorizontalResolution, CurrentVerticalResolution FROM Win32_VideoController"))
            {
                var width = video.Long("CurrentHorizontalResolution");
                var height = video.Long("CurrentVerticalResolution");
                section.Items.Add(new InfoItem(
                    video.Str("Name"),
                    Loc.F("드라이버 {0}", ValueText.OrDash(video.Str("DriverVersion"))) + (width is > 0 ? $" · {width}×{height}" : string.Empty),
                    Detail: video.Date("DriverDate") is { } date ? Loc.F("드라이버 날짜: {0:yyyy-MM-dd}", date) : null));
            }
        });
        result.Sections.Add(gpu);

        return result;
    }

    /// <summary>섹션 하나를 채우다 오류가 나도 다른 섹션은 계속 보여 줍니다.</summary>
    internal static InfoSection Section(string title, IAppLogger log, Action<InfoSection> fill)
    {
        var section = new InfoSection(title);
        try
        {
            fill(section);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.Warn($"'{title}' 정보를 읽지 못했습니다.", ex);
            section.Items.Add(new InfoItem(Loc.T("읽기 실패"), ex.Message, InfoStatus.Error));
        }

        if (section.Items.Count == 0)
        {
            section.Items.Add(new InfoItem(Loc.T("정보 없음"), "-"));
        }

        return section;
    }

    private static (string? DisplayVersion, string? Ubr) ReadOsDisplayVersion()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var display = (key?.GetValue("DisplayVersion") ?? key?.GetValue("ReleaseId")) as string;
            var ubr = key?.GetValue("UBR")?.ToString();
            return (display, ubr);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return (null, null);
        }
    }

    private static string FormatUptime(TimeSpan span) =>
        span.TotalDays >= 1 ? Loc.F("{0}일 {1}시간", (int)span.TotalDays, span.Hours) : Loc.F("{0}시간 {1}분", span.Hours, span.Minutes);
}
