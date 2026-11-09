using System.Text;

namespace SunTool.Core.Logging;

public sealed class LogService : ILogService, IDisposable
{
    private const int MaxBufferedEntries = 10_000;
    private const int LogRetentionDays = 14;

    private readonly object _gate = new();
    private readonly Queue<LogEntry> _buffer = new();
    private StreamWriter? _writer;

    public LogService(string? logDirectory, LogLevel minimumLevel = LogLevel.Debug)
    {
        MinimumLevel = minimumLevel;
        if (!string.IsNullOrWhiteSpace(logDirectory))
        {
            TryOpenLogFile(logDirectory);
        }
    }

    public LogLevel MinimumLevel { get; set; }

    public string? LogFilePath { get; private set; }

    public event Action<LogEntry>? EntryWritten;

    public IAppLogger For(string source) => new SourceLogger(this, source);

    public void Write(LogLevel level, string source, string message, Exception? exception = null)
    {
        if (level < MinimumLevel)
        {
            return;
        }

        var entry = new LogEntry(
            DateTime.Now,
            level,
            source,
            message,
            exception?.ToString(),
            Environment.CurrentManagedThreadId);

        lock (_gate)
        {
            _buffer.Enqueue(entry);
            while (_buffer.Count > MaxBufferedEntries)
            {
                _buffer.Dequeue();
            }

            try
            {
                _writer?.WriteLine(entry.ToString());
            }
            catch
            {
                // 디스크 오류로 앱이 죽으면 안 되므로 무시합니다.
            }
        }

        try
        {
            EntryWritten?.Invoke(entry);
        }
        catch
        {
            // 구독자 오류가 로깅을 깨뜨리지 않도록 합니다.
        }
    }

    public IReadOnlyList<LogEntry> Snapshot()
    {
        lock (_gate)
        {
            return _buffer.ToArray();
        }
    }

    public void ClearBuffer()
    {
        lock (_gate)
        {
            _buffer.Clear();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private void TryOpenLogFile(string logDirectory)
    {
        try
        {
            Directory.CreateDirectory(logDirectory);
            DeleteOldLogs(logDirectory);

            var path = Path.Combine(logDirectory, $"suntool-{DateTime.Now:yyyyMMdd}.log");
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            _writer = new StreamWriter(stream, new UTF8Encoding(false)) { AutoFlush = true };
            LogFilePath = path;
        }
        catch
        {
            _writer = null;
            LogFilePath = null;
        }
    }

    private static void DeleteOldLogs(string logDirectory)
    {
        var threshold = DateTime.Now.AddDays(-LogRetentionDays);
        foreach (var file in Directory.EnumerateFiles(logDirectory, "suntool-*.log"))
        {
            try
            {
                if (File.GetLastWriteTime(file) < threshold)
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // 지우지 못한 오래된 로그는 다음 실행 때 다시 시도합니다.
            }
        }
    }

    private sealed class SourceLogger(LogService owner, string source) : IAppLogger
    {
        public string Source { get; } = source;

        public void Log(LogLevel level, string message, Exception? exception = null) =>
            owner.Write(level, Source, message, exception);
    }
}
