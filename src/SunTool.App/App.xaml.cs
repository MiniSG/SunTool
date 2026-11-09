using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using SunTool.App.Terminal;
using SunTool.App.Services;
using SunTool.App.ViewModels;
using SunTool.App.Views;
using SunTool.Core;
using SunTool.Core.Diagnostics;
using SunTool.Core.Logging;
using SunTool.Core.Modules;
using SunTool.Core.Settings;
using SunTool.Wpf.Services;

namespace SunTool.App;

/// <summary>
/// 시작점: 로깅 → 예외 처리 → DI 구성(공통 서비스 + 모듈) → 메인 창 표시.
/// 명령줄 옵션: --console (시작 시 터미널 창 열기), --trace (가장 자세한 로그 수집)
/// </summary>
public partial class App : Application
{
    private LogService? _logService;
    private ServiceProvider? _services;
    private IAppLogger _log = NullLogger.Instance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        var args = e.Args.Select(a => a.ToLowerInvariant()).ToHashSet();
        _logService = new LogService(AppPaths.LogDirectory, args.Contains("--trace") ? LogLevel.Trace : LogLevel.Debug);
        _log = _logService.For("App");
        HookUnhandledExceptions();
        HookWpfTrace(_logService);

        _log.Info($"SunTool 시작 (v{AppInfo.Version}, .NET {Environment.Version}, {(Environment.Is64BitProcess ? "x64" : "x86")})");
        _log.Info($"데이터 위치: {AppPaths.DataDirectory} ({(AppPaths.IsPortable ? "포터블" : "AppData")})");

        try
        {
            _services = ConfigureServices(_logService);

            // 화면 언어: 설정(Shell.Language) 또는 --lang=en / --lang=ko
            var langArg = args.FirstOrDefault(a => a.StartsWith("--lang=", StringComparison.Ordinal))?["--lang=".Length..];
            Loc.SetLanguage(langArg ?? _services.GetRequiredService<ISettingsService>().Get<string?>(MainViewModel.LanguageKey, null));
            Loc.RegisterLoadedAssemblies();
            _log.Info($"화면 언어: {Loc.Language} (번역 {Loc.Count}개)");

            var mainWindow = new MainWindow { DataContext = _services.GetRequiredService<MainViewModel>() };
            MainWindow = mainWindow;
            mainWindow.Closed += (_, _) => _services?.GetService<IConsoleWindowService>()?.CloseForShutdown();
            mainWindow.Show();

            if (args.Contains("--console"))
            {
                _services.GetRequiredService<IConsoleWindowService>().Show();
            }
        }
        catch (Exception ex)
        {
            _log.Error("시작 중 치명적 오류", ex);
            MessageBox.Show(Loc.F("프로그램을 시작하지 못했습니다.\n\n{0}\n\n로그: {1}", ex.Message, _logService.LogFilePath),
                "SunTool", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        try
        {
            _services?.GetService<ISettingsService>()?.Save();
            _log.Info("SunTool 종료");
            _services?.Dispose();
        }
        finally
        {
            _logService?.Dispose();
            base.OnExit(e);
        }
    }

    private ServiceProvider ConfigureServices(LogService logService)
    {
        var services = new ServiceCollection();

        // 공통 서비스
        services.AddSingleton<ILogService>(logService);
        services.AddSingleton<ISettingsService>(_ =>
            new JsonSettingsService(AppPaths.SettingsFile, logService.For("Settings")));
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IUiDispatcher>(new WpfUiDispatcher(Dispatcher));
        services.AddSingleton<IConsoleWindowService, ConsoleWindowService>();

        // 터미널 명령
        services.AddSingleton<ConsoleCommandProcessor>();
        BuiltInCommands.Register(services);

        // 기능 모듈
        var modules = ModuleCatalog.Discover(ModuleAssemblies.All, logService.For("Modules"));
        services.AddSingleton<IReadOnlyList<IToolModule>>(modules);
        foreach (var module in modules)
        {
            module.RegisterServices(services);
        }

        // 셸 ViewModel
        services.AddSingleton<MainViewModel>();
        services.AddSingleton<LogConsoleViewModel>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true });
    }

    private void HookUnhandledExceptions()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            _log.Error($"처리되지 않은 예외 (종료 여부: {args.IsTerminating})", args.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            _log.Error("관찰되지 않은 Task 예외", args.Exception);
            args.SetObserved();
        };
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        _log.Error("처리되지 않은 UI 예외", e.Exception);
        e.Handled = true;

        // 디버깅을 돕기 위해 터미널 창을 자동으로 띄웁니다.
        _services?.GetService<IConsoleWindowService>()?.Show();
        MessageBox.Show(
            Loc.F("예기치 않은 오류가 발생했습니다.\n\n{0}\n\n자세한 내용은 터미널 창(F12)을 확인하세요.", e.Exception.Message),
            Loc.T("SunTool 오류"), MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private static void HookWpfTrace(ILogService logService)
    {
        // WPF 바인딩 오류(경로 오타 등)를 터미널에서 바로 볼 수 있게 연결합니다.
        PresentationTraceSources.Refresh();
        PresentationTraceSources.DataBindingSource.Listeners.Add(new LogTraceListener(logService.For("WPF.Binding"), LogLevel.Warning));
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;

        // Debug.WriteLine / Trace.WriteLine 출력도 터미널로 보냅니다.
        Trace.Listeners.Add(new LogTraceListener(logService.For("Trace"), LogLevel.Debug));
    }
}
