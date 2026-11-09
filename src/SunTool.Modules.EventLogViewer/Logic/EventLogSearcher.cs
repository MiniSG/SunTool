using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Text;
using SunTool.Core.Logging;

namespace SunTool.Modules.EventLogViewer.Logic;

/// <summary>Windows 이벤트 로그를 조건에 맞게 읽어 옵니다 (최신순).</summary>
public static class EventLogSearcher
{
    /// <summary>기본 제공 로그 이름 → 한글 표시 이름.</summary>
    public static readonly IReadOnlyDictionary<string, string> KnownLogs = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["System"] = "시스템",
        ["Application"] = "응용 프로그램",
        ["Security"] = "보안",
        ["Setup"] = "설치",
    };

    public static string DisplayName(string logName) =>
        KnownLogs.TryGetValue(logName, out var name) ? Loc.T(name) : logName;

    public static Task<EventLogSearchResult> SearchAsync(
        EventLogSearchRequest request,
        IProgress<int>? progress,
        IAppLogger? log,
        CancellationToken cancellationToken) =>
        Task.Run(() => Search(request, progress, log, cancellationToken), CancellationToken.None);

    public static EventLogSearchResult Search(
        EventLogSearchRequest request,
        IProgress<int>? progress = null,
        IAppLogger? log = null,
        CancellationToken cancellationToken = default)
    {
        log ??= NullLogger.Instance;
        var stopwatch = Stopwatch.StartNew();
        var xpath = EventLogQueryBuilder.Build(request.Levels, request.From, request.To);
        log.Debug($"이벤트 로그 쿼리: {xpath}");

        var items = new List<EventLogItem>();
        var warnings = new List<string>();
        var truncated = false;
        var cancelled = false;

        foreach (var logName in request.LogNames)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                cancelled = true;
                break;
            }

            var display = DisplayName(logName);
            try
            {
                var query = new EventLogQuery(logName, PathType.LogName, xpath) { ReverseDirection = true };
                using var reader = new EventLogReader(query);
                var countInLog = 0;

                while (true)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        cancelled = true;
                        break;
                    }

                    using var record = reader.ReadEvent();
                    if (record is null)
                    {
                        break;
                    }

                    if (countInLog >= request.MaxResults)
                    {
                        truncated = true;
                        break;
                    }

                    items.Add(Convert(record, logName, display));
                    countInLog++;
                    if (items.Count % 100 == 0)
                    {
                        progress?.Report(items.Count);
                    }
                }

                log.Debug($"{display} 로그: {countInLog}건");
            }
            catch (UnauthorizedAccessException)
            {
                warnings.Add(Loc.F("'{0}' 로그는 관리자 권한이 있어야 읽을 수 있어 건너뛰었습니다.", display));
            }
            catch (EventLogNotFoundException)
            {
                warnings.Add(Loc.F("'{0}' 로그를 찾을 수 없습니다.", display));
            }
            catch (EventLogException ex)
            {
                warnings.Add(Loc.F("'{0}' 로그를 읽지 못했습니다: {1}", display, ex.Message));
                log.Warn($"이벤트 로그 읽기 실패: {logName}", ex);
            }

            if (cancelled)
            {
                break;
            }
        }

        // 여러 로그를 합쳐 최신순 정렬 후 최대 개수로 자릅니다.
        items.Sort((a, b) => b.TimeCreated.CompareTo(a.TimeCreated));
        if (items.Count > request.MaxResults)
        {
            items.RemoveRange(request.MaxResults, items.Count - request.MaxResults);
            truncated = true;
        }

        progress?.Report(items.Count);
        return new EventLogSearchResult(items, warnings, truncated, cancelled, stopwatch.Elapsed);
    }

    private static EventLogItem Convert(EventRecord record, string logName, string display)
    {
        string? message = null;
        try
        {
            message = record.FormatDescription();
        }
        catch (Exception ex) when (ex is EventLogException or InvalidOperationException)
        {
            // 원본(공급자) 메시지 파일이 없는 경우 — 아래에서 대체 메시지 생성
        }

        if (string.IsNullOrWhiteSpace(message))
        {
            message = FallbackMessage(record);
        }

        string task = string.Empty;
        try
        {
            task = record.TaskDisplayName ?? string.Empty;
        }
        catch (Exception ex) when (ex is EventLogException or InvalidOperationException)
        {
            task = record.Task?.ToString() ?? string.Empty;
        }

        return new EventLogItem
        {
            Level = record.Level ?? 0,
            TimeCreated = record.TimeCreated ?? DateTime.MinValue,
            LogName = logName,
            LogDisplayName = display,
            ProviderName = record.ProviderName ?? string.Empty,
            EventId = record.Id,
            TaskCategory = task,
            Message = message.Trim(),
            MachineName = record.MachineName ?? string.Empty,
            UserId = record.UserId?.Value,
            RecordId = record.RecordId,
        };
    }

    private static string FallbackMessage(EventRecord record)
    {
        var builder = new StringBuilder(Loc.T("이 이벤트의 설명을 찾을 수 없습니다 (원본 메시지 파일 없음)."));
        try
        {
            var values = record.Properties.Select(p => p.Value?.ToString()).Where(v => !string.IsNullOrWhiteSpace(v)).ToList();
            if (values.Count > 0)
            {
                builder.AppendLine().Append(Loc.T("이벤트 데이터: ")).Append(string.Join(", ", values));
            }
        }
        catch (EventLogException)
        {
            // 속성도 읽지 못하면 기본 문구만 사용
        }

        return builder.ToString();
    }
}
