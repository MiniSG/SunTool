using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.Core;
using SunTool.Core.Logging;
using SunTool.Core.Settings;
using SunTool.Modules.MemoryWatch.Logic;
using SunTool.Wpf.Services;

namespace SunTool.Modules.MemoryWatch.ViewModels;

/// <summary>선택지 하나 (값 + 화면 글자).</summary>
public sealed record Option<T>(T Value, string Text)
{
    public override string ToString() => Text;
}

/// <summary>설정 파일의 "MemoryWatch" 항목.</summary>
public sealed class MemoryWatchSettings
{
    public string? LastProcessName { get; set; }

    public int IntervalSeconds { get; set; } = 5;

    public bool FollowRestart { get; set; } = true;

    public int WarmupMinutes { get; set; } = 10;
}

/// <summary>
/// 메모리 감시: 다른 프로그램의 메모리·핸들·GDI/USER·스레드 수를 일정 간격으로 기록하고
/// 그래프·CSV·참고용 추세 판정으로 보여 줍니다. 대상 프로그램에 붙거나 멈추게 하지 않습니다.
/// </summary>
public sealed partial class MemoryWatchViewModel : ObservableObject, IDisposable
{
    private const string SettingsKey = "MemoryWatch";

    private readonly IAppLogger _log;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly DispatcherTimer _timer;
    private readonly List<MemorySample> _samples = [];
    private readonly List<WatchEvent> _events = [];
    private ProcessSampler? _sampler;
    private CsvRecorder? _csv;
    private int _segmentStart;
    private bool _is32Bit;
    private int _lastPid;
    private bool _busy;
    private bool _loading;
    private DateTime _watchStart;

    public MemoryWatchViewModel(ILogService logs, IDialogService dialogs, ISettingsService settings)
    {
        _log = logs.For("MemoryWatch");
        _dialogs = dialogs;
        _settings = settings;

        ProcessesView = CollectionViewSource.GetDefaultView(Processes);
        ProcessesView.Filter = o => o is ProcessEntry p && Matches(p);

        Cards =
        [
            new ChartCardViewModel(Loc.T("커밋 (Private Bytes)"), [MetricKind.PrivateBytes], [MetricKind.PrivateBytes]),
            new ChartCardViewModel(Loc.T("핸들"), [MetricKind.Handles], [MetricKind.Handles]),
            new ChartCardViewModel(Loc.T("GDI · USER 개체"), [MetricKind.Gdi, MetricKind.User], [MetricKind.Gdi, MetricKind.User]),
            new ChartCardViewModel(Loc.T("커널 풀"), [MetricKind.PagedPool, MetricKind.NonPagedPool], [MetricKind.PagedPool, MetricKind.NonPagedPool]),
            new ChartCardViewModel(Loc.T("가상 크기"), [MetricKind.VirtualSize], [MetricKind.VirtualSize], judgeOnly32Bit: true),
            new ChartCardViewModel(Loc.T("스레드"), [MetricKind.Threads], [MetricKind.Threads]),
        ];

        LoadSettings();
        _timer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher.CurrentDispatcher);
        _timer.Tick += async (_, _) => await TickAsync();
        RefreshProcesses();
    }

    // ===================================================================== 선택지·목록

    public IReadOnlyList<Option<int>> Intervals { get; } =
    [
        new(1, Loc.T("1초")),
        new(5, Loc.T("5초")),
        new(10, Loc.T("10초")),
        new(60, Loc.T("1분")),
    ];

    public ObservableCollection<ProcessEntry> Processes { get; } = [];

    public ICollectionView ProcessesView { get; }

    public IReadOnlyList<ChartCardViewModel> Cards { get; }

    /// <summary>그래프용 전체 기록 (UI 스레드에서만 바뀜).</summary>
    public IReadOnlyList<MemorySample> Samples => _samples;

    public IReadOnlyList<WatchEvent> Events => _events;

    /// <summary>판정 조건 (테스트에서 짧게 바꿀 수 있음). 워밍업은 화면의 값을 따릅니다.</summary>
    public TrendOptions TrendOptions { get; set; } = new();

    [ObservableProperty]
    private string _filterText = string.Empty;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ToggleWatchCommand))]
    private ProcessEntry? _selectedProcess;

    [ObservableProperty]
    private int _intervalSeconds = 5;

    [ObservableProperty]
    private bool _followRestart = true;

    [ObservableProperty]
    private int _warmupMinutes = 10;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(WatchButtonText), nameof(CanEditSetup), nameof(ShowProcessList), nameof(CanTogglePicker))]
    [NotifyCanExecuteChangedFor(nameof(ToggleWatchCommand), nameof(SaveCsvCommand), nameof(ClearRecordsCommand))]
    private bool _isWatching;

    [ObservableProperty]
    private bool _isWaitingRestart;

    [ObservableProperty]
    private string _targetText = string.Empty;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string? _csvPath;

    [ObservableProperty]
    private string _statusText = Loc.T("감시할 프로그램을 고르고 [감시 시작]을 누르세요.");

    [ObservableProperty]
    private bool _statusIsError;

    [ObservableProperty]
    private string? _accessWarning;

    /// <summary>그래프를 다시 그리라는 신호 (기록이 바뀔 때마다 1씩 증가).</summary>
    [ObservableProperty]
    private int _revision;

    public string WatchButtonText => IsWatching ? Loc.T("감시 중지") : Loc.T("감시 시작");

    public bool CanEditSetup => !IsWatching;

    public bool HasRecords => _samples.Count > 0;

    /// <summary>
    /// 감시가 끝난 뒤 기록이 있으면 그래프가 잘 보이도록 프로그램 목록을 접어 둡니다 ([프로그램 목록]으로 다시 열기).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowProcessList))]
    private bool _isPickerOpen = true;

    public bool ShowProcessList => !IsWatching && (IsPickerOpen || !HasRecords);

    public bool CanTogglePicker => !IsWatching && HasRecords;

    // ===================================================================== 명령

    [RelayCommand]
    private void RefreshProcesses()
    {
        var selectedId = SelectedProcess?.Id;
        var list = ProcessSampler.ListProcesses();
        Processes.Clear();
        foreach (var p in list)
        {
            Processes.Add(p);
        }

        var last = _settings.Get(SettingsKey, new MemoryWatchSettings()).LastProcessName;
        SelectedProcess = Processes.FirstOrDefault(p => p.Id == selectedId)
                          ?? (last is null ? null : Processes.FirstOrDefault(p => string.Equals(p.Name, last, StringComparison.OrdinalIgnoreCase)));
    }

    private bool CanToggleWatch() => IsWatching || SelectedProcess is not null;

    [RelayCommand(CanExecute = nameof(CanToggleWatch))]
    private void ToggleWatch()
    {
        if (IsWatching)
        {
            StopWatching(Loc.T("감시를 멈췄습니다."));
            return;
        }

        if (SelectedProcess is not { } target)
        {
            return;
        }

        var sampler = ProcessSampler.Open(target.Id);
        if (sampler is null)
        {
            SetStatus(Loc.F("'{0}' (PID {1}) 이(가) 이미 끝났습니다. [새로고침]을 누르세요.", target.Name, target.Id), isError: true);
            return;
        }

        _samples.Clear();
        _events.Clear();
        _segmentStart = 0;
        foreach (var card in Cards)
        {
            card.Clear();
        }

        _watchStart = DateTime.Now;
        try
        {
            var file = $"memwatch-{MemoryCsv.SafeFileName(sampler.Name)}-{_watchStart:yyyyMMdd-HHmmss}.csv";
            _csv = new CsvRecorder(Path.Combine(AppPaths.LogDirectory, file));
            CsvPath = _csv.Path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _csv = null;
            CsvPath = null;
            _log.Warn("메모리 감시 CSV 파일을 만들지 못했습니다.", ex);
        }

        AttachSampler(sampler);
        IsPickerOpen = false;
        AddEvent(new WatchEvent(_watchStart, Loc.F("감시 시작: {0} (PID {1})", sampler.Name, sampler.Id), IsRestart: false));
        IsWatching = true;
        IsWaitingRestart = false;
        SaveSettings();
        _timer.Interval = TimeSpan.FromSeconds(Math.Max(1, IntervalSeconds));
        _timer.Start();
        _log.Info($"메모리 감시 시작: {sampler.Name} (PID {sampler.Id}), 간격 {IntervalSeconds}초");
        SetStatus(_csv is null
            ? Loc.T("감시 중입니다. (CSV 자동 저장 실패 — 끝난 뒤 [CSV로 저장]을 쓰세요)")
            : Loc.T("감시 중입니다. 측정값은 CSV 에 자동으로 저장됩니다."), isError: _csv is null);
        _ = TickAsync();
    }

    private bool CanUseRecords() => _samples.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUseRecords))]
    private void SaveCsv()
    {
        var name = MemoryCsv.SafeFileName(_sampler?.Name ?? TargetText.Split(' ').FirstOrDefault() ?? "process");
        var path = _dialogs.SaveFile(Loc.T("측정값 CSV 저장"), Loc.T("CSV 파일 (*.csv)|*.csv"), $"memwatch-{name}-{_watchStart:yyyyMMdd-HHmmss}.csv");
        if (path is null)
        {
            return;
        }

        try
        {
            var lines = new List<string> { MemoryCsv.Header };
            var e = 0;
            foreach (var s in _samples)
            {
                while (e < _events.Count && _events[e].Time <= s.Time)
                {
                    lines.Add(MemoryCsv.EventRow(_events[e++], 0));
                }

                lines.Add(MemoryCsv.Row(s, 0));
            }

            lines.AddRange(_events.Skip(e).Select(x => MemoryCsv.EventRow(x, 0)));
            File.WriteAllLines(path, lines, new System.Text.UTF8Encoding(true));
            SetStatus(Loc.F("CSV 로 저장했습니다: {0}", path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("메모리 감시 CSV 저장 실패", ex);
            _dialogs.ShowError(Loc.F("저장하지 못했습니다.\n\n{0}", ex.Message));
        }
    }

    [RelayCommand]
    private void OpenCsvFolder()
    {
        var folder = CsvPath is not null ? Path.GetDirectoryName(CsvPath) : AppPaths.LogDirectory;
        if (folder is null || !Directory.Exists(folder))
        {
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", CsvPath is not null && File.Exists(CsvPath) ? $"/select,\"{CsvPath}\"" : $"\"{folder}\"") { UseShellExecute = true });
    }

    private bool CanClearRecords() => !IsWatching && _samples.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClearRecords))]
    private void ClearRecords()
    {
        _samples.Clear();
        _events.Clear();
        _segmentStart = 0;
        foreach (var card in Cards)
        {
            card.Clear();
        }

        TargetText = string.Empty;
        ProgressText = string.Empty;
        CsvPath = null;
        AccessWarning = null;
        SystemCommitText = string.Empty;
        SystemCommitNearLimit = false;
        Revision++;
        IsPickerOpen = true;
        RaiseRecordState();
        SetStatus(Loc.T("기록을 지웠습니다. (자동 저장된 CSV 파일은 그대로 있습니다)"));
    }

    // ===================================================================== 측정

    private async Task TickAsync()
    {
        if (_busy || !IsWatching)
        {
            return;
        }

        _busy = true;
        try
        {
            if (_sampler is null)
            {
                await TryReattachAsync();
                return;
            }

            var sampler = _sampler;
            var sample = await Task.Run(() => sampler.Sample(DateTime.Now));
            if (!IsWatching || !ReferenceEquals(sampler, _sampler))
            {
                return;
            }

            if (sample is null)
            {
                OnTargetExited();
                return;
            }

            _samples.Add(sample);
            _csv?.Write(MemoryCsv.Row(sample, sampler.Id));
            Refresh();
        }
        catch (Exception ex)
        {
            _log.Warn("메모리 감시 측정 실패", ex);
        }
        finally
        {
            _busy = false;
        }
    }

    private void OnTargetExited()
    {
        var name = _sampler!.Name;
        var pid = _sampler.Id;
        _lastPid = pid;
        AddEvent(new WatchEvent(DateTime.Now, Loc.F("프로그램 종료: {0} (PID {1})", name, pid), IsRestart: false));
        DetachSampler();
        _log.Info($"메모리 감시 대상 종료: {name} (PID {pid})");

        if (FollowRestart)
        {
            IsWaitingRestart = true;
            TargetText = Loc.F("{0} — 다시 켜지기를 기다리는 중", name);
            SetStatus(Loc.F("'{0}' 이(가) 끝났습니다. 같은 이름으로 다시 켜지면 이어서 감시합니다.", name));
            _pendingName = name;
        }
        else
        {
            StopWatching(Loc.F("'{0}' 이(가) 끝나서 감시를 멈췄습니다.", name));
        }

        Revision++;
    }

    private string? _pendingName;

    private async Task TryReattachAsync()
    {
        if (_pendingName is not { } name)
        {
            return;
        }

        var exclude = _lastPid;
        var sampler = await Task.Run(() => ProcessSampler.OpenNewestByName(name, exclude));
        if (sampler is null || !IsWatching)
        {
            sampler?.Dispose();
            return;
        }

        _segmentStart = _samples.Count;
        AttachSampler(sampler);
        IsWaitingRestart = false;
        _pendingName = null;
        AddEvent(new WatchEvent(DateTime.Now, Loc.F("다시 시작: {0} (PID {1})", sampler.Name, sampler.Id), IsRestart: true));
        _log.Info($"메모리 감시 대상 다시 시작: {sampler.Name} (PID {sampler.Id})");
        SetStatus(Loc.F("'{0}' 이(가) 다시 켜져 이어서 감시합니다. 판정은 다시 켜진 뒤의 기록으로 새로 합니다.", sampler.Name));
        Revision++;
    }

    private void AttachSampler(ProcessSampler sampler)
    {
        _sampler = sampler;
        _is32Bit = sampler.Is32Bit;
        TargetText = Loc.F("{0} (PID {1}{2})", sampler.Name, sampler.Id, sampler.Is32Bit ? Loc.T(", 32비트") : string.Empty);
        AccessWarning = sampler.CanReadGui
            ? null
            : Loc.T("GDI·USER 개체 수를 읽을 권한이 없습니다 (관리자·서비스 프로그램). 필요하면 SunTool 을 관리자 권한으로 실행하세요.");
    }

    private void DetachSampler()
    {
        _sampler?.Dispose();
        _sampler = null;
    }

    private void StopWatching(string message)
    {
        _timer.Stop();
        if (IsWatching)
        {
            AddEvent(new WatchEvent(DateTime.Now, Loc.T("감시 끝"), IsRestart: false));
        }

        DetachSampler();
        _pendingName = null;
        _csv?.Dispose();
        _csv = null;
        IsWatching = false;
        IsWaitingRestart = false;
        Refresh();
        SetStatus(message);
        _log.Info($"메모리 감시 끝 (측정 {_samples.Count}회)");
    }

    private void AddEvent(WatchEvent e)
    {
        _events.Add(e);
        _csv?.Write(MemoryCsv.EventRow(e, _sampler?.Id ?? 0));
    }

    private void Refresh()
    {
        var segment = _samples.Count > _segmentStart ? _samples.GetRange(_segmentStart, _samples.Count - _segmentStart) : [];
        var options = TrendOptions with { Warmup = TimeSpan.FromMinutes(Math.Max(0, WarmupMinutes)) };
        foreach (var card in Cards)
        {
            card.Update(segment, options, _is32Bit);
        }

        SystemCommitText = FormatSystemCommit(_samples.Count > 0 ? _samples[^1] : null, out var nearLimit);
        SystemCommitNearLimit = nearLimit;

        var elapsed = DateTime.Now - _watchStart;
        ProgressText = _samples.Count == 0
            ? string.Empty
            : Loc.F("경과 {0} · 측정 {1:N0}회 · 간격 {2}초", TrendAnalyzer.FormatSpan(elapsed), _samples.Count, IntervalSeconds);
        Revision++;
        RaiseRecordState();
    }

    /// <summary>시스템 전체 커밋 (모든 프로그램 합계). 판정하지 않고 참고로만 보여 줍니다.</summary>
    [ObservableProperty]
    private string _systemCommitText = string.Empty;

    /// <summary>시스템 커밋이 한도의 90% 이상.</summary>
    [ObservableProperty]
    private bool _systemCommitNearLimit;

    public static string FormatSystemCommit(MemorySample? last, out bool nearLimit)
    {
        nearLimit = false;
        if (last is null || last.SystemCommit < 0 || last.SystemCommitLimit <= 0)
        {
            return string.Empty;
        }

        const double gb = 1024.0 * 1024 * 1024;
        var ratio = (double)last.SystemCommit / last.SystemCommitLimit;
        nearLimit = ratio >= 0.9;
        var text = Loc.F("시스템 전체 커밋 {0:N1} / {1:N1} GB ({2:P0})", last.SystemCommit / gb, last.SystemCommitLimit / gb, ratio);
        return nearLimit ? text + " · " + Loc.T("한도에 가까움: 메모리를 못 얻는 프로그램이 생길 수 있음") : text;
    }

    private void RaiseRecordState()
    {
        OnPropertyChanged(nameof(HasRecords));
        OnPropertyChanged(nameof(ShowProcessList));
        OnPropertyChanged(nameof(CanTogglePicker));
        SaveCsvCommand.NotifyCanExecuteChanged();
        ClearRecordsCommand.NotifyCanExecuteChanged();
    }

    // ===================================================================== 필터·설정

    partial void OnFilterTextChanged(string value) => ProcessesView.Refresh();

    private bool Matches(ProcessEntry p) =>
        string.IsNullOrWhiteSpace(FilterText)
        || p.Name.Contains(FilterText.Trim(), StringComparison.OrdinalIgnoreCase)
        || p.WindowTitle.Contains(FilterText.Trim(), StringComparison.OrdinalIgnoreCase)
        || p.Id.ToString() == FilterText.Trim();

    partial void OnIntervalSecondsChanged(int value) => SaveSettings();

    partial void OnFollowRestartChanged(bool value) => SaveSettings();

    partial void OnWarmupMinutesChanged(int value)
    {
        SaveSettings();
        if (_samples.Count > 0)
        {
            Refresh();
        }
    }

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var s = _settings.Get(SettingsKey, new MemoryWatchSettings());
            IntervalSeconds = Intervals.Any(i => i.Value == s.IntervalSeconds) ? s.IntervalSeconds : 5;
            FollowRestart = s.FollowRestart;
            WarmupMinutes = Math.Clamp(s.WarmupMinutes, 0, 600);
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveSettings()
    {
        if (_loading)
        {
            return;
        }

        _settings.Set(SettingsKey, new MemoryWatchSettings
        {
            LastProcessName = _sampler?.Name ?? SelectedProcess?.Name ?? _settings.Get(SettingsKey, new MemoryWatchSettings()).LastProcessName,
            IntervalSeconds = IntervalSeconds,
            FollowRestart = FollowRestart,
            WarmupMinutes = WarmupMinutes,
        });
        _settings.Save();
    }

    private void SetStatus(string text, bool isError = false)
    {
        StatusText = text;
        StatusIsError = isError;
    }

    public void Dispose()
    {
        _timer.Stop();
        DetachSampler();
        _csv?.Dispose();
        _csv = null;
    }
}
