using System.IO.Ports;
using System.Management;
using System.Text.RegularExpressions;
using SunTool.Core.Logging;

namespace SunTool.Modules.Comm.Logic;

/// <summary>COM 포트 하나. <see cref="IsPresent"/> 가 false 면 지금 PC 에 없는 포트 (마지막으로 쓴 포트 표시용).</summary>
public sealed record SerialPortEntry(string Name, string Description, bool IsPresent = true)
{
    public string DisplayText => string.IsNullOrEmpty(Description) ? Name : $"{Name} — {Description}";
}

/// <summary>
/// 지금 Windows 가 인식한 COM 포트 목록. 포트를 열지 않습니다.
/// 포트 이름은 레지스트리(HARDWARE\DEVICEMAP\SERIALCOMM), 장치 이름은 WMI(Win32_PnPEntity, 연결된 장치만)에서 읽습니다.
/// </summary>
public sealed partial class SerialPortCatalog(ILogService logs)
{
    private readonly IAppLogger _log = logs.For("Comm");
    private Dictionary<string, string> _names = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>포트 이름만 빠르게 (2초마다 변화 감지에 사용).</summary>
    public static string[] ReadPortNames()
    {
        try
        {
            return SerialPort.GetPortNames()
                .Select(n => n.Trim().TrimEnd('\0'))
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(PortNumber)
                .ThenBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>포트 이름 + 장치 이름. 포트 구성이 바뀌었을 때만 WMI 를 다시 읽습니다.</summary>
    public IReadOnlyList<SerialPortEntry> Read(IReadOnlyList<string> portNames)
    {
        if (portNames.Any(p => !_names.ContainsKey(p)))
        {
            _names = ReadDeviceNames();
        }

        return portNames.Select(p => new SerialPortEntry(p, _names.GetValueOrDefault(p, string.Empty))).ToList();
    }

    private Dictionary<string, string> ReadDeviceNames()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_PnPEntity WHERE Name LIKE '%(COM%'");
            using var results = searcher.Get();
            foreach (ManagementBaseObject device in results)
            {
                using (device)
                {
                    var name = device["Name"] as string ?? string.Empty;
                    var match = ComInName().Match(name);
                    if (match.Success)
                    {
                        // "USB 직렬 장치(COM3)" → "USB 직렬 장치"
                        map[match.Groups[1].Value.ToUpperInvariant()] = name.Remove(match.Index, match.Length).Trim();
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn("COM 포트 장치 이름을 읽지 못했습니다.", ex);
        }

        return map;
    }

    private static int PortNumber(string name) =>
        name.StartsWith("COM", StringComparison.OrdinalIgnoreCase) && int.TryParse(name.AsSpan(3), out var n) ? n : int.MaxValue;

    [GeneratedRegex(@"\s*\((COM\d+)\)", RegexOptions.IgnoreCase)]
    private static partial Regex ComInName();
}
