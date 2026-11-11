using System.IO.Ports;
using SunTool.Modules.Comm.Logic;

namespace SunTool.Modules.Comm.ViewModels;

public enum ConnectionKind
{
    Serial,
    TcpClient,
    TcpServer,
    Udp,
    Relay,
}

public enum LogViewMode
{
    Text,
    Hex,
    Both,
}

/// <summary>선택지 하나 (값 + 화면 글자).</summary>
public sealed record Option<T>(T Value, string Text)
{
    public override string ToString() => Text;
}

/// <summary>설정 파일(SunTool.settings.json)의 "CommTool" 항목. 마지막에 쓴 연결·보내기·보기 설정.</summary>
public sealed class CommToolSettings
{
    public ConnectionKind Kind { get; set; } = ConnectionKind.Serial;

    public string? PortName { get; set; }

    public int BaudRate { get; set; } = 9600;

    public int DataBits { get; set; } = 8;

    public Parity Parity { get; set; } = Parity.None;

    public StopBits StopBits { get; set; } = StopBits.One;

    public Handshake Handshake { get; set; } = Handshake.None;

    public string TcpHost { get; set; } = "127.0.0.1";

    public int TcpPort { get; set; } = 5000;

    public string ServerAddress { get; set; } = "0.0.0.0";

    public int ServerPort { get; set; } = 5000;

    public int UdpLocalPort { get; set; } = 6000;

    public string UdpRemoteHost { get; set; } = "127.0.0.1";

    public int UdpRemotePort { get; set; } = 6001;

    public InputMode InputMode { get; set; } = InputMode.Text;

    public TextEncodingKind Encoding { get; set; } = TextEncodingKind.Ascii;

    public bool Escapes { get; set; } = true;

    public CheckKind Check { get; set; } = CheckKind.None;

    public CheckByteOrder CheckOrder { get; set; } = CheckByteOrder.HighFirst;

    public bool AppendCr { get; set; }

    public bool AppendLf { get; set; }

    public LogViewMode ViewMode { get; set; } = LogViewMode.Both;

    public bool ShowDelta { get; set; } = true;

    public bool AutoScroll { get; set; } = true;

    public int GapMs { get; set; } = 20;

    public bool VerifyReceived { get; set; } = true;

    public bool OnlyMatches { get; set; } = true;

    // ----- 수신 묶기 방식
    public FrameMode FrameMode { get; set; } = FrameMode.Gap;

    public string FrameStart { get; set; } = "02";

    public string FrameEnd { get; set; } = "0D 0A";

    public int FrameLength { get; set; } = 8;

    // ----- 반복 보내기 / 파일 보내기
    public bool RepeatEnabled { get; set; }

    public int RepeatIntervalMs { get; set; } = 1000;

    public int RepeatCount { get; set; }

    public int FileIntervalMs { get; set; } = 500;

    public List<QuickCommand> QuickCommands { get; set; } = [];

    // ----- Steal (중계)
    public string? PortName2 { get; set; }

    public bool ListenOnly { get; set; }

    public int InjectSide { get; set; } = 1;

    public List<string> History { get; set; } = [];
}

/// <summary>빠른 보내기 버튼 하나 (이름, 보낼 내용, 입력 방식).</summary>
public sealed class QuickCommand
{
    public string Name { get; set; } = string.Empty;

    public string Text { get; set; } = string.Empty;

    public InputMode Mode { get; set; } = InputMode.Text;

    /// <summary>버튼에 마우스를 올렸을 때 보이는 내용 (설정 파일에는 저장하지 않음).</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public string ToolTipText => $"[{(Mode == InputMode.Hex ? "HEX" : "TEXT")}] {Text}";
}
