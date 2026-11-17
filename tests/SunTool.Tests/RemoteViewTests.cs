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

    // ===== 제어·입력 (버전 2) =====

    [Fact]
    public void Version_IsTwo()
    {
        Assert.Equal(2, RemoteProtocol.Version);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ControlGrant_RoundTrips(bool granted)
    {
        var payload = RemoteProtocol.EncodeControlGrant(granted);
        Assert.Equal(RemoteProtocol.MsgType.ControlGrant, RemoteProtocol.TypeOf(payload));
        Assert.True(RemoteProtocol.TryDecodeControlGrant(payload, out var back));
        Assert.Equal(granted, back);
    }

    [Fact]
    public void InputMouse_RoundTrips()
    {
        var payload = RemoteProtocol.EncodeInputMouse(RemoteProtocol.MouseAction.Wheel, 0.25f, 0.75f, -120);
        Assert.Equal(RemoteProtocol.MsgType.InputMouse, RemoteProtocol.TypeOf(payload));
        Assert.True(RemoteProtocol.TryDecodeInputMouse(payload, out var action, out var nx, out var ny, out var data));
        Assert.Equal(RemoteProtocol.MouseAction.Wheel, action);
        Assert.Equal(0.25f, nx);
        Assert.Equal(0.75f, ny);
        Assert.Equal(-120, data);
    }

    [Fact]
    public void InputKey_RoundTrips()
    {
        var payload = RemoteProtocol.EncodeInputKey(0x41, down: true, modifiers: 0x0004);
        Assert.Equal(RemoteProtocol.MsgType.InputKey, RemoteProtocol.TypeOf(payload));
        Assert.True(RemoteProtocol.TryDecodeInputKey(payload, out var vk, out var down, out var mods));
        Assert.Equal(0x41, vk);
        Assert.True(down);
        Assert.Equal(0x0004, mods);
    }

    [Fact]
    public void InputDecode_RejectsWrongType()
    {
        Assert.False(RemoteProtocol.TryDecodeInputMouse(RemoteProtocol.EncodeHelloError(), out _, out _, out _, out _));
        Assert.False(RemoteProtocol.TryDecodeInputKey(RemoteProtocol.EncodeFrame([1]), out _, out _, out _));
        Assert.False(RemoteProtocol.TryDecodeControlGrant(RemoteProtocol.EncodeFrame([1]), out _));
    }

    // ===== 좌표 매핑 (뷰어 입력) =====

    [Fact]
    public void DisplayToUnit_Center_NoLetterbox()
    {
        // 이미지 비율과 표시 영역 비율이 같으면 여백이 없습니다.
        var (u, v, inside) = InputMapping.DisplayToUnit(100, 50, 200, 100, 1920, 960);
        Assert.True(inside);
        Assert.Equal(0.5, u, 3);
        Assert.Equal(0.5, v, 3);
    }

    [Fact]
    public void DisplayToUnit_Letterbox_MapsContentAndRejectsMargin()
    {
        // 16:9 이미지를 정사각 1000x1000 에 Uniform → 위아래 여백(각 218.75px).
        var (_, vTop, insideTop) = InputMapping.DisplayToUnit(500, 100, 1000, 1000, 1920, 1080);
        Assert.False(insideTop); // 위쪽 여백은 콘텐츠 밖

        var (u, v, inside) = InputMapping.DisplayToUnit(500, 500, 1000, 1000, 1920, 1080);
        Assert.True(inside);
        Assert.Equal(0.5, u, 3);
        Assert.Equal(0.5, v, 3);
    }

    [Fact]
    public void ToFull_WholeImage_Identity()
    {
        var (nx, ny) = InputMapping.ToFull(0.5, 0.25, 0, 0, 0, 0, 1920, 1080);
        Assert.Equal(0.5f, nx, 3);
        Assert.Equal(0.25f, ny, 3);
    }

    [Fact]
    public void ToFull_SecondMonitorCrop_OffsetsIntoFullImage()
    {
        // 전체 3200x1080, 두 번째 모니터 크롭 (1920,0,1280,1080). 크롭 중앙(0.5,0.5) → 전체에서 오른쪽 모니터 중앙.
        var (nx, ny) = InputMapping.ToFull(0.5, 0.5, 1920, 0, 1280, 1080, 3200, 1080);
        Assert.Equal((1920 + 0.5 * 1280) / 3200f, nx, 4); // = 0.8
        Assert.Equal(0.5f, ny, 4);
    }

    // ===== 제어 허용 게이팅 =====

    [Theory]
    [InlineData(1, -1, true)]   // 모니터 1대면 '전체'여도 제어 가능
    [InlineData(1, 0, true)]
    [InlineData(2, -1, false)]  // 2대 + '전체'면 제어 불가
    [InlineData(2, 0, true)]    // 2대 + 개별 선택이면 가능
    [InlineData(2, 1, true)]
    [InlineData(3, -1, false)]
    [InlineData(0, -1, true)]   // 연결 전/모니터 미상은 막지 않음
    public void ControlAllowed_BlocksAllModeOnlyOnMultiMonitor(int realMonitorCount, int selectedIndex, bool expected)
        => Assert.Equal(expected, InputMapping.ControlAllowed(realMonitorCount, selectedIndex));

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
