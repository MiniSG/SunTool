using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.App.Terminal;
using SunTool.Core;
using SunTool.Core.Common;
using SunTool.Core.Diagnostics;
using SunTool.Core.Logging;
using SunTool.Wpf.Services;

namespace SunTool.App.ViewModels;

/// <summary>
/// 디버그 터미널 창의 ViewModel.
/// 로그 서비스의 실시간 로그를 받아 필터링해 보여주고, 하단 입력줄에서 명령을 실행합니다.
/// </summary>
public sealed partial class LogConsoleViewModel : ObservableObject, IConsoleContext
{
    private const int MaxStoredEntries = 10_000;
    private const int MaxVisibleEntries = 5_000;

    private readonly ILogService _logs;
    private readonly ConsoleCommandProcessor _processor;
    private readonly IUiDispatcher _ui;
    private readonly IClipboardService _clipboard;
    private readonly IDialogService _dialogs;

    private readonly List<LogEntry> _all = new();
    private readonly ConcurrentQueue<LogEntry> _pending = new();
    private readonly List<string> _history = new();
    private int _historyIndex;
    private int _flushScheduled;

    public LogConsoleViewModel(
        ILogService logs,
        ConsoleCommandProcessor processor,
        IUiDispatcher ui,
        IClipboardService clipboard,
        IDialogService dialogs)
    {
        _logs = logs;
        _processor = processor;
        _ui = ui;
        _clipboard = clipboard;
        _dialogs = dialogs;
        _collectLevel = logs.MinimumLevel;

        _all.AddRange(logs.Snapshot());
        Rebuild();
        logs.EntryWritten += OnEntryWritten;

        Write(Loc.T("SunTool 디버그 터미널. 'help' 를 입력하면 명령 목록을 볼 수 있습니다."));
    }

    /// <summary>새 항목이 추가된 뒤 View 가 맨 아래로 스크롤하도록 알립니다.</summary>
    public event EventHandler? ScrollToEndRequested;

    public ObservableCollection<LogEntry> Entries { get; } = new();

    public IReadOnlyList<EnumOption<LogLevel>> Levels { get; } =
    [
        new(LogLevel.Trace, Loc.T("Trace 이상")),
        new(LogLevel.Debug, Loc.T("Debug 이상")),
        new(LogLevel.Info, Loc.T("Info 이상")),
        new(LogLevel.Warning, Loc.T("Warning 이상")),
        new(LogLevel.Error, Loc.T("Error 만")),
    ];

    public string LogFilePath => _logs.LogFilePath ?? Loc.T("(파일 기록 안 함)");

    public IReadOnlyCollection<IConsoleCommand> Commands => _processor.Commands;

    /// <summary>화면 표시 필터 레벨.</summary>
    [ObservableProperty]
    private LogLevel _filterLevel = LogLevel.Trace;

    /// <summary>로그 서비스가 수집하는 최소 레벨 (낮출수록 자세히 기록).</summary>
    [ObservableProperty]
    private LogLevel _collectLevel;

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _commandText = string.Empty;

    [ObservableProperty]
    private string _countText = string.Empty;

    partial void OnFilterLevelChanged(LogLevel value) => Rebuild();

    partial void OnSearchTextChanged(string value) => Rebuild();

    partial void OnIsPausedChanged(bool value)
    {
        if (!value)
        {
            Rebuild();
        }
    }

    partial void OnCollectLevelChanged(LogLevel value) => _logs.MinimumLevel = value;

    // ===== IConsoleContext =====

    public void Write(string text, LogLevel level = LogLevel.Info)
    {
        // 명령 출력은 수집 레벨과 무관하게 항상 보이도록 로그 서비스를 거치지 않고 직접 추가합니다.
        var entry = new LogEntry(DateTime.Now, level, "Console", text, null, Environment.CurrentManagedThreadId);
        _pending.Enqueue(entry);
        ScheduleFlush();
    }

    public void ClearOutput()
    {
        _pending.Clear();
        _all.Clear();
        Entries.Clear();
        _logs.ClearBuffer();
        UpdateCount();
    }

    // ===== 명령 =====

    [RelayCommand]
    private void Clear() => ClearOutput();

    [RelayCommand]
    private void CopyAll()
    {
        var text = string.Join(Environment.NewLine, Entries.Select(e => e.ToString()));
        if (!_clipboard.SetText(text))
        {
            _dialogs.ShowWarning(Loc.T("클립보드에 복사하지 못했습니다."));
        }
    }

    public void CopyEntries(IEnumerable<LogEntry> entries)
    {
        var text = string.Join(Environment.NewLine, entries.Select(e => e.ToString()));
        if (text.Length > 0)
        {
            _clipboard.SetText(text);
        }
    }

    [RelayCommand]
    private void SaveToFile()
    {
        var path = _dialogs.SaveFile(Loc.T("로그 저장"), Loc.T("로그 파일 (*.log)|*.log|텍스트 파일 (*.txt)|*.txt"), $"suntool-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        if (path is null)
        {
            return;
        }

        try
        {
            File.WriteAllLines(path, Entries.Select(e => e.ToString()));
            Write(Loc.F("저장했습니다: {0}", path));
        }
        catch (Exception ex)
        {
            Write(Loc.F("저장 실패: {0}", ex.Message), LogLevel.Error);
        }
    }

    [RelayCommand]
    private void OpenLogFolder()
    {
        try
        {
            Directory.CreateDirectory(AppPaths.LogDirectory);
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{AppPaths.LogDirectory}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Write(Loc.F("로그 폴더를 열지 못했습니다: {0}", ex.Message), LogLevel.Error);
        }
    }

    [RelayCommand]
    private void ExecuteCommand()
    {
        var line = CommandText.Trim();
        CommandText = string.Empty;
        if (line.Length == 0)
        {
            return;
        }

        if (_history.Count == 0 || _history[^1] != line)
        {
            _history.Add(line);
        }

        _historyIndex = _history.Count;
        Write($"> {line}");
        _processor.Execute(line, this);
    }

    [RelayCommand]
    private void HistoryPrevious()
    {
        if (_history.Count == 0)
        {
            return;
        }

        _historyIndex = Math.Max(0, _historyIndex - 1);
        CommandText = _history[_historyIndex];
    }

    [RelayCommand]
    private void HistoryNext()
    {
        if (_history.Count == 0)
        {
            return;
        }

        _historyIndex = Math.Min(_history.Count, _historyIndex + 1);
        CommandText = _historyIndex < _history.Count ? _history[_historyIndex] : string.Empty;
    }

    // ===== 내부 =====

    private void OnEntryWritten(LogEntry entry)
    {
        _pending.Enqueue(entry);
        ScheduleFlush();
    }

    /// <summary>로그가 쏟아져도 UI 스레드에는 한 번에 모아서 반영합니다.</summary>
    private void ScheduleFlush()
    {
        if (Interlocked.Exchange(ref _flushScheduled, 1) == 0)
        {
            _ui.Post(Flush);
        }
    }

    private void Flush()
    {
        Volatile.Write(ref _flushScheduled, 0);

        var added = false;
        while (_pending.TryDequeue(out var entry))
        {
            _all.Add(entry);
            if (!IsPaused && Matches(entry))
            {
                Entries.Add(entry);
                added = true;
            }
        }

        if (_all.Count > MaxStoredEntries)
        {
            _all.RemoveRange(0, _all.Count - MaxStoredEntries);
        }

        while (Entries.Count > MaxVisibleEntries)
        {
            Entries.RemoveAt(0);
        }

        UpdateCount();
        if (added && AutoScroll)
        {
            ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Rebuild()
    {
        Entries.Clear();
        foreach (var entry in _all.Where(Matches).TakeLast(MaxVisibleEntries))
        {
            Entries.Add(entry);
        }

        UpdateCount();
        if (AutoScroll)
        {
            ScrollToEndRequested?.Invoke(this, EventArgs.Empty);
        }
    }

    private bool Matches(LogEntry entry)
    {
        if (entry.Level < FilterLevel && entry.Source != "Console")
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(SearchText))
        {
            return true;
        }

        return entry.Message.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || entry.Source.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
               || (entry.Exception?.Contains(SearchText, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private void UpdateCount() => CountText = Loc.F("{0:N0} / {1:N0} 줄", Entries.Count, _all.Count);
}
