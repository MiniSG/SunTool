using System.Diagnostics;
using System.IO.Enumeration;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using SunTool.Core.Logging;

namespace SunTool.Modules.SystemInfo.Logic;

/// <summary>감지할 소프트웨어 정의 한 개 (software.json 의 항목).</summary>
public sealed class SoftwareDefinition
{
    public string Category { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Vendor { get; set; }

    public int Priority { get; set; } = 5;

    public bool Summary { get; set; }

    /// <summary>'프로그램 및 기능' 이름과 비교할 정규식.</summary>
    public List<string> ProgramPatterns { get; set; } = new();

    public List<string> ExcludePatterns { get; set; } = new();

    public List<string> EnvVars { get; set; } = new();

    /// <summary>버전을 읽을 파일. %변수% 와 폴더·파일 이름의 * 사용 가능 (예: %ProgramFiles%\Basler\pylon*\Runtime\x64\PylonBase_v*.dll).</summary>
    public List<string> VersionFiles { get; set; } = new();

    /// <summary>설치 프로그램 없이 압축만 풀어 쓰는 라이브러리(OpenCV 등)를 찾는 폴더 검색.</summary>
    public FolderSearchDefinition? SearchFolders { get; set; }

    /// <summary>PATH 환경 변수의 폴더들에서 찾을 파일 이름 (예: opencv_world*.dll).</summary>
    public List<string> PathFiles { get; set; } = new();

    /// <summary>파일에 버전 정보가 없을 때 파일 이름에서 버전을 뽑는 정규식. 그룹들을 '.' 으로 이어 붙입니다.</summary>
    public string? VersionFromFileName { get; set; }

    public List<string> RegistryKeys { get; set; } = new();
}

public sealed class FolderSearchDefinition
{
    /// <summary>검색을 시작할 폴더 (%변수% 사용 가능).</summary>
    public List<string> Roots { get; set; } = new();

    /// <summary>찾을 폴더 이름 (예: *opencv*).</summary>
    public string FolderPattern { get; set; } = "*";

    /// <summary>시작 폴더 아래 몇 단계까지 폴더 이름을 볼지.</summary>
    public int MaxDepth { get; set; } = 2;

    /// <summary>찾은 폴더 안에서 찾을 파일 이름.</summary>
    public List<string> Files { get; set; } = new();

    /// <summary>찾은 폴더 아래 몇 단계까지 파일을 볼지.</summary>
    public int FileDepth { get; set; } = 6;
}

/// <summary>기본 정의(프로그램 내장) + 사용자 정의(exe 옆 SunTool.software.json)를 합친 목록.</summary>
public sealed class SoftwareCatalog
{
    public const string UserFileName = "SunTool.software.json";
    private const string ResourceName = "SunTool.SystemInfo.software.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private SoftwareCatalog(IReadOnlyList<SoftwareDefinition> definitions, string? userFilePath, string? userFileError)
    {
        Definitions = definitions;
        UserFilePath = userFilePath;
        UserFileError = userFileError;
    }

    public IReadOnlyList<SoftwareDefinition> Definitions { get; }

    /// <summary>실제로 읽은 사용자 정의 파일 경로 (없으면 null).</summary>
    public string? UserFilePath { get; }

    public string? UserFileError { get; }

    public static string ReadDefaultJson()
    {
        using var stream = typeof(SoftwareCatalog).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException("기본 소프트웨어 정의를 찾을 수 없습니다.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static List<SoftwareDefinition> Parse(string json) =>
        JsonSerializer.Deserialize<Root>(json, JsonOptions)?.Software ?? new List<SoftwareDefinition>();

    public static SoftwareCatalog Load(string? userFilePath, IAppLogger? log = null)
    {
        var merged = Parse(ReadDefaultJson());
        string? loadedUserFile = null;
        string? error = null;

        if (!string.IsNullOrEmpty(userFilePath) && File.Exists(userFilePath))
        {
            try
            {
                foreach (var custom in Parse(File.ReadAllText(userFilePath)))
                {
                    merged.RemoveAll(d => string.Equals(d.Name, custom.Name, StringComparison.OrdinalIgnoreCase)
                                          && string.Equals(d.Category, custom.Category, StringComparison.OrdinalIgnoreCase));
                    merged.Add(custom);
                }

                loadedUserFile = userFilePath;
                log?.Debug($"사용자 소프트웨어 정의 적용: {userFilePath}");
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                error = ex.Message;
                log?.Warn($"사용자 소프트웨어 정의 파일을 읽지 못해 기본값만 사용합니다: {userFilePath}", ex);
            }
        }

        return new SoftwareCatalog(merged, loadedUserFile, error);
    }

    public IEnumerable<SoftwareDefinition> ForCategory(string category) =>
        Definitions
            .Where(d => string.Equals(d.Category, category, StringComparison.OrdinalIgnoreCase))
            .OrderBy(d => d.Priority)
            .ThenBy(d => d.Name, StringComparer.CurrentCultureIgnoreCase);

    private sealed class Root
    {
        public List<SoftwareDefinition>? Software { get; set; }
    }
}

/// <summary>정의 하나의 감지 결과.</summary>
public sealed record SoftwareDetection(
    SoftwareDefinition Definition,
    IReadOnlyList<InstalledProgram> Programs,
    IReadOnlyList<(string Name, string Value)> EnvVars,
    IReadOnlyList<(string Path, string? Version)> Files,
    IReadOnlyList<string> RegistryKeys)
{
    public bool IsInstalled => Programs.Count > 0 || EnvVars.Count > 0 || Files.Count > 0 || RegistryKeys.Count > 0;

    /// <summary>파일에서 읽은 버전들 (중복 제거).</summary>
    public IReadOnlyList<string> FileVersions =>
        Files.Select(f => f.Version).OfType<string>().Where(v => v.Length > 0).Distinct().ToList();

    public string? Version =>
        Programs.Select(p => p.DisplayVersion).FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))
        ?? FileVersions.FirstOrDefault();
}

public static partial class SoftwareDetector
{
    private const int MaxFiles = 10;
    private const int MaxVisitedFolders = 4000;

    private static readonly System.IO.EnumerationOptions ShallowOptions = new()
    {
        IgnoreInaccessible = true,
        MatchCasing = MatchCasing.CaseInsensitive,
        RecurseSubdirectories = false,
        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System,
    };

    public static SoftwareDetection Detect(
        SoftwareDefinition definition,
        IReadOnlyList<InstalledProgram> programs,
        Func<string, string?>? getEnv = null,
        Func<string, bool>? registryExists = null)
    {
        getEnv ??= DefaultEnv;
        registryExists ??= DefaultRegistryExists;

        var include = definition.ProgramPatterns.Select(ToRegex).ToList();
        var exclude = definition.ExcludePatterns.Select(ToRegex).ToList();
        var matched = include.Count == 0
            ? new List<InstalledProgram>()
            : programs
                .Where(p => include.Any(r => r.IsMatch(p.DisplayName)) && !exclude.Any(r => r.IsMatch(p.DisplayName)))
                .OrderBy(p => p.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

        // 보이는 항목을 우선하고, 없을 때만 숨김 구성 요소를 사용
        var visible = matched.Where(p => !p.Hidden).ToList();
        var matchedPrograms = visible.Count > 0 ? visible : matched;

        var envVars = definition.EnvVars
            .Select(name => (Name: name, Value: getEnv(name)))
            .Where(e => !string.IsNullOrWhiteSpace(e.Value))
            .Select(e => (e.Name, e.Value!))
            .ToList();

        var paths = new List<string>();
        foreach (var pattern in definition.VersionFiles)
        {
            paths.AddRange(ResolveFiles(pattern, getEnv));
        }

        if (definition.SearchFolders is { } search)
        {
            paths.AddRange(SearchFolders(search, getEnv));
        }

        foreach (var name in definition.PathFiles)
        {
            paths.AddRange(SearchPath(name, getEnv));
        }

        var files = paths
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxFiles)
            .Select(path => (path, ReadFileVersion(path) ?? VersionFromName(path, definition.VersionFromFileName)))
            .ToList();

        var keys = definition.RegistryKeys.Where(registryExists).ToList();
        return new SoftwareDetection(definition, matchedPrograms, envVars, files, keys);
    }

    /// <summary>감지 결과를 화면 항목으로 바꿉니다 (같은 제품이 여러 버전 설치되면 각각 한 줄).</summary>
    public static List<InfoItem> ToItems(SoftwareDetection detection)
    {
        var definition = detection.Definition;
        var evidence = new List<string>();
        evidence.AddRange(detection.EnvVars.Select(e => $"{e.Name}={e.Value}"));
        evidence.AddRange(detection.Files.Select(f => $"{f.Path}{(f.Version is null ? string.Empty : $" (v{f.Version})")}"));
        evidence.AddRange(detection.RegistryKeys.Select(k => $@"HKLM\{k}"));

        if (detection.Programs.Count > 0)
        {
            var multiple = detection.Programs.Count > 1;
            return detection.Programs.Select((program, index) =>
            {
                var details = new List<string> { $"설치 이름: {program.DisplayName}" };
                if (program.Publisher.Length > 0)
                {
                    details.Add($"제조사: {program.Publisher}");
                }

                if (program.InstallLocation.Length > 0)
                {
                    details.Add($"경로: {program.InstallLocation}");
                }

                if (index == 0)
                {
                    details.AddRange(evidence);
                }

                var version = !string.IsNullOrWhiteSpace(program.DisplayVersion)
                    ? program.DisplayVersion
                    : detection.FileVersions.FirstOrDefault() ?? "설치됨 (버전 정보 없음)";

                return new InfoItem(multiple ? $"{definition.Name} #{index + 1}" : definition.Name, version, InfoStatus.Installed, string.Join("  ·  ", details));
            }).ToList();
        }

        if (detection.IsInstalled)
        {
            var versions = detection.FileVersions;
            return
            [
                new InfoItem(
                    definition.Name,
                    versions.Count > 0 ? string.Join(", ", versions) : "설치 흔적 있음 (버전 확인 불가)",
                    versions.Count > 0 ? InfoStatus.Installed : InfoStatus.Warning,
                    string.Join("  ·  ", evidence)),
            ];
        }

        return [new InfoItem(definition.Name, "설치되지 않음", InfoStatus.NotInstalled)];
    }

    /// <summary>
    /// %변수% 를 펼친 뒤 경로를 찾습니다. 폴더·파일 이름에 * ? 를 쓸 수 있고 .. 도 됩니다.
    /// 예: %ProgramFiles%\Basler\pylon*\Runtime\x64\PylonBase_v*.dll
    /// </summary>
    public static IReadOnlyList<string> ResolveFiles(string pattern, Func<string, string?> getEnv)
    {
        var expanded = ExpandEnv(pattern, getEnv);
        if (expanded is null)
        {
            return Array.Empty<string>();
        }

        try
        {
            var root = Path.GetPathRoot(expanded);
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return Array.Empty<string>();
            }

            var segments = expanded[root.Length..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
            var current = new List<string> { root };
            for (var i = 0; i < segments.Length && current.Count > 0; i++)
            {
                var segment = segments[i];
                var last = i == segments.Length - 1;
                var next = new List<string>();
                foreach (var directory in current)
                {
                    if (segment == "..")
                    {
                        var parent = Path.GetDirectoryName(directory.TrimEnd('\\'));
                        if (parent is not null)
                        {
                            next.Add(parent);
                        }

                        continue;
                    }

                    if (segment.IndexOfAny(['*', '?']) >= 0)
                    {
                        if (Directory.Exists(directory))
                        {
                            next.AddRange((last
                                ? Directory.EnumerateFiles(directory, segment, ShallowOptions)
                                : Directory.EnumerateDirectories(directory, segment, ShallowOptions)).Take(MaxFiles));
                        }

                        continue;
                    }

                    var candidate = Path.Combine(directory, segment);
                    if (last ? File.Exists(candidate) : Directory.Exists(candidate))
                    {
                        next.Add(candidate);
                    }
                }

                current = next;
            }

            return current.Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            return Array.Empty<string>();
        }
    }

    /// <summary>시작 폴더들 아래에서 이름이 맞는 폴더를 찾고(얕게), 그 안에서 파일을 찾습니다.</summary>
    public static IReadOnlyList<string> SearchFolders(FolderSearchDefinition search, Func<string, string?> getEnv)
    {
        var results = new List<string>();
        var visited = 0;

        foreach (var rootPattern in search.Roots)
        {
            var root = ExpandEnv(rootPattern, getEnv);
            if (root is null || !Directory.Exists(root))
            {
                continue;
            }

            // 1) 이름이 맞는 폴더 찾기 (MaxDepth 단계까지)
            var matches = new List<string>();
            var level = new List<string> { root };
            for (var depth = 1; depth <= Math.Max(1, search.MaxDepth) && level.Count > 0 && visited < MaxVisitedFolders; depth++)
            {
                var nextLevel = new List<string>();
                foreach (var directory in level)
                {
                    IEnumerable<string> children;
                    try
                    {
                        children = Directory.EnumerateDirectories(directory, "*", ShallowOptions).ToList();
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        continue;
                    }

                    foreach (var child in children)
                    {
                        if (++visited > MaxVisitedFolders)
                        {
                            break;
                        }

                        var name = Path.GetFileName(child);
                        if (FileSystemName.MatchesSimpleExpression(search.FolderPattern, name, ignoreCase: true))
                        {
                            matches.Add(child);
                        }
                        else if (!SkipFolder(name))
                        {
                            nextLevel.Add(child);
                        }
                    }
                }

                level = nextLevel;
            }

            // 2) 찾은 폴더 안에서 파일 찾기
            var deep = new System.IO.EnumerationOptions
            {
                IgnoreInaccessible = true,
                MatchCasing = MatchCasing.CaseInsensitive,
                RecurseSubdirectories = true,
                MaxRecursionDepth = Math.Max(0, search.FileDepth),
                AttributesToSkip = FileAttributes.ReparsePoint,
            };

            foreach (var folder in matches)
            {
                foreach (var filePattern in search.Files)
                {
                    try
                    {
                        results.AddRange(Directory.EnumerateFiles(folder, filePattern, deep).Take(MaxFiles));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        // 읽을 수 없는 폴더는 건너뜀
                    }

                    if (results.Count >= MaxFiles)
                    {
                        return results;
                    }
                }
            }
        }

        return results;
    }

    /// <summary>PATH 의 폴더들에서 파일을 찾습니다 (프로세스 + 시스템 + 사용자 PATH).</summary>
    public static IReadOnlyList<string> SearchPath(string filePattern, Func<string, string?> getEnv)
    {
        var pathValue = string.Join(';', new[]
        {
            getEnv("PATH"),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine),
            Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User),
        }.Where(p => !string.IsNullOrEmpty(p)));

        var results = new List<string>();
        foreach (var raw in pathValue.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            var directory = ExpandEnv(raw.Trim('"'), getEnv);
            if (directory is null)
            {
                continue;
            }

            try
            {
                if (Directory.Exists(directory))
                {
                    results.AddRange(Directory.EnumerateFiles(directory, filePattern, ShallowOptions).Take(MaxFiles));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // 잘못된 PATH 항목은 무시
            }
        }

        return results;
    }

    public static string? ReadFileVersion(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            var version = (info.ProductVersion ?? info.FileVersion)?.Trim();
            return string.IsNullOrEmpty(version) ? null : version;
        }
        catch (Exception ex) when (ex is FileNotFoundException or IOException)
        {
            return null;
        }
    }

    /// <summary>파일 이름에서 버전 추출 (예: opencv_world4100.dll + 정규식 → 4.10.0).</summary>
    public static string? VersionFromName(string path, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            return null;
        }

        var match = Regex.Match(Path.GetFileName(path), pattern, RegexOptions.IgnoreCase, TimeSpan.FromSeconds(1));
        if (!match.Success || match.Groups.Count < 2)
        {
            return null;
        }

        return string.Join('.', match.Groups.Cast<Group>().Skip(1).Where(g => g.Success).Select(g => int.TryParse(g.Value, out var n) ? n.ToString() : g.Value));
    }

    /// <summary>%변수% 를 펼칩니다. 값이 없는 변수가 있으면 null.</summary>
    private static string? ExpandEnv(string pattern, Func<string, string?> getEnv)
    {
        var unresolved = false;
        var expanded = EnvRegex().Replace(pattern, m =>
        {
            var value = getEnv(m.Groups[1].Value);
            if (string.IsNullOrEmpty(value))
            {
                unresolved = true;
                return m.Value;
            }

            return value.TrimEnd('\\');
        });

        return unresolved ? null : expanded;
    }

    /// <summary>폴더 검색에서 들어가지 않을 폴더 (시간만 오래 걸리고 결과가 없는 곳).</summary>
    private static bool SkipFolder(string name) =>
        name.StartsWith('$') || name.StartsWith('.')
        || name.Equals("Windows", StringComparison.OrdinalIgnoreCase)
        || name.Equals("AppData", StringComparison.OrdinalIgnoreCase)
        || name.Equals("node_modules", StringComparison.OrdinalIgnoreCase)
        || name.Equals("System Volume Information", StringComparison.OrdinalIgnoreCase);

    private static Regex ToRegex(string pattern) =>
        new(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));

    private static string? DefaultEnv(string name) =>
        Environment.GetEnvironmentVariable(name)
        ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine);

    private static bool DefaultRegistryExists(string path)
    {
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            try
            {
                using var root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view);
                using var key = root.OpenSubKey(path);
                if (key is not null)
                {
                    return true;
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or System.Security.SecurityException or IOException)
            {
                // 읽을 수 없으면 '없음'으로 간주
            }
        }

        return false;
    }

    // %ProgramFiles(x86)% 처럼 괄호가 있는 변수 이름도 허용
    [GeneratedRegex(@"%([^%\\]+)%")]
    private static partial Regex EnvRegex();
}
