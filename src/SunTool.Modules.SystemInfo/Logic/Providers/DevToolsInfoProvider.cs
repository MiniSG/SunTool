using System.Diagnostics;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace SunTool.Modules.SystemInfo.Logic.Providers;

/// <summary>개발 도구: Visual Studio(vswhere), .NET, VC++ 재배포, 기타(정의 파일).</summary>
public sealed partial class DevToolsInfoProvider : ISystemInfoProvider
{
    public string Id => "devtools";

    public string Title => "개발 도구";

    public string IconGlyph => "";

    public int Order => 50;

    public ProviderResult Collect(CollectionContext context, CancellationToken cancellationToken)
    {
        var result = new ProviderResult();
        var log = context.Log;

        // Visual Studio
        var vs = ComputerInfoProvider.Section("Visual Studio", log, section =>
        {
            var instances = ReadVisualStudioInstances(out var source);
            foreach (var instance in instances)
            {
                section.Items.Add(new InfoItem(instance.Name, instance.Version, instance.Complete ? InfoStatus.Installed : InfoStatus.Warning,
                    $"경로: {instance.Path}" + (instance.Complete ? string.Empty : " · 설치가 완료되지 않았습니다")));
            }

            foreach (var legacy in context.InstalledPrograms.Where(p => !p.Hidden && LegacyVsRegex().IsMatch(p.DisplayName)))
            {
                section.Items.Add(new InfoItem(legacy.DisplayName, ValueText.OrDash(legacy.DisplayVersion), InfoStatus.Installed, "이전 버전 (2015 이하)"));
            }

            if (section.Items.Count == 0)
            {
                section.Items.Add(new InfoItem("Visual Studio", "설치되지 않음", InfoStatus.NotInstalled));
            }

            section.Note = source;
            result.Summary.AddRange(section.Items.Select(i => i with { Detail = null }));
        });
        result.Sections.Add(vs);
        cancellationToken.ThrowIfCancellationRequested();

        // .NET
        result.Sections.Add(ComputerInfoProvider.Section(".NET", log, section =>
        {
            var dotnetRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet");
            var sdks = ListVersionFolders(Path.Combine(dotnetRoot, "sdk"));
            section.Items.Add(sdks.Count > 0
                ? new InfoItem(".NET SDK", string.Join(", ", sdks), InfoStatus.Installed)
                : new InfoItem(".NET SDK", "설치되지 않음", InfoStatus.NotInstalled));

            foreach (var (runtime, label) in new[]
                     {
                         ("Microsoft.NETCore.App", ".NET 런타임"),
                         ("Microsoft.WindowsDesktop.App", "데스크톱 런타임 (WPF/WinForms)"),
                         ("Microsoft.AspNetCore.App", "ASP.NET Core 런타임"),
                     })
            {
                var versions = ListVersionFolders(Path.Combine(dotnetRoot, "shared", runtime));
                if (versions.Count > 0)
                {
                    section.Items.Add(new InfoItem(label, string.Join(", ", versions), InfoStatus.Installed));
                }
            }

            section.Items.Add(new InfoItem(".NET Framework", ReadNetFramework() ?? "4.5 미만 또는 확인 불가"));
        }));

        // VC++ 재배포 패키지 (비전 라이브러리 실행에 필요)
        result.Sections.Add(ComputerInfoProvider.Section("Visual C++ 재배포 패키지", log, section =>
        {
            foreach (var program in context.InstalledPrograms
                         .Where(p => !p.Hidden && VcRedistRegex().IsMatch(p.DisplayName))
                         .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase))
            {
                section.Items.Add(new InfoItem(program.DisplayName.Replace("Microsoft Visual C++ ", "VC++ "), ValueText.OrDash(program.DisplayVersion), InfoStatus.Installed));
            }

            if (section.Items.Count == 0)
            {
                section.Items.Add(new InfoItem("VC++ 재배포 패키지", "설치되지 않음", InfoStatus.NotInstalled));
            }
        }));

        // 정의 파일의 기타 개발 도구
        var others = new InfoSection("기타 개발 도구");
        foreach (var definition in context.Software.ForCategory("devtools"))
        {
            var detection = SoftwareDetector.Detect(definition, context.InstalledPrograms);
            var items = SoftwareDetector.ToItems(detection);
            others.Items.AddRange(items);
            if (definition.Summary)
            {
                result.Summary.AddRange(items.Select(i => i with { Detail = null }));
            }
        }

        if (others.Items.Count > 0)
        {
            result.Sections.Add(others);
        }

        return result;
    }

    /// <summary>vswhere.exe(Visual Studio 설치 관리자에 포함)로 설치된 VS 2017 이상을 찾습니다.</summary>
    public static List<VisualStudioInstance> ReadVisualStudioInstances(out string source)
    {
        var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(vswhere))
        {
            source = "vswhere.exe 가 없어 Visual Studio 2017 이상은 확인하지 못했습니다 (설치되지 않았을 가능성이 큽니다).";
            return new List<VisualStudioInstance>();
        }

        source = "Visual Studio 설치 관리자(vswhere)로 확인";
        var start = new ProcessStartInfo(vswhere, "-all -prerelease -products * -format json -utf8")
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
        };

        using var process = Process.Start(start) ?? throw new InvalidOperationException("vswhere 실행 실패");
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(15000))
        {
            process.Kill();
            throw new TimeoutException("vswhere 응답 시간 초과");
        }

        return ParseVsWhere(output);
    }

    public static List<VisualStudioInstance> ParseVsWhere(string json)
    {
        var list = new List<VisualStudioInstance>();
        using var document = JsonDocument.Parse(json);
        foreach (var element in document.RootElement.EnumerateArray())
        {
            var name = element.TryGetProperty("displayName", out var n) ? n.GetString() : null;
            var version = element.TryGetProperty("catalog", out var catalog) && catalog.TryGetProperty("productDisplayVersion", out var v)
                ? v.GetString()
                : element.TryGetProperty("installationVersion", out var iv) ? iv.GetString() : null;
            var path = element.TryGetProperty("installationPath", out var p) ? p.GetString() : null;
            var complete = !element.TryGetProperty("isComplete", out var c) || c.ValueKind != JsonValueKind.False;
            list.Add(new VisualStudioInstance(name ?? "Visual Studio", version ?? "-", path ?? "-", complete));
        }

        return list.OrderBy(i => i.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    /// <summary>NDP\v4\Full 의 Release 값 → .NET Framework 4.x 버전.</summary>
    public static string? NetFrameworkFromRelease(int release) => release switch
    {
        >= 533320 => "4.8.1",
        >= 528040 => "4.8",
        >= 461808 => "4.7.2",
        >= 461308 => "4.7.1",
        >= 460798 => "4.7",
        >= 394802 => "4.6.2",
        >= 394254 => "4.6.1",
        >= 393295 => "4.6",
        >= 379893 => "4.5.2",
        >= 378675 => "4.5.1",
        >= 378389 => "4.5",
        _ => null,
    };

    private static string? ReadNetFramework()
    {
        try
        {
            using var key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64)
                .OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full");
            return key?.GetValue("Release") is int release ? NetFrameworkFromRelease(release) : null;
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException)
        {
            return null;
        }
    }

    private static List<string> ListVersionFolders(string path)
    {
        if (!Directory.Exists(path))
        {
            return new List<string>();
        }

        return Directory.GetDirectories(path)
            .Select(d => Path.GetFileName(d))
            .OfType<string>()
            .Where(n => n.Length > 0 && char.IsDigit(n[0]))
            .OrderBy(n => Version.TryParse(n.Split('-')[0], out var v) ? v : new Version(0, 0))
            .ToList();
    }

    [GeneratedRegex(@"^Microsoft Visual Studio (Professional|Enterprise|Community|Premium|Ultimate)? ?20(05|08|10|12|13|15)\b", RegexOptions.IgnoreCase)]
    private static partial Regex LegacyVsRegex();

    [GeneratedRegex(@"^Microsoft Visual C\+\+ \d{4}.*Redistributable", RegexOptions.IgnoreCase)]
    private static partial Regex VcRedistRegex();
}

public sealed record VisualStudioInstance(string Name, string Version, string Path, bool Complete);
