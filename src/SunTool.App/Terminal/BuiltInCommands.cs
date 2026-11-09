using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using SunTool.Core;
using SunTool.Core.Diagnostics;
using SunTool.Core.Logging;
using SunTool.Core.Modules;
using SunTool.Core.Settings;

namespace SunTool.App.Terminal;

/// <summary>터미널 기본 명령 모음.</summary>
internal static class BuiltInCommands
{
    public static void Register(IServiceCollection services)
    {
        services.AddSingleton<IConsoleCommand, HelpCommand>();
        services.AddSingleton<IConsoleCommand, ClearCommand>();
        services.AddSingleton<IConsoleCommand, LevelCommand>();
        services.AddSingleton<IConsoleCommand, ModulesCommand>();
        services.AddSingleton<IConsoleCommand, PathsCommand>();
        services.AddSingleton<IConsoleCommand, EnvCommand>();
        services.AddSingleton<IConsoleCommand, GcCommand>();
        services.AddSingleton<IConsoleCommand, SettingsCommand>();
        services.AddSingleton<IConsoleCommand, TestLogCommand>();
    }

    private sealed class HelpCommand : IConsoleCommand
    {
        public string Name => "help";

        public string Usage => "help";

        public string Description => "사용 가능한 명령 목록을 보여줍니다.";

        public void Execute(IReadOnlyList<string> args, IConsoleContext context)
        {
            context.Write("사용 가능한 명령:");
            foreach (var command in context.Commands.OrderBy(c => c.Name))
            {
                context.Write($"  {command.Usage,-40} {command.Description}");
            }

            context.Write("단축키: F12 / Ctrl+` 터미널 열기·닫기, ↑↓ 명령 기록");
        }
    }

    private sealed class ClearCommand : IConsoleCommand
    {
        public string Name => "clear";

        public string Usage => "clear";

        public string Description => "터미널 화면과 메모리 로그 버퍼를 비웁니다 (로그 파일은 유지).";

        public void Execute(IReadOnlyList<string> args, IConsoleContext context) => context.ClearOutput();
    }

    private sealed class LevelCommand(ILogService logs) : IConsoleCommand
    {
        public string Name => "level";

        public string Usage => "level [trace|debug|info|warning|error]";

        public string Description => "수집할 최소 로그 레벨을 확인/변경합니다.";

        public void Execute(IReadOnlyList<string> args, IConsoleContext context)
        {
            if (args.Count == 0)
            {
                context.Write($"현재 수집 레벨: {logs.MinimumLevel}");
                return;
            }

            if (Enum.TryParse<LogLevel>(args[0], ignoreCase: true, out var level))
            {
                logs.MinimumLevel = level;
                context.Write($"수집 레벨을 {level}(으)로 바꿨습니다.");
            }
            else
            {
                context.Write($"알 수 없는 레벨: {args[0]}", LogLevel.Warning);
            }
        }
    }

    private sealed class ModulesCommand(IReadOnlyList<IToolModule> modules) : IConsoleCommand
    {
        public string Name => "modules";

        public string Usage => "modules";

        public string Description => "로드된 기능 모듈 목록을 보여줍니다.";

        public void Execute(IReadOnlyList<string> args, IConsoleContext context)
        {
            foreach (var module in modules)
            {
                context.Write($"  [{module.Order}] {module.Id,-22} {module.DisplayName}  ({module.GetType().Assembly.GetName().Name})");
            }
        }
    }

    private sealed class PathsCommand(ISettingsService settings, ILogService logs) : IConsoleCommand
    {
        public string Name => "paths";

        public string Usage => "paths";

        public string Description => "실행 파일, 설정, 로그, 되돌리기 기록 위치를 보여줍니다.";

        public void Execute(IReadOnlyList<string> args, IConsoleContext context)
        {
            context.Write($"  실행 위치   : {AppPaths.BaseDirectory}");
            context.Write($"  데이터 위치 : {AppPaths.DataDirectory} ({(AppPaths.IsPortable ? "포터블" : "AppData")})");
            context.Write($"  설정 파일   : {settings.FilePath}");
            context.Write($"  로그 파일   : {logs.LogFilePath ?? "(기록 안 함)"}");
            context.Write($"  되돌리기    : {AppPaths.UndoDirectory}");
        }
    }

    private sealed class EnvCommand : IConsoleCommand
    {
        public string Name => "env";

        public string Usage => "env";

        public string Description => "버전, 런타임, OS, 메모리 정보를 보여줍니다.";

        public void Execute(IReadOnlyList<string> args, IConsoleContext context)
        {
            using var process = Process.GetCurrentProcess();
            context.Write($"  SunTool     : v{AppInfo.Version}");
            context.Write($"  .NET        : {Environment.Version} ({(Environment.Is64BitProcess ? "x64" : "x86")})");
            context.Write($"  OS          : {Environment.OSVersion}");
            context.Write($"  PID         : {Environment.ProcessId}");
            context.Write($"  작업 메모리 : {process.WorkingSet64 / 1024 / 1024:N0} MB");
            context.Write($"  관리 힙     : {GC.GetTotalMemory(false) / 1024 / 1024:N0} MB");
            context.Write($"  가동 시간   : {DateTime.Now - process.StartTime:hh\\:mm\\:ss}");
        }
    }

    private sealed class GcCommand : IConsoleCommand
    {
        public string Name => "gc";

        public string Usage => "gc";

        public string Description => "가비지 컬렉션을 강제로 실행하고 메모리 변화를 보여줍니다.";

        public void Execute(IReadOnlyList<string> args, IConsoleContext context)
        {
            var before = GC.GetTotalMemory(false);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var after = GC.GetTotalMemory(true);
            context.Write($"관리 힙: {before / 1024:N0} KB → {after / 1024:N0} KB");
        }
    }

    private sealed class SettingsCommand(ISettingsService settings) : IConsoleCommand
    {
        public string Name => "settings";

        public string Usage => "settings [save]";

        public string Description => "설정 파일 내용을 보여주거나(save) 즉시 저장합니다.";

        public void Execute(IReadOnlyList<string> args, IConsoleContext context)
        {
            if (args.Count > 0 && args[0].Equals("save", StringComparison.OrdinalIgnoreCase))
            {
                settings.Save();
                context.Write("설정을 저장했습니다.");
                return;
            }

            if (!File.Exists(settings.FilePath))
            {
                context.Write("아직 저장된 설정 파일이 없습니다. ('settings save' 로 저장)");
                return;
            }

            context.Write(File.ReadAllText(settings.FilePath));
        }
    }

    private sealed class TestLogCommand(ILogService logs) : IConsoleCommand
    {
        public string Name => "testlog";

        public string Usage => "testlog";

        public string Description => "레벨별 샘플 로그를 기록합니다 (터미널 동작 확인용).";

        public void Execute(IReadOnlyList<string> args, IConsoleContext context)
        {
            var log = logs.For("TestLog");
            log.Trace("Trace 레벨 샘플");
            log.Debug("Debug 레벨 샘플");
            log.Info("Info 레벨 샘플");
            log.Warn("Warning 레벨 샘플");
            log.Error("Error 레벨 샘플", new InvalidOperationException("샘플 예외"));
        }
    }
}
