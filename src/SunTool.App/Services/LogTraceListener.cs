using System.Diagnostics;
using System.Text;
using SunTool.Core.Logging;

namespace SunTool.App.Services;

/// <summary>System.Diagnostics.Trace / WPF 바인딩 추적 출력을 로그 서비스로 전달합니다.</summary>
internal sealed class LogTraceListener(IAppLogger logger, LogLevel level) : TraceListener
{
    [ThreadStatic]
    private static bool _isWriting;

    private readonly StringBuilder _pending = new();

    public override void Write(string? message)
    {
        lock (_pending)
        {
            _pending.Append(message);
        }
    }

    public override void WriteLine(string? message)
    {
        string text;
        lock (_pending)
        {
            _pending.Append(message);
            text = _pending.ToString();
            _pending.Clear();
        }

        if (_isWriting || string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        try
        {
            _isWriting = true; // 로그 기록 중 다시 Trace 가 호출되는 재귀 방지
            logger.Log(level, text.Trim());
        }
        finally
        {
            _isWriting = false;
        }
    }
}
