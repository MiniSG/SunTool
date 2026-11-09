using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Globalization;
using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using SunTool.Core.Logging;

namespace SunTool.Modules.EventLogViewer.Logic;

/// <summary>
/// Windows 에 등록된 모든 이벤트 원본(공급자)의 이벤트 정의를 모아 ID 로 찾을 수 있게 합니다.
/// 처음 한 번 만들고(수 초) 데이터 폴더에 압축 저장해 다음부터는 바로 불러옵니다.
/// OS 버전이나 표시 언어가 바뀌면 자동으로 다시 만듭니다.
/// </summary>
public sealed partial class EventDefinitionCatalog
{
    // 메시지 ID 의 Customer 비트(29) 또는 Facility(16~27) 가 있으면 이벤트 메시지가 아님(매니페스트의 수준·작업 이름 등)
    private const uint NonEventMask = 0x2FFF0000;
    private const int MaxProviderOnlyResults = 3000;

    private readonly string? _cacheDirectory;
    private readonly IAppLogger _log;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Dictionary<int, List<EventDefinition>> _byId = new();
    private List<EventDefinition> _all = new();

    public EventDefinitionCatalog(string? cacheDirectory, IAppLogger? log = null)
    {
        _cacheDirectory = cacheDirectory;
        _log = log ?? NullLogger.Instance;
    }

    public bool IsLoaded { get; private set; }

    public int ProviderCount { get; private set; }

    public int DefinitionCount => _all.Count;

    public DateTime? BuiltAt { get; private set; }

    public bool LoadedFromCache { get; private set; }

    private static string CacheKey => $"{Environment.OSVersion.Version}|{CultureInfo.CurrentUICulture.Name}";

    private string? CacheFile => _cacheDirectory is null ? null : Path.Combine(_cacheDirectory, "event-definitions.json.gz");

    /// <summary>목록이 없으면 캐시에서 불러오거나 새로 만듭니다.</summary>
    public async Task EnsureLoadedAsync(bool forceRebuild, IProgress<CatalogProgress>? progress, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (IsLoaded && !forceRebuild)
            {
                return;
            }

            await Task.Run(
                () =>
                {
                    if (!forceRebuild && TryLoadCache())
                    {
                        return;
                    }

                    var stopwatch = Stopwatch.StartNew();
                    var names = EventLogSession.GlobalSession.GetProviderNames().ToList();
                    var definitions = Build(names, progress, _log, cancellationToken);
                    Load(definitions, names.Count, DateTime.Now, fromCache: false);
                    _log.Info($"이벤트 정의 목록 생성: 공급자 {names.Count:N0}개, 정의 {definitions.Count:N0}개 ({stopwatch.Elapsed.TotalSeconds:0.0}초)");
                    TrySaveCache();
                },
                cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>미리 만든 정의 목록을 넣습니다 (캐시 로드·테스트용).</summary>
    public void Load(IEnumerable<EventDefinition> definitions, int providerCount, DateTime builtAt, bool fromCache)
    {
        var all = definitions.ToList();
        var byId = new Dictionary<int, List<EventDefinition>>();
        foreach (var definition in all)
        {
            if (!byId.TryGetValue(definition.EventId, out var list))
            {
                byId[definition.EventId] = list = new List<EventDefinition>();
            }

            list.Add(definition);
        }

        _all = all;
        _byId = byId;
        ProviderCount = providerCount;
        BuiltAt = builtAt;
        LoadedFromCache = fromCache;
        IsLoaded = true;
    }

    /// <summary>
    /// ID 로 찾습니다. 원본 필터는 이름 일부만 넣어도 되며, 정확히 일치하는 원본이 맨 위에 옵니다.
    /// ID 없이 원본만 주면 그 원본의 모든 이벤트를 돌려줍니다.
    /// </summary>
    public IReadOnlyList<EventDefinition> Find(int? eventId, string? providerFilter)
    {
        var filter = providerFilter?.Trim() ?? string.Empty;
        IEnumerable<EventDefinition> query;

        if (eventId is { } id)
        {
            query = _byId.TryGetValue(id, out var list) ? list : Enumerable.Empty<EventDefinition>();
            if (filter.Length > 0)
            {
                query = query.Where(d => d.ProviderName.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }
        }
        else if (filter.Length > 0)
        {
            query = _all.Where(d => d.ProviderName.Contains(filter, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            return Array.Empty<EventDefinition>();
        }

        var ordered = query
            .OrderBy(d => string.Equals(d.ProviderName, filter, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
            .ThenBy(d => d.ProviderName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(d => d.EventId);

        return (eventId is null ? ordered.Take(MaxProviderOnlyResults) : ordered).ToList();
    }

    /// <summary>주어진 공급자들의 이벤트 정의를 읽습니다 (매니페스트 + 메시지 파일).</summary>
    public static List<EventDefinition> Build(
        IReadOnlyList<string> providerNames,
        IProgress<CatalogProgress>? progress = null,
        IAppLogger? log = null,
        CancellationToken cancellationToken = default)
    {
        log ??= NullLogger.Instance;
        var definitions = new List<EventDefinition>();
        var tableCache = new Dictionary<string, IReadOnlyList<(uint MessageId, string Text)>>(StringComparer.OrdinalIgnoreCase);
        var failed = 0;

        for (var i = 0; i < providerNames.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var name = providerNames[i];
            var seenIds = new HashSet<int>();
            string? messageFile = null;

            try
            {
                using var metadata = new ProviderMetadata(name);
                messageFile = SafeGet(() => metadata.MessageFilePath);
                foreach (var definition in ReadManifestEvents(metadata, name))
                {
                    if (seenIds.Add(definition.EventId))
                    {
                        definitions.Add(definition);
                    }
                }
            }
            catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException)
            {
                failed++;
            }

            if (!string.IsNullOrWhiteSpace(messageFile))
            {
                foreach (var file in messageFile.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!tableCache.TryGetValue(file, out var table))
                    {
                        table = MessageTableReader.Read(file);
                        tableCache[file] = table;
                    }

                    foreach (var (messageId, text) in table)
                    {
                        if ((messageId & NonEventMask) != 0)
                        {
                            continue;
                        }

                        var description = MessageTableReader.Clean(text);
                        if (!HasLetter(description))
                        {
                            continue; // "%1" 처럼 내용 없는 공용 메시지 제외 (.NET 이벤트 원본 등)
                        }

                        var eventId = (int)(messageId & 0xFFFF);
                        if (!seenIds.Add(eventId))
                        {
                            continue;
                        }

                        definitions.Add(new EventDefinition
                        {
                            ProviderName = name,
                            EventId = eventId,
                            Level = LevelFromSeverity(messageId >> 30),
                            Description = description,
                            Source = EventDefinitionSource.MessageFile,
                        });
                    }
                }
            }

            if ((i + 1) % 20 == 0 || i == providerNames.Count - 1)
            {
                progress?.Report(new CatalogProgress(i + 1, providerNames.Count));
            }
        }

        if (failed > 0)
        {
            log.Debug($"이벤트 정의를 읽지 못한 공급자: {failed}개");
        }

        return definitions;
    }

    private static IEnumerable<EventDefinition> ReadManifestEvents(ProviderMetadata metadata, string name)
    {
        IEnumerable<EventMetadata>? events = SafeGet(() => metadata.Events.ToList());
        if (events is null)
        {
            yield break;
        }

        foreach (var ev in events)
        {
            var level = SafeGet(() => ev.Level?.Value ?? 0);
            var task = SafeGet(() => ev.Task?.DisplayName) ?? string.Empty;
            var description = MessageTableReader.Clean(SafeGet(() => ev.Description));
            yield return new EventDefinition
            {
                ProviderName = name,
                EventId = (int)ev.Id,
                Level = (byte)Math.Clamp(level, 0, 255),
                Task = task,
                Description = description,
                Source = EventDefinitionSource.Manifest,
            };
        }
    }

    private static T? SafeGet<T>(Func<T> getter)
    {
        try
        {
            return getter();
        }
        catch (Exception ex) when (ex is EventLogException or UnauthorizedAccessException or InvalidOperationException or NotSupportedException)
        {
            return default;
        }
    }

    /// <summary>메시지 ID 상위 2비트(심각도) → 이벤트 수준.</summary>
    private static byte LevelFromSeverity(uint severity) => severity switch
    {
        3 => 2, // Error
        2 => 3, // Warning
        _ => 4, // Success / Informational
    };

    private static bool HasLetter(string text) => LetterRegex().IsMatch(text);

    [GeneratedRegex(@"\p{L}")]
    private static partial Regex LetterRegex();

    // ===== 캐시 =====

    private bool TryLoadCache()
    {
        var path = CacheFile;
        if (path is null || !File.Exists(path))
        {
            return false;
        }

        try
        {
            using var file = File.OpenRead(path);
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            var cache = JsonSerializer.Deserialize<CacheData>(gzip);
            if (cache is null || cache.Key != CacheKey || cache.Definitions is null)
            {
                _log.Info("이벤트 정의 캐시가 현재 Windows 버전/언어와 달라 새로 만듭니다.");
                return false;
            }

            Load(cache.Definitions, cache.ProviderCount, cache.BuiltAt, fromCache: true);
            _log.Debug($"이벤트 정의 캐시 로드: 정의 {cache.Definitions.Count:N0}개");
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            _log.Warn("이벤트 정의 캐시를 읽지 못해 새로 만듭니다.", ex);
            return false;
        }
    }

    private void TrySaveCache()
    {
        var path = CacheFile;
        if (path is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temp = path + ".tmp";
            using (var file = File.Create(temp))
            using (var gzip = new GZipStream(file, CompressionLevel.Optimal))
            {
                JsonSerializer.Serialize(gzip, new CacheData
                {
                    Key = CacheKey,
                    BuiltAt = BuiltAt ?? DateTime.Now,
                    ProviderCount = ProviderCount,
                    Definitions = _all,
                });
            }

            File.Move(temp, path, overwrite: true);
            _log.Debug($"이벤트 정의 캐시 저장: {path} ({new FileInfo(path).Length / 1024:N0} KB)");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Warn("이벤트 정의 캐시를 저장하지 못했습니다 (다음 실행 때 다시 만듭니다).", ex);
        }
    }

    private sealed class CacheData
    {
        public string Key { get; set; } = string.Empty;

        public DateTime BuiltAt { get; set; }

        public int ProviderCount { get; set; }

        public List<EventDefinition>? Definitions { get; set; }
    }
}
