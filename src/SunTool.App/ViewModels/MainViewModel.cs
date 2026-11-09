using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.App.Services;
using SunTool.Core;
using SunTool.Core.Logging;
using SunTool.Core.Modules;
using SunTool.Core.Settings;
using SunTool.Wpf.Services;

namespace SunTool.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject
{
    private const string LastModuleKey = "Shell.LastModule";

    private readonly IConsoleWindowService _console;
    private readonly ISettingsService _settings;
    private readonly IUiDispatcher _ui;
    private readonly IAppLogger _log;

    public MainViewModel(
        IReadOnlyList<IToolModule> modules,
        IServiceProvider services,
        IConsoleWindowService console,
        ISettingsService settings,
        ILogService logs,
        IUiDispatcher ui)
    {
        _console = console;
        _settings = settings;
        _ui = ui;
        _log = logs.For("Shell");

        Modules = new ObservableCollection<ModuleItemViewModel>(modules.Select(m => new ModuleItemViewModel(m, services)));
        logs.EntryWritten += OnLogEntryWritten;

        var lastId = settings.Get<string?>(LastModuleKey, null);
        SelectedModule = Modules.FirstOrDefault(m => m.Id == lastId) ?? Modules.FirstOrDefault();
    }

    public string AppTitle => $"SunTool v{AppInfo.Version}";

    public string Version => $"v{AppInfo.Version}";

    public string DataLocationText => AppPaths.IsPortable ? "포터블 모드" : "AppData 모드";

    public ObservableCollection<ModuleItemViewModel> Modules { get; }

    [ObservableProperty]
    private ModuleItemViewModel? _selectedModule;

    [ObservableProperty]
    private object? _currentViewModel;

    [ObservableProperty]
    private string _lastLogMessage = "준비";

    [ObservableProperty]
    private LogLevel _lastLogLevel = LogLevel.Info;

    [ObservableProperty]
    private int _warningCount;

    [ObservableProperty]
    private int _errorCount;

    partial void OnSelectedModuleChanged(ModuleItemViewModel? value)
    {
        if (value is null)
        {
            CurrentViewModel = null;
            return;
        }

        try
        {
            CurrentViewModel = value.GetOrCreateViewModel();
            _settings.Set(LastModuleKey, value.Id);
            _log.Debug($"모듈 전환: {value.DisplayName}");
        }
        catch (Exception ex)
        {
            _log.Error($"모듈 '{value.DisplayName}' 화면을 만들지 못했습니다.", ex);
            CurrentViewModel = null;
        }
    }

    [RelayCommand]
    private void ToggleConsole() => _console.Toggle();

    [RelayCommand]
    private void ResetCounters()
    {
        WarningCount = 0;
        ErrorCount = 0;
    }

    private void OnLogEntryWritten(LogEntry entry)
    {
        if (entry.Level < LogLevel.Info)
        {
            return;
        }

        _ui.Post(() =>
        {
            LastLogMessage = $"[{entry.TimeText}] {entry.Message}";
            LastLogLevel = entry.Level;
            if (entry.Level == LogLevel.Warning)
            {
                WarningCount++;
            }
            else if (entry.Level == LogLevel.Error)
            {
                ErrorCount++;
            }
        });
    }
}
