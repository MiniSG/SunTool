using Microsoft.Extensions.DependencyInjection;
using SunTool.Core.Diagnostics;
using SunTool.Core.Logging;
using SunTool.Modules.Comm.ViewModels;

namespace SunTool.Modules.Comm.Logic;

/// <summary>
/// 터미널 명령: comm ports | status | hex | text | check
/// COM 포트를 열지 않습니다 (목록 확인과 변환·계산만).
/// </summary>
public sealed class CommConsoleCommand(SerialPortCatalog catalog, IServiceProvider services) : IConsoleCommand
{
    public string Name => "comm";

    public string Usage => "comm [ports|status|hex|text|check]";

    public string Description => "통신 테스트: COM 포트 목록, 연결 상태, 텍스트↔HEX 변환, 체크섬·CRC 계산 (포트는 열지 않음).";

    public void Execute(IReadOnlyList<string> args, IConsoleContext context)
    {
        var sub = args.Count > 0 ? args[0].ToLowerInvariant() : "help";
        var rest = string.Join(' ', args.Skip(1));
        switch (sub)
        {
            case "ports":
                WritePorts(context);
                break;
            case "status":
                WriteStatus(context);
                break;
            case "hex":
                WriteHex(rest, context);
                break;
            case "text":
                WriteText(rest, context);
                break;
            case "check" or "crc":
                WriteChecks(rest, context);
                break;
            default:
                WriteHelp(context);
                break;
        }
    }

    private static void WriteHelp(IConsoleContext context)
    {
        context.Write("comm ports              COM 포트 목록과 장치 이름 (포트는 열지 않음)");
        context.Write("comm status             통신 테스트 화면의 연결 상태와 송수신 개수");
        context.Write("comm hex <텍스트>        텍스트 → HEX  (\\r \\n \\t \\0 \\xHH 사용 가능, 예: comm hex \"ID?\\r\\n\")");
        context.Write("comm text <HEX>         HEX → 텍스트  (예: comm text 02 41 42 03)");
        context.Write("comm check <HEX>        SUM8 · XOR · CRC-16(Modbus/CCITT) 계산  (예: comm check 01 03 00 00 00 0A)");
    }

    private void WritePorts(IConsoleContext context)
    {
        var entries = catalog.Read(SerialPortCatalog.ReadPortNames());
        context.Write($"[COM 포트] {entries.Count}개 (포트는 열지 않고 목록만 읽습니다)");
        foreach (var entry in entries)
        {
            context.Write($"  {entry.Name,-7} {(entry.Description.Length > 0 ? entry.Description : "(장치 이름 없음)")}");
        }

        if (entries.Count == 0)
        {
            context.Write("  지금 인식된 COM 포트가 없습니다. USB-시리얼 변환기를 꽂았다면 장치 관리자에서 드라이버를 확인하세요.", LogLevel.Warning);
        }
    }

    private void WriteStatus(IConsoleContext context)
    {
        var vm = services.GetRequiredService<CommToolViewModel>();
        var state = vm.IsConnected ? "연결됨" : vm.IsConnecting ? "연결 중" : "연결 안 됨";
        context.Write($"[통신 테스트] {state} · 방식 {vm.Kind} · {vm.ConnectionTitle}");
        context.Write(vm.IsRelayKind
            ? $"  1→2 {vm.TxCount:N0}건 · {vm.TxBytes:N0} B   2→1 {vm.RxCount:N0}건 · {vm.RxBytes:N0} B"
            : $"  송신 {vm.TxCount:N0}건 · {vm.TxBytes:N0} B   수신 {vm.RxCount:N0}건 · {vm.RxBytes:N0} B");
        context.Write($"  로그 {vm.Entries.Count:N0}줄 · 상태: {vm.StatusText}", vm.StatusIsError ? LogLevel.Warning : LogLevel.Info);
    }

    private static void WriteHex(string text, IConsoleContext context)
    {
        if (text.Length == 0)
        {
            context.Write("사용법: comm hex <텍스트>   예) comm hex \"ID?\\r\\n\"", LogLevel.Warning);
            return;
        }

        var bytes = ByteCodec.EncodeText(text, TextEncodingKind.Utf8, escapes: true);
        context.Write($"{bytes.Length}바이트: {ByteCodec.ToHex(bytes)}");
    }

    private static void WriteText(string hex, IConsoleContext context)
    {
        if (!TryParse(hex, "text", context, out var bytes))
        {
            return;
        }

        context.Write($"{bytes.Length}바이트: {ByteCodec.ToDisplayText(bytes, TextEncodingKind.Ascii)}");
        context.Write($"  UTF-8 로 읽으면: {ByteCodec.ToDisplayText(bytes, TextEncodingKind.Utf8)}");
    }

    private static void WriteChecks(string hex, IConsoleContext context)
    {
        if (!TryParse(hex, "check", context, out var bytes))
        {
            return;
        }

        var modbus = Checksum.Crc16Modbus(bytes);
        var ccitt = Checksum.Crc16Ccitt(bytes);
        context.Write($"{bytes.Length}바이트: {ByteCodec.ToHex(bytes)}");
        context.Write($"  SUM8          {Checksum.Sum8(bytes):X2}");
        context.Write($"  XOR (BCC)     {Checksum.Xor8(bytes):X2}");
        context.Write($"  CRC-16 Modbus {modbus:X4}   붙이는 순서: 하위 먼저 {modbus & 0xFF:X2} {modbus >> 8:X2} (Modbus RTU 기본)");
        context.Write($"  CRC-16 CCITT  {ccitt:X4}   붙이는 순서: 상위 먼저 {ccitt >> 8:X2} {ccitt & 0xFF:X2}");
    }

    private static bool TryParse(string hex, string sub, IConsoleContext context, out byte[] bytes)
    {
        bytes = [];
        if (hex.Trim().Length == 0)
        {
            context.Write($"사용법: comm {sub} <HEX>   예) comm {sub} 01 03 00 00 00 0A", LogLevel.Warning);
            return false;
        }

        try
        {
            bytes = ByteCodec.ParseHex(hex);
            return true;
        }
        catch (FormatException ex)
        {
            context.Write($"HEX 를 읽지 못했습니다: {ex.Message}", LogLevel.Warning);
            return false;
        }
    }
}
