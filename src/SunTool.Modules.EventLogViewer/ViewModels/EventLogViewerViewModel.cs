using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.Core.Logging;
using SunTool.Core.Settings;
using SunTool.Modules.EventLogViewer.Logic;
using SunTool.Wpf.Services;

namespace SunTool.Modules.EventLogViewer.ViewModels;

/// <summary>조회할 로그 하나 (체크박스).</summary>
public sealed partial class LogChannelOption(string name, string displayName, string? hint = null) : ObservableObject
{
    public string Name { get; } = name;

    public string DisplayName { get; } = displayName;

    public string? Hint { get; } = hint;

    [ObservableProperty]
    private bool _isSelected;
}

public sealed partial class EventLogViewerViewModel : ObservableObject
{
    private const string SettingsKey = "EventLogViewer";
    private const int DefaultMaxResults = 3000;

    private readonly IAppLogger _log;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly ISettingsService _settings;
    private IReadOnlyList<EventLogItem> _all = Array.Empty<EventLogItem>();
    private CancellationTokenSource? _cts;

    public EventLogViewerViewModel(
        ILogService logs,
        IDialogService dialogs,
        IClipboardService clipboard,
        ISettingsService settings,
        EventIdLookupViewModel lookup)
    {
        Lookup = lookup;
        _log = logs.For("EventLogViewer");
        _dialogs = dialogs;
        _clipboard = clipboard;
        _settings = settings;

        Channels =
        [
            new LogChannelOption("System", Loc.T("시스템")) { IsSelected = true },
            new LogChannelOption("Application", Loc.T("응용 프로그램")) { IsSelected = true },
            new LogChannelOption("Security", Loc.T("보안"), Loc.T("관리자 권한으로 실행해야 읽을 수 있습니다.")),
            new LogChannelOption("Setup", Loc.T("설치")),
        ];

        LoadSettings();
        SetRange("month"); // 기본: 최근 한 달
    }

    public ObservableCollection<LogChannelOption> Channels { get; }

    /// <summary>'이벤트 ID 검색' 탭.</summary>
    public EventIdLookupViewModel Lookup { get; }

    /// <summary>0 = 로그 조회, 1 = 이벤트 ID 검색.</summary>
    [ObservableProperty]
    private int _selectedTab;

    // ===== 조회 조건 =====

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    private DateTime? _fromDate;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RangeText))]
    private DateTime? _toDate;

    [ObservableProperty]
    private bool _includeCritical = true;

    [ObservableProperty]
    private bool _includeError = true;

    [ObservableProperty]
    private bool _includeWarning = true;

    [ObservableProperty]
    private bool _includeInformation;

    [ObservableProperty]
    private int _maxResults = DefaultMaxResults;

    // ===== 결과 =====

    [ObservableProperty]
    private IReadOnlyList<EventLogItem> _entries = Array.Empty<EventLogItem>();

    [ObservableProperty]
    private EventLogItem? _selectedEntry;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>결과 안에서 특정 수준만 보기 (null = 전체).</summary>
    [ObservableProperty]
    private EventLevelKind? _displayLevel;

    [ObservableProperty]
    private int _totalCount;

    [ObservableProperty]
    private int _criticalCount;

    [ObservableProperty]
    private int _errorCount;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _informationCount;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasResult;

    [ObservableProperty]
    private string _statusText = Loc.T("조건을 고른 뒤 [조회]를 누르세요.");

    public string RangeText => FromDate is { } f && ToDate is { } t
        ? Loc.F("{0:yyyy-MM-dd} ~ {1:yyyy-MM-dd} ({2}일)", f, t, (t.Date - f.Date).Days + 1)
        : Loc.T("기간을 지정하세요");

    partial void OnIsBusyChanged(bool value)
    {
        SearchCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ExportCsvCommand.NotifyCanExecuteChanged();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter();

    partial void OnDisplayLevelChanged(EventLevelKind? value) => ApplyFilter();

    partial void OnEntriesChanged(IReadOnlyList<EventLogItem> value)
    {
        ExportCsvCommand.NotifyCanExecuteChanged();
        SelectedEntry = value.FirstOrDefault();
    }

    // ===== 명령 =====

    /// <summary>기간 바로 지정: today / week / month / 3months</summary>
    [RelayCommand]
    private void SetRange(string? range)
    {
        var today = DateTime.Today;
        ToDate = today;
        FromDate = range switch
        {
            "today" => today,
            "week" => today.AddDays(-7),
            "3months" => today.AddMonths(-3),
            _ => today.AddMonths(-1),
        };
    }

    private bool CanSearch() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        var request = BuildRequest(out var error);
        if (request is null)
        {
            _dialogs.ShowWarning(error!);
            return;
        }

        SaveSettings();
        IsBusy = true;
        _cts = new CancellationTokenSource();
        StatusText = Loc.T("이벤트 로그를 읽는 중...");
        _log.Info($"이벤트 로그 조회: {string.Join(", ", request.LogNames)} / {request.Levels} / {RangeText}");

        try
        {
            var progress = new Progress<int>(n => StatusText = Loc.F("이벤트 로그를 읽는 중... {0:N0}건", n));
            var result = await EventLogSearcher.SearchAsync(request, progress, _log, _cts.Token);

            _all = result.Items;
            HasResult = true;
            UpdateCounts();
            DisplayLevel = null;
            ApplyFilter();

            var status = Loc.F("{0:N0}건을 찾았습니다 ({1:0.0}초).", result.Items.Count, result.Elapsed.TotalSeconds);
            if (result.Truncated)
            {
                status += Loc.F(" 최대 {0:N0}건까지만 표시합니다 — 기간을 줄이거나 최대 개수를 늘리세요.", request.MaxResults);
            }

            if (result.Cancelled)
            {
                status = Loc.T("취소됨 — 그때까지 읽은 ") + status;
            }

            StatusText = status;
            _log.Info(status);
            foreach (var warning in result.Warnings)
            {
                _log.Warn(warning);
            }

            if (result.Warnings.Count > 0)
            {
                _dialogs.ShowWarning(string.Join("\n", result.Warnings));
            }
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("조회 실패: ") + ex.Message;
            _log.Error("이벤트 로그 조회 실패", ex);
            _dialogs.ShowError(Loc.F("이벤트 로그를 읽지 못했습니다.\n\n{0}", ex.Message));
        }
        finally
        {
            IsBusy = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = Loc.T("취소하는 중...");
    }

    /// <summary>수준 칩 클릭: all / critical / error / warning / information</summary>
    [RelayCommand]
    private void ShowLevel(string? level)
    {
        DisplayLevel = level switch
        {
            "critical" => EventLevelKind.Critical,
            "error" => EventLevelKind.Error,
            "warning" => EventLevelKind.Warning,
            "information" => EventLevelKind.Information,
            _ => null,
        };
    }

    private bool CanExport() => !IsBusy && Entries.Count > 0;

    [RelayCommand(CanExecute = nameof(CanExport))]
    private void ExportCsv()
    {
        var path = _dialogs.SaveFile(Loc.T("CSV로 저장"), Loc.T("CSV 파일 (*.csv)|*.csv"), Loc.F("이벤트로그_{0:yyyyMMdd_HHmm}.csv", DateTime.Now));
        if (path is null)
        {
            return;
        }

        try
        {
            EventLogCsv.Write(path, Entries);
            StatusText = Loc.F("{0:N0}건을 저장했습니다: {1}", Entries.Count, path);
            _log.Info(StatusText);
        }
        catch (Exception ex)
        {
            _log.Error("CSV 저장 실패", ex);
            _dialogs.ShowError(Loc.F("저장하지 못했습니다.\n\n{0}", ex.Message));
        }
    }

    [RelayCommand]
    private void CopySelected()
    {
        if (SelectedEntry is not null && _clipboard.SetText(SelectedEntry.Details))
        {
            StatusText = Loc.T("선택한 이벤트 내용을 클립보드에 복사했습니다.");
        }
    }

    /// <summary>선택한 이벤트의 원본+ID 정의를 'ID 검색' 탭에서 보여 줍니다.</summary>
    [RelayCommand]
    private async Task LookupSelectedAsync()
    {
        if (SelectedEntry is null)
        {
            return;
        }

        SelectedTab = 1;
        await Lookup.LookupAsync(SelectedEntry.ProviderName, SelectedEntry.EventId);
    }

    [RelayCommand]
    private void OpenEventViewer()
    {
        try
        {
            Process.Start(new ProcessStartInfo("eventvwr.msc") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error("이벤트 뷰어를 열지 못했습니다.", ex);
        }
    }

    // ===== 내부 =====

    private EventLogSearchRequest? BuildRequest(out string? error)
    {
        error = null;
        var logs = Channels.Where(c => c.IsSelected).Select(c => c.Name).ToList();
        if (logs.Count == 0)
        {
            error = Loc.T("조회할 로그를 하나 이상 고르세요.");
            return null;
        }

        var levels = EventLevels.None;
        if (IncludeCritical)
        {
            levels |= EventLevels.Critical;
        }

        if (IncludeError)
        {
            levels |= EventLevels.Error;
        }

        if (IncludeWarning)
        {
            levels |= EventLevels.Warning;
        }

        if (IncludeInformation)
        {
            levels |= EventLevels.Information;
        }

        if (levels == EventLevels.None)
        {
            error = Loc.T("이벤트 수준(위험·오류·경고·정보)을 하나 이상 고르세요.");
            return null;
        }

        if (FromDate is not { } from || ToDate is not { } to)
        {
            error = Loc.T("시작일과 종료일을 지정하세요.");
            return null;
        }

        if (from.Date > to.Date)
        {
            error = Loc.T("시작일이 종료일보다 늦습니다.");
            return null;
        }

        if (MaxResults < 1)
        {
            MaxResults = DefaultMaxResults;
        }

        var (start, end) = EventLogQueryBuilder.DayRange(from, to);
        return new EventLogSearchRequest
        {
            LogNames = logs,
            Levels = levels,
            From = start,
            To = end,
            MaxResults = MaxResults,
        };
    }

    private void UpdateCounts()
    {
        TotalCount = _all.Count;
        CriticalCount = _all.Count(e => e.LevelKind == EventLevelKind.Critical);
        ErrorCount = _all.Count(e => e.LevelKind == EventLevelKind.Error);
        WarningCount = _all.Count(e => e.LevelKind == EventLevelKind.Warning);
        InformationCount = _all.Count(e => e.LevelKind is EventLevelKind.Information or EventLevelKind.Verbose);
    }

    private void ApplyFilter()
    {
        IEnumerable<EventLogItem> query = _all;
        if (DisplayLevel is { } level)
        {
            query = level == EventLevelKind.Information
                ? query.Where(e => e.LevelKind is EventLevelKind.Information or EventLevelKind.Verbose)
                : query.Where(e => e.LevelKind == level);
        }

        var text = SearchText.Trim();
        if (text.Length > 0)
        {
            query = query.Where(e =>
                e.Message.Contains(text, StringComparison.OrdinalIgnoreCase)
                || e.ProviderName.Contains(text, StringComparison.OrdinalIgnoreCase)
                || e.EventId.ToString() == text);
        }

        Entries = query.ToList();
    }

    private void LoadSettings()
    {
        var saved = _settings.Get(SettingsKey, new EventLogViewerSettings());
        IncludeCritical = saved.IncludeCritical;
        IncludeError = saved.IncludeError;
        IncludeWarning = saved.IncludeWarning;
        IncludeInformation = saved.IncludeInformation;
        MaxResults = saved.MaxResults > 0 ? saved.MaxResults : DefaultMaxResults;
        if (saved.Logs is { Count: > 0 })
        {
            foreach (var channel in Channels)
            {
                channel.IsSelected = saved.Logs.Contains(channel.Name, StringComparer.OrdinalIgnoreCase);
            }
        }
    }

    private void SaveSettings() =>
        _settings.Set(SettingsKey, new EventLogViewerSettings
        {
            IncludeCritical = IncludeCritical,
            IncludeError = IncludeError,
            IncludeWarning = IncludeWarning,
            IncludeInformation = IncludeInformation,
            MaxResults = MaxResults,
            Logs = Channels.Where(c => c.IsSelected).Select(c => c.Name).ToList(),
        });

    private sealed class EventLogViewerSettings
    {
        public bool IncludeCritical { get; set; } = true;

        public bool IncludeError { get; set; } = true;

        public bool IncludeWarning { get; set; } = true;

        public bool IncludeInformation { get; set; }

        public int MaxResults { get; set; } = DefaultMaxResults;

        public List<string>? Logs { get; set; }
    }
}
