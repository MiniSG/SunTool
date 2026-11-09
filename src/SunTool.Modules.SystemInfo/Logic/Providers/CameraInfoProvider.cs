using System.Text.RegularExpressions;

namespace SunTool.Modules.SystemInfo.Logic.Providers;

/// <summary>카메라·그랩보드: 장치 관리자에서 찾은 장치, 카메라 SDK(Basler·DALSA 우선), GenTL 생산자.</summary>
public sealed partial class CameraInfoProvider : ISystemInfoProvider
{
    public string Id => "camera";

    public string Title => "카메라 · 그랩보드";

    public string IconGlyph => "";

    public int Order => 30;

    public ProviderResult Collect(CollectionContext context, CancellationToken cancellationToken)
    {
        var result = new ProviderResult();

        // 1) 장치 관리자의 카메라 / 그랩보드
        var devices = context.PnpDevices
            .Where(IsCameraDevice)
            .OrderBy(d => PriorityOf(d))
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();

        var deviceSection = new InfoSection("감지된 카메라 · 그랩보드 장치")
        {
            Note = "GigE 카메라는 네트워크로 연결되어 장치 목록에 나타나지 않습니다. pylon Viewer·CamExpert 같은 카메라 SDK 도구로 확인하세요.",
        };

        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var (status, stateText) = PnpDeviceReader.DescribeState(device);
            var (driverVersion, driverDate) = PnpDeviceReader.DriverOf(device.DeviceId);
            var bus = PnpDeviceReader.BusOf(device.DeviceId);
            var kind = bus == "PCI/PCIe" ? "그랩보드(PCIe)" : bus == "USB" ? "USB 장치" : bus;
            deviceSection.Items.Add(new InfoItem(
                device.Name,
                $"{kind} · {ValueText.OrDash(device.Manufacturer)}" + (driverVersion.Length > 0 ? $" · 드라이버 {driverVersion}" : string.Empty),
                status,
                stateText + (driverDate is { } date ? $" · 드라이버 날짜 {date:yyyy-MM-dd}" : string.Empty)));
        }

        if (devices.Count == 0)
        {
            deviceSection.Items.Add(new InfoItem("카메라·그랩보드 장치", "감지된 장치 없음", InfoStatus.NotInstalled));
        }

        result.Sections.Add(deviceSection);
        result.Summary.Add(devices.Count > 0
            ? new InfoItem("카메라·그랩보드 장치", $"{devices.Count}개", InfoStatus.Installed, string.Join(", ", devices.Select(d => d.Name).Take(4)))
            : new InfoItem("카메라·그랩보드 장치", "감지된 장치 없음", InfoStatus.NotInstalled));

        // 2) 카메라 SDK · 드라이버 (정의 파일)
        var sdkSection = new InfoSection("카메라 SDK · 그랩보드 드라이버");
        foreach (var definition in context.Software.ForCategory("camera"))
        {
            var items = SoftwareDetector.ToItems(SoftwareDetector.Detect(definition, context.InstalledPrograms));
            sdkSection.Items.AddRange(items);
            if (definition.Summary)
            {
                result.Summary.AddRange(items.Select(i => i with { Detail = null }));
            }
        }

        result.Sections.Add(sdkSection);

        // 3) GenTL 생산자 (GenICam 표준 드라이버 목록)
        result.Sections.Add(CollectGenTl());
        return result;
    }

    private static InfoSection CollectGenTl()
    {
        var section = new InfoSection("GenTL 생산자 (GenICam)")
        {
            Note = "카메라 SDK가 GENICAM_GENTL64_PATH 환경 변수에 등록한 .cti 드라이버입니다. 여러 SDK가 같은 카메라를 잡으려 할 때 확인합니다.",
        };

        foreach (var variable in new[] { "GENICAM_GENTL64_PATH", "GENICAM_GENTL32_PATH" })
        {
            var value = Environment.GetEnvironmentVariable(variable)
                        ?? Environment.GetEnvironmentVariable(variable, EnvironmentVariableTarget.Machine);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            foreach (var directory in value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string[] files;
                try
                {
                    files = Directory.Exists(directory) ? Directory.GetFiles(directory, "*.cti") : Array.Empty<string>();
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    files = Array.Empty<string>();
                }

                foreach (var file in files)
                {
                    section.Items.Add(new InfoItem(
                        Path.GetFileName(file),
                        SoftwareDetector.ReadFileVersion(file) ?? "버전 정보 없음",
                        InfoStatus.Installed,
                        $"{variable.Replace("GENICAM_", string.Empty)} · {directory}"));
                }

                if (files.Length == 0)
                {
                    section.Items.Add(new InfoItem("(등록된 폴더)", "폴더가 없거나 .cti 파일 없음", InfoStatus.Warning, directory));
                }
            }
        }

        if (section.Items.Count == 0)
        {
            section.Items.Add(new InfoItem("GenTL 생산자", "등록된 GenTL 없음", InfoStatus.NotInstalled));
        }

        return section;
    }

    public static bool IsCameraDevice(PnpDevice device)
    {
        if (string.Equals(device.PnpClass, "Camera", StringComparison.OrdinalIgnoreCase)
            || string.Equals(device.PnpClass, "Image", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return CameraRegex().IsMatch(device.Name) || CameraRegex().IsMatch(device.Manufacturer);
    }

    /// <summary>Basler, DALSA 장치를 먼저 보여 줍니다.</summary>
    private static int PriorityOf(PnpDevice device)
    {
        var text = device.Name + " " + device.Manufacturer;
        if (text.Contains("Basler", StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        return DalsaRegex().IsMatch(text) ? 1 : 2;
    }

    [GeneratedRegex(@"Basler|DALSA|Teledyne|Xtium|Xcelera|Matrox|Euresys|Coaxlink|Grablink|Hikrobot|Hikvision|Allied Vision|FLIR|Point Grey|Cognex|IMAQ|GigE Vision|USB3 ?Vision|CoaXPress|Camera ?Link|Frame ?Grabber|GenICam", RegexOptions.IgnoreCase)]
    private static partial Regex CameraRegex();

    [GeneratedRegex(@"DALSA|Teledyne|Xtium|Xcelera", RegexOptions.IgnoreCase)]
    private static partial Regex DalsaRegex();
}
