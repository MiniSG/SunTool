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

    /// <summary>사용자 정의의 잘못된 정규식 등. 있으면 감지하지 않고 그 항목만 '확인 필요'로 표시합니다.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string? Error { get; set; }
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

    /// <summary>category 값 = 시스템 정보 분류 Id (화면 이름은 <see cref="CategoryListText"/>).</summary>
    public static readonly string[] KnownCategories = ["computer", "communication", "camera", "vision", "devtools"];

    /// <summary>오류 문구용: computer(컴퓨터), communication(통신), ...</summary>
    public static string CategoryListText =>
        string.Join(", ", KnownCategories.Select(c => $"{c}({CategoryScreen(c)})"));

    public static string CategoryScreen(string category) => category switch
    {
        "computer" => Loc.T("컴퓨터"),
        "communication" => Loc.T("통신"),
        "camera" => Loc.T("카메라 · 그랩보드"),
        "vision" => Loc.T("이미지 라이브러리"),
        "devtools" => Loc.T("개발 도구"),
        _ => category,
    };

    private SoftwareCatalog(IReadOnlyList<SoftwareDefinition> definitions, string? userFilePath, IReadOnlyList<string> userFileProblems)
    {
        Definitions = definitions;
        UserFilePath = userFilePath;
        UserFileProblems = userFileProblems;
    }

    public IReadOnlyList<SoftwareDefinition> Definitions { get; }

    /// <summary>실제로 읽은 사용자 정의 파일 경로 (없거나 JSON 형식이 깨져 못 읽었으면 null).</summary>
    public string? UserFilePath { get; }

    /// <summary>사용자 정의 파일의 문제 (JSON 형식 오류는 줄 번호 포함, 잘못된 항목·정규식). 화면 '요약'에 표시합니다.</summary>
    public IReadOnlyList<string> UserFileProblems { get; }

    public static string ReadDefaultJson()
    {
        using var stream = typeof(SoftwareCatalog).Assembly.GetManifestResourceStream(ResourceName)
                           ?? throw new InvalidOperationException(Loc.T("기본 소프트웨어 정의를 찾을 수 없습니다."));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static List<SoftwareDefinition> Parse(string json) =>
        JsonSerializer.Deserialize<Root>(json, JsonOptions)?.Software ?? new List<SoftwareDefinition>();

    public static SoftwareCatalog Load(string? userFilePath, IAppLogger? log = null)
    {
        var merged = Parse(ReadDefaultJson());
        string? loadedUserFile = null;
        var problems = new List<string>();

        if (!string.IsNullOrEmpty(userFilePath) && File.Exists(userFilePath))
        {
            string? json = null;
            try
            {
                json = File.ReadAllText(userFilePath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                problems.Add(Loc.F("파일을 읽지 못했습니다: {0}", ex.Message));
            }

            if (json is not null && ParseUser(json, problems) is { } customs)
            {
                foreach (var custom in customs)
                {
                    merged.RemoveAll(d => string.Equals(d.Name, custom.Name, StringComparison.OrdinalIgnoreCase)
                                          && string.Equals(d.Category, custom.Category, StringComparison.OrdinalIgnoreCase));
                    merged.Add(custom);
                }

                loadedUserFile = userFilePath;
                log?.Debug(Loc.F("사용자 소프트웨어 정의 적용: {0}", userFilePath));
            }

            foreach (var problem in problems)
            {
                log?.Warn($"{UserFileName}: {problem}");
            }
        }

        return new SoftwareCatalog(merged, loadedUserFile, problems);
    }

    /// <summary>
    /// 사용자 정의 파일을 읽습니다. JSON 형식이 깨졌으면 null (줄 번호와 함께 문제 추가).
    /// 항목 하나가 잘못되어도 나머지는 적용하고, 잘못된 정규식은 그 항목에 <see cref="SoftwareDefinition.Error"/> 로 남깁니다.
    /// </summary>
    public static List<SoftwareDefinition>? ParseUser(string json, List<string> problems)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        }
        catch (JsonException ex)
        {
            problems.Add(DescribeJsonError(ex));
            return null;
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                problems.Add(Loc.T("파일 전체를 { } 로 감싸야 합니다. 사용자 감지 목록은 적용하지 않았습니다."));
                return null;
            }

            var lists = root.EnumerateObject().Where(p => p.Name.Equals("software", StringComparison.OrdinalIgnoreCase)).ToList();
            if (lists.Count == 0)
            {
                problems.Add(Loc.T("\"software\": [ ... ] 목록이 없습니다."));
            }
            else if (lists.Count > 1)
            {
                problems.Add(Loc.F("\"software\" 목록이 {0}개 있습니다. 항목은 하나의 목록 [ ] 안에 쉼표로 이어서 넣으세요. (이번에는 모두 읽었습니다)", lists.Count));
            }

            var result = new List<SoftwareDefinition>();
            var index = 0;
            foreach (var list in lists)
            {
                if (list.Value.ValueKind != JsonValueKind.Array)
                {
                    problems.Add(Loc.T("\"software\" 는 [ ] 목록이어야 합니다."));
                    continue;
                }

                foreach (var element in list.Value.EnumerateArray())
                {
                    index++;
                    SoftwareDefinition? definition;
                    try
                    {
                        definition = element.Deserialize<SoftwareDefinition>(JsonOptions);
                    }
                    catch (JsonException ex)
                    {
                        problems.Add(Loc.F("{0}번째 항목을 읽지 못했습니다: {1}", index, FirstSentence(ex.Message)));
                        continue;
                    }

                    if (definition is null)
                    {
                        continue;
                    }

                    var label = string.IsNullOrWhiteSpace(definition.Name) ? Loc.F("{0}번째 항목", index) : definition.Name;
                    if (string.IsNullOrWhiteSpace(definition.Name) || string.IsNullOrWhiteSpace(definition.Category))
                    {
                        problems.Add(Loc.F("{0}: category 와 name 은 꼭 적어야 합니다.", label));
                        continue;
                    }

                    if (!KnownCategories.Contains(definition.Category, StringComparer.OrdinalIgnoreCase))
                    {
                        problems.Add(Loc.F("{0}: category '{1}' 는 {2} 중 하나여야 합니다.", label, definition.Category, CategoryListText));
                        continue;
                    }

                    definition.Error = SoftwareDetector.ValidatePatterns(definition);
                    if (definition.Error is not null)
                    {
                        problems.Add($"{label}: {definition.Error}");
                    }

                    result.Add(definition);
                }
            }

            return result;
        }
    }

    /// <summary>JSON 형식 오류를 줄 번호와 흔한 원인으로 설명합니다.</summary>
    private static string DescribeJsonError(JsonException ex)
    {
        var message = ex.Message;
        var hint = message.Contains("Expected either ','", StringComparison.Ordinal) || message.Contains("is invalid after a value", StringComparison.Ordinal)
            ? Loc.T("앞 줄 끝에 쉼표(,)가 빠졌을 수 있습니다.")
            : message.Contains("is an invalid escapable character", StringComparison.Ordinal)
                ? Loc.T("경로·정규식의 \\ 는 \\\\ 처럼 두 번 써야 합니다.")
                : message.Contains("is an invalid start of a property name", StringComparison.Ordinal)
                    ? Loc.T("이름은 \"name\" 처럼 큰따옴표로 감싸야 합니다.")
                    : FirstSentence(message);
        return ex.LineNumber is { } line
            ? Loc.F("{0}번째 줄 근처 JSON 형식 오류: {1} 사용자 감지 목록은 적용하지 않았습니다.", line + 1, hint)
            : Loc.F("JSON 형식 오류: {0} 사용자 감지 목록은 적용하지 않았습니다.", hint);
    }

    private static string FirstSentence(string message)
    {
        var end = message.IndexOf(". ", StringComparison.Ordinal);
        return end > 0 ? message[..(end + 1)] : message;
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

    /// <summary>정의가 잘못되어 감지하지 못한 이유 (잘못된 정규식 등).</summary>
    public string? Error { get; init; }
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

        // 잘못된 정규식은 분류 전체를 실패시키지 않고 이 항목만 '확인 필요'로
        if ((definition.Error ?? ValidatePatterns(definition)) is { } error)
        {
            return new SoftwareDetection(definition, [], [], [], []) { Error = error };
        }

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

    /// <summary>
    /// 기본 목록에 정의가 없는 분류(컴퓨터·통신)에 사용자가 넣은 항목을 "추가 감지 항목" 카드로 붙입니다.
    /// 항목이 없으면 카드를 만들지 않습니다.
    /// </summary>
    public static void AddUserDefinedSection(CollectionContext context, string category, ProviderResult result)
    {
        var section = new InfoSection(Loc.T("추가 감지 항목"), note: Loc.F("감지 목록 파일(SunTool.software.json)에 category \"{0}\" 로 넣은 항목입니다.", category));
        foreach (var definition in context.Software.ForCategory(category))
        {
            var items = ToItems(Detect(definition, context.InstalledPrograms));
            section.Items.AddRange(items);
            if (definition.Summary)
            {
                result.Summary.AddRange(items.Select(i => i with { Detail = null }));
            }
        }

        if (section.Items.Count > 0)
        {
            result.Sections.Add(section);
        }
    }

    /// <summary>감지 결과를 화면 항목으로 바꿉니다 (같은 제품이 여러 버전 설치되면 각각 한 줄).</summary>
    public static List<InfoItem> ToItems(SoftwareDetection detection)
    {
        var definition = detection.Definition;
        if (detection.Error is not null)
        {
            return [new InfoItem(definition.Name, Loc.T("감지 목록 오류"), InfoStatus.Warning, detection.Error)];
        }

        var evidence = new List<string>();
        evidence.AddRange(detection.EnvVars.Select(e => $"{e.Name}={e.Value}"));
        evidence.AddRange(detection.Files.Select(f => $"{f.Path}{(f.Version is null ? string.Empty : $" (v{f.Version})")}"));
        evidence.AddRange(detection.RegistryKeys.Select(k => $@"HKLM\{k}"));

        if (detection.Programs.Count > 0)
        {
            var multiple = detection.Programs.Count > 1;
            return detection.Programs.Select((program, index) =>
            {
                var details = new List<string> { Loc.F("설치 이름: {0}", program.DisplayName) };
                if (program.Publisher.Length > 0)
                {
                    details.Add(Loc.F("제조사: {0}", program.Publisher));
                }

                if (program.InstallLocation.Length > 0)
                {
                    details.Add(Loc.F("경로: {0}", program.InstallLocation));
                }

                if (index == 0)
                {
                    details.AddRange(evidence);
                }

                var version = !string.IsNullOrWhiteSpace(program.DisplayVersion)
                    ? program.DisplayVersion
                    : detection.FileVersions.FirstOrDefault() ?? Loc.T("설치됨 (버전 정보 없음)");

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
                    versions.Count > 0 ? string.Join(", ", versions) : Loc.T("설치 흔적 있음 (버전 확인 불가)"),
                    versions.Count > 0 ? InfoStatus.Installed : InfoStatus.Warning,
                    string.Join("  ·  ", evidence)),
            ];
        }

        return [new InfoItem(definition.Name, Loc.T("설치되지 않음"), InfoStatus.NotInstalled)];
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

    /// <summary>정의의 정규식(programPatterns, excludePatterns, versionFromFileName)을 검사합니다. 문제가 없으면 null.</summary>
    public static string? ValidatePatterns(SoftwareDefinition definition)
    {
        var patterns = definition.ProgramPatterns.Concat(definition.ExcludePatterns);
        if (!string.IsNullOrEmpty(definition.VersionFromFileName))
        {
            patterns = patterns.Append(definition.VersionFromFileName);
        }

        foreach (var pattern in patterns)
        {
            try
            {
                _ = ToRegex(pattern);
            }
            catch (ArgumentException ex)
            {
                // "Invalid pattern '^Notepad++' at offset 10. Nested quantifier '+'." → "Nested quantifier '+'"
                var reason = ex.Message;
                var cut = reason.IndexOf(". ", StringComparison.Ordinal);
                reason = (cut > 0 ? reason[(cut + 2)..] : reason).TrimEnd('.');
                return Loc.F("정규식 '{0}' 이(가) 잘못되었습니다 ({1}). + . ( ) [ ] 같은 글자를 그대로 찾으려면 앞에 \\ 를 붙이세요 (JSON 에서는 \\\\+ 처럼 씁니다).", pattern, reason);
            }
        }

        return null;
    }

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
