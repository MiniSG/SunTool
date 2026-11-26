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
    private SingleInstance? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 단일 exe 배포에서 PenImc_cor3.dll(펜/스타일러스 네이티브) 로드 실패로 생기는 크래시 방지.
        // 장치·디스플레이 변경(예: 크롬 원격 데스크톱 연결) 시 WPF 가 구형 WISP 스타일러스 스택으로
        // 태블릿 장치를 다시 조회하다 penimc 를 못 찾는 문제가 있어, WM_POINTER 스택을 쓰게 바꿉니다.
        // 펜/터치를 쓰지 않는 앱이라 마우스·키보드 입력에는 영향이 없습니다. 첫 창이 뜨기 전에 설정해야 합니다.
        AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", true);

        base.OnStartup(e);

        var args = e.Args.Select(a => a.ToLowerInvariant()).ToHashSet();
        _logService = new LogService(AppPaths.LogDirectory, args.Contains("--trace") ? LogLevel.Trace : LogLevel.Debug);
        _log = _logService.For("App");
        HookUnhandledExceptions();
        HookWpfTrace(_logService);

        _log.Info($"SunTool 시작 (v{AppInfo.Version}, .NET {Environment.Version}, {(Environment.Is64BitProcess ? "x64" : "x86")})");
        _log.Info($"데이터 위치: {AppPaths.DataDirectory} ({(AppPaths.IsPortable ? "포터블" : "AppData")})");

        // 중복 실행 처리: 이미 실행 중이면 새 창을 열지, 기존 창을 쓸지 묻습니다.
        // (--relaunch 는 언어 전환 재시작이라 묻지 않고 기존 뮤텍스를 넘겨받습니다.)
        var isRelaunch = args.Contains("--relaunch");
        _singleInstance = SingleInstance.Acquire(waitForRelease: isRelaunch);
        // 뮤텍스(새 버전끼리) 또는 같은 이름의 다른 프로세스(뮤텍스 없는 구버전 포함)로 이미 실행 중인지 봅니다.
        var alreadyRunning = !_singleInstance.IsFirstInstance || SingleInstance.AnyOtherProcessRunning();
        if (alreadyRunning && !isRelaunch)
        {
            var choice = MessageBox.Show(
                "SunTool 이 이미 실행 중입니다. 새 창을 하나 더 열까요?\n\n[예] 새 창 열기    [아니요] 기존 창 보기\n\n" +
                "(SunTool is already running. Open another window?\nYes: open a new window    No: show the existing one)",
                "SunTool", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (choice != MessageBoxResult.Yes)
            {
                _log.Info("이미 실행 중 — 기존 창을 앞으로 가져오고 새 인스턴스는 종료");
                SingleInstance.SignalExistingToForeground();       // 새 버전: 신호로 확실히 활성화
                SingleInstance.TryActivateOtherProcessWindow();    // 구버전: 창 핸들로 최선 노력 활성화
                _singleInstance.Dispose();
                _singleInstance = null;
                Shutdown(0);
                return;
            }

            _log.Info("이미 실행 중 — 사용자가 새 창 열기를 선택");
        }

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

            // 최초 인스턴스만, 뒤에 뜨는 두 번째 인스턴스의 "앞으로 와라" 신호를 듣습니다.
            if (_singleInstance?.IsFirstInstance == true)
            {
                _singleInstance.ListenForActivation(() =>
                    Dispatcher.Invoke(() => (MainWindow as Views.MainWindow)?.BringToForeground()));
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
            _singleInstance?.Dispose();
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
