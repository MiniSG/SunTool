using SunTool.Core.Logging;

namespace SunTool.Core.Diagnostics;

/// <summary>
/// 디버그 터미널 창에서 실행할 수 있는 명령.
/// 모듈도 RegisterServices 에서 services.AddSingleton&lt;IConsoleCommand, MyCommand&gt;() 로 추가할 수 있습니다.
/// </summary>
public interface IConsoleCommand
{
    string Name { get; }

    /// <summary>사용법 (예: "level [trace|debug|info|warning|error]").</summary>
    string Usage { get; }

    string Description { get; }

    void Execute(IReadOnlyList<string> args, IConsoleContext context);
}

public interface IConsoleContext
{
    void Write(string text, LogLevel level = LogLevel.Info);

    void ClearOutput();

    IReadOnlyCollection<IConsoleCommand> Commands { get; }
}
