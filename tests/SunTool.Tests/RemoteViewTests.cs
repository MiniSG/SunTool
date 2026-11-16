using System.Buffers.Binary;
using SunTool.Modules.RemoteView.Logic;

namespace SunTool.Tests;

/// <summary>원격 화면 보기 프로토콜·PIN 의 순수 로직 검사 (소켓 없이).</summary>
public class RemoteViewTests
{
    // ===== 프레이밍 =====

    [Fact]
    public void WriteRead_RoundTrips()
    {
        using var ms = new MemoryStream();
        var payload = new byte[] { 1, 2, 3, 4, 5 };
        RemoteProtocol.WriteMessage(ms, payload);
        ms.Position = 0;
        Assert.Equal(payload, RemoteProtocol.ReadMessage(ms));
    }

    [Fact]
    public void ReadMessage_ReturnsNull_OnEmptyStream()
    {
        using var ms = new MemoryStream();
        Assert.Null(RemoteProtocol.ReadMessage(ms));
    }

    [Fact]
    public void ReadMessage_Throws_OnOversizeLength()
    {
        using var ms = new MemoryStream();
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, RemoteProtocol.MaxMessageSize + 1);
        ms.Write(header);
        ms.Position = 0;
        Assert.Throws<InvalidDataException>(() => RemoteProtocol.ReadMessage(ms));
    }

    // ===== 메시지 =====

    [Fact]
    public void Hello_RoundTrips()
    {
        var payload = RemoteProtocol.EncodeHello("123456");
        Assert.True(RemoteProtocol.TryDecodeHello(payload, out var version, out var pin));
        Assert.Equal(RemoteProtocol.Version, version);
        Assert.Equal("123456", pin);
    }

    [Fact]
    public void HelloOk_RoundTrips_WithMonitors()
    {
        var monitors = new List<(int, int, int, int)> { (0, 0, 1280, 720), (1280, 0, 640, 720) };
        var payload = RemoteProtocol.EncodeHelloOk(1920, 720, monitors);
        Assert.Equal(RemoteProtocol.MsgType.HelloOk, RemoteProtocol.TypeOf(payload));
        Assert.True(RemoteProtocol.TryDecodeHelloOk(payload, out var w, out var h, out var mons));
        Assert.Equal(1920, w);
        Assert.Equal(720, h);
        Assert.Equal(monitors, mons);
    }

    [Fact]
    public void HelloError_HasErrorType()
    {
        Assert.Equal(RemoteProtocol.MsgType.HelloError, RemoteProtocol.TypeOf(RemoteProtocol.EncodeHelloError()));
    }

    [Fact]
    public void Frame_RoundTrips()
    {
        var jpeg = new byte[] { 0xFF, 0xD8, 0xAB, 0xCD, 0xFF, 0xD9 };
        var payload = RemoteProtocol.EncodeFrame(jpeg);
        Assert.Equal(RemoteProtocol.MsgType.Frame, RemoteProtocol.TypeOf(payload));
        Assert.True(RemoteProtocol.TryDecodeFrame(payload, out var back));
        Assert.Equal(jpeg, back);
    }

    [Fact]
    public void Decode_RejectsWrongType()
    {
        Assert.False(RemoteProtocol.TryDecodeHello(RemoteProtocol.EncodeFrame([1]), out _, out _));
        Assert.False(RemoteProtocol.TryDecodeFrame(RemoteProtocol.EncodeHelloError(), out _));
    }

    // ===== PIN =====

    [Fact]
    public void Pin_IsSixDigits()
    {
        for (var i = 0; i < 50; i++)
        {
            var pin = Pin.Generate();
            Assert.Matches("^[0-9]{6}$", pin);
        }
    }

    [Fact]
    public void Pin_Matches_OnlyWhenEqual()
    {
        Assert.True(Pin.Matches("123456", "123456"));
        Assert.False(Pin.Matches("123456", "123457"));
        Assert.False(Pin.Matches("123456", "12345"));
        Assert.False(Pin.Matches("123456", null));
    }
}
