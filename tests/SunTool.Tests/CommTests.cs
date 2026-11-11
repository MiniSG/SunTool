using System.Net;
using System.Text;
using SunTool.Modules.Comm.Logic;

namespace SunTool.Tests;

public class CommCodecTests
{
    private static readonly byte[] Digits = Encoding.ASCII.GetBytes("123456789");

    [Theory]
    [InlineData("02 4F 4B", new byte[] { 0x02, 0x4F, 0x4B })]
    [InlineData("024f4b", new byte[] { 0x02, 0x4F, 0x4B })]
    [InlineData("0x02,0x4F, 0x4B", new byte[] { 0x02, 0x4F, 0x4B })]
    [InlineData("2 4F", new byte[] { 0x02, 0x4F })]
    [InlineData("  ", new byte[0])]
    public void ParseHex_AcceptsCommonFormats(string input, byte[] expected) =>
        Assert.Equal(expected, ByteCodec.ParseHex(input));

    [Theory]
    [InlineData("024")]
    [InlineData("0G")]
    public void ParseHex_RejectsInvalid(string input) =>
        Assert.Throws<FormatException>(() => ByteCodec.ParseHex(input));

    [Fact]
    public void EncodeText_InterpretsEscapesOnlyWhenAsked()
    {
        Assert.Equal(new byte[] { 0x41, 0x0D, 0x0A, 0x02, 0x5C, 0x42 }, ByteCodec.EncodeText(@"A\r\n\x02\\B", TextEncodingKind.Ascii, escapes: true));
        Assert.Equal(Encoding.ASCII.GetBytes(@"A\r"), ByteCodec.EncodeText(@"A\r", TextEncodingKind.Ascii, escapes: false));
        Assert.Equal(Encoding.ASCII.GetBytes(@"\q\"), ByteCodec.EncodeText(@"\q\", TextEncodingKind.Ascii, escapes: true));
    }

    [Fact]
    public void EncodeText_EucKr_UsesTwoBytesPerHangul() =>
        Assert.Equal(new byte[] { 0xC7, 0xD1 }, ByteCodec.EncodeText("한", TextEncodingKind.EucKr, escapes: true));

    [Fact]
    public void ToHex_GroupsEveryEightBytes()
    {
        var data = Enumerable.Range(0, 10).Select(i => (byte)i).ToArray();
        Assert.Equal("00 01 02 03 04 05 06 07  08 09", ByteCodec.ToHex(data));
        Assert.Equal(string.Empty, ByteCodec.ToHex([]));
    }

    [Fact]
    public void ToTextPieces_NamesControlCharacters()
    {
        var pieces = ByteCodec.ToTextPieces([0x02, 0x4F, 0x4B, 0x0D, 0x0A], TextEncodingKind.Ascii);
        Assert.Equal(
            [new("STX", true), new("OK", false), new("CR", true), new("LF", true)],
            pieces);
        Assert.Equal("<STX>OK<CR><LF>", ByteCodec.ToDisplayText([0x02, 0x4F, 0x4B, 0x0D, 0x0A], TextEncodingKind.Ascii));
        Assert.Equal("A·", ByteCodec.ToDisplayText([0x41, 0xC7], TextEncodingKind.Ascii));
    }

    [Fact]
    public void ToHexDump_ShowsOffsetHexAndAscii()
    {
        var dump = ByteCodec.ToHexDump(Encoding.ASCII.GetBytes("ABCDEFGHIJKLMNOPQR"));
        var lines = dump.Split(Environment.NewLine);
        Assert.Equal(2, lines.Length);
        Assert.StartsWith("0000  41 42", lines[0]);
        Assert.EndsWith("ABCDEFGHIJKLMNOP", lines[0]);
        Assert.StartsWith("0010  51 52", lines[1]);
        Assert.EndsWith("QR", lines[1]);
    }

    [Fact]
    public void Checksums_MatchKnownVectors()
    {
        Assert.Equal(0x4B37, Checksum.Crc16Modbus(Digits));
        Assert.Equal(0x29B1, Checksum.Crc16Ccitt(Digits));
        Assert.Equal(0x06, Checksum.Sum8([0x01, 0x02, 0x03]));
        Assert.Equal(0xDD, Checksum.Sum8([0xFF, 0xDE]));
        Assert.Equal(0x00, Checksum.Xor8([0x01, 0x02, 0x03]));
    }

    [Fact]
    public void Crc16Modbus_ByteOrder_MatchesModbusFrame()
    {
        // Modbus RTU: 01 03 00 00 00 0A → CRC C5 CD (하위 바이트 먼저)
        byte[] request = [0x01, 0x03, 0x00, 0x00, 0x00, 0x0A];
        Assert.Equal(new byte[] { 0xC5, 0xCD }, Checksum.Compute(CheckKind.Crc16Modbus, request, CheckByteOrder.LowFirst));
        Assert.Equal(new byte[] { 0xCD, 0xC5 }, Checksum.Compute(CheckKind.Crc16Modbus, request, CheckByteOrder.HighFirst));
    }

    [Fact]
    public void Verify_DetectsMatchMismatchAndTrailingCrLf()
    {
        byte[] ok = [0x01, 0x03, 0x00, 0x00, 0x00, 0x0A, 0xC5, 0xCD];
        Assert.True(Checksum.Verify(CheckKind.Crc16Modbus, ok, CheckByteOrder.LowFirst));
        Assert.True(Checksum.Verify(CheckKind.Crc16Modbus, [.. ok, 0x0D, 0x0A], CheckByteOrder.LowFirst));
        Assert.False(Checksum.Verify(CheckKind.Crc16Modbus, [.. ok[..^1], 0x00], CheckByteOrder.LowFirst));
        Assert.Null(Checksum.Verify(CheckKind.Crc16Modbus, [0x01, 0x02], CheckByteOrder.LowFirst));
        Assert.Null(Checksum.Verify(CheckKind.None, ok, CheckByteOrder.LowFirst));

        // 검사값 자체가 0x0D 인 경우: 끝 CR 을 지우지 않은 위치로도 확인
        byte[] body = [0x0C];
        Assert.Equal(0x0D, Checksum.Sum8([0x01, 0x0C]));
        Assert.True(Checksum.Verify(CheckKind.Sum8, [0x01, .. body, 0x0D], CheckByteOrder.LowFirst));
    }

    [Fact]
    public void SendBuilder_AppendsCheckThenCrLf()
    {
        var bytes = SendBuilder.Build("01 03 00 00 00 0A", InputMode.Hex, TextEncodingKind.Ascii, escapes: true,
            CheckKind.Crc16Modbus, CheckByteOrder.LowFirst, appendCr: true, appendLf: true);
        Assert.Equal(new byte[] { 0x01, 0x03, 0x00, 0x00, 0x00, 0x0A, 0xC5, 0xCD, 0x0D, 0x0A }, bytes);

        Assert.Equal(new byte[] { 0x41, 0x42, 0x0D },
            SendBuilder.Build("AB", InputMode.Text, TextEncodingKind.Ascii, true, CheckKind.None, CheckByteOrder.HighFirst, true, false));
    }

    [Fact]
    public void LogEntry_MatchesTextHexAndPeer()
    {
        var entry = new CommLogEntry(1, DateTime.Now, null, CommDirection.Received, "COM3", [0x02, 0x4F, 0x4B, 0x03]);
        Assert.True(entry.Matches("ok", TextEncodingKind.Ascii));
        Assert.True(entry.Matches("4f 4b", TextEncodingKind.Ascii));
        Assert.True(entry.Matches("4F4B03", TextEncodingKind.Ascii));
        Assert.True(entry.Matches("com3", TextEncodingKind.Ascii));
        Assert.True(entry.Matches("<STX>", TextEncodingKind.Ascii));
        Assert.False(entry.Matches("NG", TextEncodingKind.Ascii));
        Assert.Contains("<STX>OK<ETX>  |  02 4F 4B 03", entry.ToLogLine(TextEncodingKind.Ascii));
    }

    [Theory]
    [InlineData(TextEncodingKind.Ascii)]
    [InlineData(TextEncodingKind.Utf8)]
    [InlineData(TextEncodingKind.EucKr)]
    public void DisplayMap_TextMatchesDisplayText(TextEncodingKind encoding)
    {
        byte[] data = [0x02, 0x41, 0xC7, 0xD1, 0xEA, 0xB0, 0x80, 0x42, 0x0D, 0x0A, 0xFF];
        var map = DisplayMap.Build(data, encoding);
        Assert.Equal(ByteCodec.ToDisplayText(data, encoding), map.Text);
        Assert.Equal(map.Text.Length, map.ByteIndex.Length);
        Assert.All(map.ByteIndex, i => Assert.InRange(i, 0, data.Length - 1));
    }

    [Fact]
    public void DisplayMap_MapsMultiByteCharactersToFirstByte()
    {
        var map = DisplayMap.Build([0x41, 0xC7, 0xD1, 0x42], TextEncodingKind.EucKr); // A한B
        Assert.Equal("A한B", map.Text);
        Assert.Equal(new[] { 0, 1, 3 }, map.ByteIndex);
    }

    [Fact]
    public void FindBytes_TextMatchMarksSourceBytes()
    {
        byte[] data = [0x02, 0x54, 0x45, 0x4D, 0x50, 0x3D, 0x32, 0x35, 0x03]; // <STX>TEMP=25<ETX>
        var mask = SearchMatcher.FindBytes(data, "temp", TextEncodingKind.Ascii)!;
        Assert.Equal(new[] { false, true, true, true, true, false, false, false, false }, mask);

        // 제어 문자 이름
        mask = SearchMatcher.FindBytes(data, "ETX", TextEncodingKind.Ascii)!;
        Assert.True(mask[8]);
        Assert.Equal(1, mask.Count(m => m));

        Assert.Null(SearchMatcher.FindBytes(data, "NG", TextEncodingKind.Ascii));
        Assert.Null(SearchMatcher.FindBytes(data, " ", TextEncodingKind.Ascii));
    }

    [Fact]
    public void FindBytes_HexMatchIsByteAlignedAndFindsAll()
    {
        byte[] data = [0x03, 0x0D, 0x30, 0xD0, 0x03, 0x0D];
        var mask = SearchMatcher.FindBytes(data, "03 0d", TextEncodingKind.Ascii)!;
        Assert.Equal(new[] { true, true, false, false, true, true }, mask);

        // "30D0" 은 바이트 경계에서만 (0x30, 0xD0)
        mask = SearchMatcher.FindBytes(data, "30D0", TextEncodingKind.Ascii)!;
        Assert.Equal(new[] { false, false, true, true, false, false }, mask);

        // 홀수 자리는 HEX 로 보지 않음
        Assert.False(SearchMatcher.TryParseHexQuery("30D", out _));
    }

    [Fact]
    public void FindText_FindsAllCaseInsensitive()
    {
        Assert.Equal([(0, 3), (12, 3)], SearchMatcher.FindText("127.0.0.1:50127", "127"));
        Assert.Empty(SearchMatcher.FindText("abc", "x"));
        Assert.Empty(SearchMatcher.FindText(null, "x"));
    }

    [Fact]
    public async Task ReceiveAssembler_MergesChunksWithinGap()
    {
        var lines = new List<byte[]>();
        using var assembler = new ReceiveAssembler((_, data, _) => { lock (lines) { lines.Add(data); } }) { GapMs = 80 };
        assembler.Add("COM1", [0x01, 0x02], DateTime.Now);
        assembler.Add("COM1", [0x03], DateTime.Now);
        assembler.Add("COM2", [0x09], DateTime.Now);
        await WaitUntil(() => { lock (lines) { return lines.Count == 2; } });
        lock (lines)
        {
            Assert.Contains(lines, l => l.SequenceEqual(new byte[] { 0x01, 0x02, 0x03 }));
            Assert.Contains(lines, l => l.SequenceEqual(new byte[] { 0x09 }));
        }
    }

    [Fact]
    public void Framer_EndMarker_SplitsAndKeepsRemainder()
    {
        var buffer = new List<byte> { 0x41, 0x0D, 0x0A, 0x42, 0x43, 0x0D, 0x0A, 0x44 };
        var frames = Framer.Split(buffer, new FrameSettings(FrameMode.EndMarker, 20, [], [0x0D, 0x0A], 0));
        Assert.Equal(2, frames.Count);
        Assert.Equal(new byte[] { 0x41, 0x0D, 0x0A }, frames[0]);
        Assert.Equal(new byte[] { 0x42, 0x43, 0x0D, 0x0A }, frames[1]);
        Assert.Equal(new byte[] { 0x44 }, buffer);
    }

    [Fact]
    public void Framer_StartEnd_EmitsLeadingGarbageSeparately()
    {
        var settings = new FrameSettings(FrameMode.StartEnd, 20, [0x02], [0x03], 0);
        var buffer = new List<byte> { 0xFF, 0x02, 0x41, 0x03, 0x02, 0x42 };
        var frames = Framer.Split(buffer, settings);
        Assert.Equal(new byte[] { 0xFF }, frames[0]);
        Assert.Equal(new byte[] { 0x02, 0x41, 0x03 }, frames[1]);
        Assert.Equal(new byte[] { 0x02, 0x42 }, buffer); // 끝이 아직 안 옴

        buffer.Add(0x03);
        Assert.Equal(new byte[] { 0x02, 0x42, 0x03 }, Assert.Single(Framer.Split(buffer, settings)));
        Assert.Empty(buffer);

        // 시작이 없으면 그대로 둠 (시간이 지나면 ReceiveAssembler 가 내보냄)
        buffer.AddRange([0x10, 0x11]);
        Assert.Empty(Framer.Split(buffer, settings));
        Assert.Equal(2, buffer.Count);
    }

    [Fact]
    public void Framer_FixedLength_SplitsEveryN()
    {
        var buffer = new List<byte> { 1, 2, 3, 4, 5, 6, 7 };
        var frames = Framer.Split(buffer, new FrameSettings(FrameMode.FixedLength, 20, [], [], 3));
        Assert.Equal(2, frames.Count);
        Assert.Equal(new byte[] { 7 }, buffer);
    }

    [Fact]
    public void FrameSettings_FallsBackToGapWhenIncomplete()
    {
        Assert.Equal(FrameMode.Gap, new FrameSettings(FrameMode.EndMarker, 20, [], [], 0).Effective.Mode);
        Assert.Equal(FrameMode.Gap, new FrameSettings(FrameMode.StartEnd, 20, [0x02], [], 0).Effective.Mode);
        Assert.Equal(FrameMode.Gap, new FrameSettings(FrameMode.FixedLength, 20, [], [], 0).Effective.Mode);
        Assert.Equal(FrameMode.EndMarker, new FrameSettings(FrameMode.EndMarker, 20, [], [0x0D], 0).Effective.Mode);
    }

    [Fact]
    public async Task ReceiveAssembler_EndMarker_EmitsFramesAcrossChunksAndFlushesPartial()
    {
        var lines = new List<byte[]>();
        using var assembler = new ReceiveAssembler((_, data, _) => { lock (lines) { lines.Add(data); } })
        {
            Settings = new FrameSettings(FrameMode.EndMarker, 20, [], [0x0A], 0),
        };
        assembler.Add("COM1", [0x41, 0x42], DateTime.Now);
        assembler.Add("COM1", [0x0A, 0x43, 0x0A, 0x44], DateTime.Now);
        lock (lines)
        {
            Assert.Equal(2, lines.Count); // 완성된 두 프레임은 바로
            Assert.Equal(new byte[] { 0x41, 0x42, 0x0A }, lines[0]);
            Assert.Equal(new byte[] { 0x43, 0x0A }, lines[1]);
        }

        await WaitUntil(() => { lock (lines) { return lines.Count == 3; } }); // 끝이 안 온 0x44 는 시간 지나 내보냄
        lock (lines)
        {
            Assert.Equal(new byte[] { 0x44 }, lines[2]);
        }
    }

    internal static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < deadline, "시간 안에 조건이 충족되지 않았습니다.");
            await Task.Delay(20);
        }
    }
}

/// <summary>Steal(중계) 테스트: COM 포트 대신 가짜 연결 두 개를 씁니다.</summary>
public class CommRelayTests
{
    private sealed class FakeChannel(string name, bool failOpen = false) : CommChannelBase
    {
        public List<byte[]> Sent { get; } = [];

        public bool Disposed { get; private set; }

        public override string Title => name;

        public override bool IsOpen => !Disposed;

        public override Task OpenAsync(CancellationToken cancellationToken) =>
            failOpen ? throw new UnauthorizedAccessException("busy") : Task.CompletedTask;

        public override Task<string> SendAsync(byte[] data, CancellationToken cancellationToken)
        {
            lock (Sent)
            {
                Sent.Add(data);
            }

            return Task.FromResult(name);
        }

        public void Inject(byte[] data) => RaiseReceived(name, data);

        public void Unplug() => RaiseClosed("unplugged");

        protected override void CloseCore() => Disposed = true;
    }

    [Fact]
    public async Task Relay_ForwardsBothWaysAndReportsDirection()
    {
        var a = new FakeChannel("COM3");
        var b = new FakeChannel("COM4");
        using var relay = new RelayChannel(a, "COM3", b, "COM4", listenOnly: false);
        var seen = new List<(int Side, byte[] Data)>();
        relay.Relayed += (side, data) => { lock (seen) { seen.Add((side, data)); } };
        await relay.OpenAsync(CancellationToken.None);

        a.Inject([0x01]);
        a.Inject([0x02]);
        b.Inject([0x09]);
        await CommCodecTests.WaitUntil(() => { lock (b.Sent) { lock (a.Sent) { return b.Sent.Count == 2 && a.Sent.Count == 1; } } });

        Assert.Equal(new byte[] { 0x01 }, b.Sent[0]); // 순서 유지
        Assert.Equal(new byte[] { 0x02 }, b.Sent[1]);
        Assert.Equal(new byte[] { 0x09 }, a.Sent[0]);
        lock (seen)
        {
            Assert.Equal([1, 1, 2], seen.Select(s => s.Side));
        }
    }

    [Fact]
    public async Task Relay_ListenOnly_DoesNotForward()
    {
        var a = new FakeChannel("COM3");
        var b = new FakeChannel("COM4");
        using var relay = new RelayChannel(a, "COM3", b, "COM4", listenOnly: true);
        var count = 0;
        relay.Relayed += (_, _) => Interlocked.Increment(ref count);
        await relay.OpenAsync(CancellationToken.None);
        a.Inject([0x01]);
        b.Inject([0x02]);
        await Task.Delay(100);
        Assert.Equal(2, count);
        Assert.Empty(a.Sent);
        Assert.Empty(b.Sent);
    }

    [Fact]
    public async Task Relay_InjectSendsToChosenSide()
    {
        var a = new FakeChannel("COM3");
        var b = new FakeChannel("COM4");
        using var relay = new RelayChannel(a, "COM3", b, "COM4", listenOnly: false);
        await relay.OpenAsync(CancellationToken.None);
        Assert.Equal("COM4", await relay.SendToAsync(2, [0x55], CancellationToken.None));
        Assert.Single(b.Sent);
        Assert.Empty(a.Sent);
    }

    [Fact]
    public async Task Relay_ClosesFirstSideWhenSecondFailsToOpen()
    {
        var a = new FakeChannel("COM3");
        var b = new FakeChannel("COM4", failOpen: true);
        var relay = new RelayChannel(a, "COM3", b, "COM4", listenOnly: false);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => relay.OpenAsync(CancellationToken.None));
        Assert.True(a.Disposed);
    }

    [Fact]
    public async Task Relay_ReportsClosedWhenOneSideUnplugs()
    {
        var a = new FakeChannel("COM3");
        var b = new FakeChannel("COM4");
        var relay = new RelayChannel(a, "COM3", b, "COM4", listenOnly: false);
        string? reason = null;
        relay.Closed += r => reason = r;
        await relay.OpenAsync(CancellationToken.None);
        b.Unplug();
        Assert.Equal("COM4: unplugged", reason);
        Assert.True(a.Disposed); // 반대쪽도 닫힘
    }
}

/// <summary>내 PC 안(127.0.0.1)에서만 주고받는 TCP/UDP 왕복 테스트. COM 포트는 사용하지 않습니다.</summary>
public class CommLoopbackTests
{
    [Fact]
    public async Task TcpServerAndClient_ExchangeBothWays()
    {
        var port = FreeTcpPort();
        using var server = new TcpServerChannel(IPAddress.Loopback, port);
        using var client = new TcpClientChannel("127.0.0.1", port, TimeSpan.FromSeconds(3));
        var serverGot = new List<byte>();
        var clientGot = new List<byte>();
        var connected = false;
        server.Received += (_, d) => { lock (serverGot) { serverGot.AddRange(d); } };
        server.Notice += (_, _) => connected = true;
        client.Received += (_, d) => { lock (clientGot) { clientGot.AddRange(d); } };

        await server.OpenAsync(CancellationToken.None);
        await client.OpenAsync(CancellationToken.None);
        await CommCodecTests.WaitUntil(() => connected);

        await client.SendAsync([0x01, 0x02], CancellationToken.None);
        await server.SendAsync([0x0A], CancellationToken.None);
        await CommCodecTests.WaitUntil(() => { lock (serverGot) { lock (clientGot) { return serverGot.Count == 2 && clientGot.Count == 1; } } });
        Assert.Equal(new byte[] { 0x01, 0x02 }, serverGot);
        Assert.Equal(new byte[] { 0x0A }, clientGot);
    }

    [Fact]
    public async Task TcpClient_ReportsClosedWhenServerStops()
    {
        var port = FreeTcpPort();
        var server = new TcpServerChannel(IPAddress.Loopback, port);
        using var client = new TcpClientChannel("127.0.0.1", port, TimeSpan.FromSeconds(3));
        string? reason = null;
        var connected = false;
        server.Notice += (_, _) => connected = true;
        client.Closed += r => reason = r;
        await server.OpenAsync(CancellationToken.None);
        await client.OpenAsync(CancellationToken.None);
        await CommCodecTests.WaitUntil(() => connected);
        server.Dispose();
        await CommCodecTests.WaitUntil(() => reason is not null);
    }

    [Fact]
    public async Task TcpServer_SendWithoutClients_Throws()
    {
        using var server = new TcpServerChannel(IPAddress.Loopback, FreeTcpPort());
        await server.OpenAsync(CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => server.SendAsync([0x01], CancellationToken.None));
    }

    [Fact]
    public async Task Udp_SendsToTargetAndRepliesToLastSender()
    {
        using var a = new UdpChannel(0, null, 0);
        await a.OpenAsync(CancellationToken.None);
        using var b = new UdpChannel(0, "127.0.0.1", a.LocalPort);
        await b.OpenAsync(CancellationToken.None);

        byte[]? aGot = null;
        byte[]? bGot = null;
        a.Received += (_, d) => aGot = d;
        b.Received += (_, d) => bGot = d;

        // a 는 대상이 없으므로 받기 전에는 보낼 수 없음
        await Assert.ThrowsAsync<InvalidOperationException>(() => a.SendAsync([0x00], CancellationToken.None));

        await b.SendAsync([0x31, 0x32], CancellationToken.None);
        await CommCodecTests.WaitUntil(() => aGot is not null);
        await a.SendAsync([0x33], CancellationToken.None); // 마지막으로 보낸 상대(b)에게 답장
        await CommCodecTests.WaitUntil(() => bGot is not null);
        Assert.Equal(new byte[] { 0x31, 0x32 }, aGot);
        Assert.Equal(new byte[] { 0x33 }, bGot);
    }

    private static int FreeTcpPort()
    {
        var listener = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}

public class CommConsoleCommandTests
{
    private sealed class FakeContext : SunTool.Core.Diagnostics.IConsoleContext
    {
        public List<string> Lines { get; } = [];

        public IReadOnlyCollection<SunTool.Core.Diagnostics.IConsoleCommand> Commands => [];

        public void Write(string text, SunTool.Core.Logging.LogLevel level = SunTool.Core.Logging.LogLevel.Info) => Lines.Add(text);

        public void ClearOutput() => Lines.Clear();
    }

    private static List<string> Run(params string[] args)
    {
        var command = new CommConsoleCommand(null!, null!);
        var context = new FakeContext();
        command.Execute(args, context);
        return context.Lines;
    }

    [Fact]
    public void Check_ComputesModbusCrc()
    {
        // 01 03 00 00 00 0A → Modbus CRC CDC5, 실제 프레임은 ... C5 CD
        var lines = Run("check", "01", "03", "00", "00", "00", "0A");
        Assert.Contains(lines, l => l.Contains("CRC-16 Modbus CDC5") && l.Contains("C5 CD"));
    }

    [Fact]
    public void Hex_UsesEscapes() =>
        Assert.Contains(Run("hex", "ID?\r\n"), l => l.Contains("49 44 3F 0D 0A"));

    [Fact]
    public void Text_ShowsControlNames() =>
        Assert.Contains(Run("text", "02 41 42 03"), l => l.Contains("AB"));

    [Fact]
    public void BadHex_WarnsInsteadOfThrowing() =>
        Assert.Contains(Run("check", "zz"), l => l.Contains("HEX"));
}
