namespace SunTool.Core.Logging;

/// <summary>앱 전체 로그 허브. 메모리 버퍼 + 파일 기록 + 실시간 구독(터미널 창)을 제공합니다.</summary>
public interface ILogService
{
    /// <summary>이 레벨보다 낮은 로그는 버립니다.</summary>
    LogLevel MinimumLevel { get; set; }

    /// <summary>현재 기록 중인 로그 파일 경로 (파일 기록 불가 시 null).</summary>
    string? LogFilePath { get; }

    /// <summary>새 로그가 기록될 때마다 발생합니다. 호출 스레드는 임의의 스레드입니다.</summary>
    event Action<LogEntry>? EntryWritten;

    IAppLogger For(string source);

    void Write(LogLevel level, string source, string message, Exception? exception = null);

    /// <summary>메모리 버퍼에 남아 있는 최근 로그.</summary>
    IReadOnlyList<LogEntry> Snapshot();

    /// <summary>메모리 버퍼를 비웁니다 (파일은 유지).</summary>
    void ClearBuffer();
}

/// <summary>특정 소스(모듈/클래스) 이름이 붙은 로거.</summary>
public interface IAppLogger
{
    string Source { get; }

    void Log(LogLevel level, string message, Exception? exception = null);
}

public static class AppLoggerExtensions
{
    public static void Trace(this IAppLogger logger, string message) => logger.Log(LogLevel.Trace, message);

    public static void Debug(this IAppLogger logger, string message) => logger.Log(LogLevel.Debug, message);

    public static void Info(this IAppLogger logger, string message) => logger.Log(LogLevel.Info, message);

    public static void Warn(this IAppLogger logger, string message, Exception? exception = null) =>
        logger.Log(LogLevel.Warning, message, exception);

    public static void Error(this IAppLogger logger, string message, Exception? exception = null) =>
        logger.Log(LogLevel.Error, message, exception);
}

/// <summary>테스트 등에서 쓰는 아무것도 하지 않는 로거.</summary>
public sealed class NullLogger : IAppLogger
{
    public static readonly NullLogger Instance = new();

    public string Source => "Null";

    public void Log(LogLevel level, string message, Exception? exception = null)
    {
    }
}
