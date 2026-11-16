using System.Buffers.Binary;

namespace SunTool.Modules.RemoteView.Logic;

/// <summary>
/// 원격 화면 보기용 아주 단순한 TCP 프로토콜.
/// 모든 메시지는 [4바이트 길이(빅엔디언)][payload] 이고, payload 첫 바이트가 종류입니다.
/// 기본은 보기 전용이며, 호스트가 제어를 허용했을 때만 입력(마우스·키보드)을 뷰어→호스트로 보냅니다.
/// </summary>
public static class RemoteProtocol
{
    /// <summary>버전 2 부터 원격 제어(입력 전송)를 지원합니다.</summary>
    public const int Version = 2;

    /// <summary>한 메시지 최대 크기 (장난 데이터로 메모리를 터뜨리지 않도록). 64 MB.</summary>
    public const int MaxMessageSize = 64 * 1024 * 1024;

    public enum MsgType : byte
    {
        Hello = 1,         // 뷰어 → 호스트: 버전 + PIN
        HelloOk = 2,       // 호스트 → 뷰어: 화면 크기
        HelloError = 3,    // 호스트 → 뷰어: 거부 (PIN 틀림·버전 불일치)
        Frame = 4,         // 호스트 → 뷰어: JPEG 한 장
        ControlGrant = 5,  // 호스트 → 뷰어: 제어 허용/해제 알림
        InputMouse = 6,    // 뷰어 → 호스트: 마우스 입력 (정규화 좌표 0~1)
        InputKey = 7,      // 뷰어 → 호스트: 키보드 입력 (가상 키 코드)
    }

    /// <summary>마우스 입력 종류.</summary>
    public enum MouseAction : byte
    {
        Move = 0,
        LeftDown = 1,
        LeftUp = 2,
        RightDown = 3,
        RightUp = 4,
        MiddleDown = 5,
        MiddleUp = 6,
        Wheel = 7,
    }

    // ===================================================================== 프레이밍

    public static void WriteMessage(Stream stream, byte[] payload)
    {
        var header = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(header, payload.Length);
        stream.Write(header, 0, 4);
        stream.Write(payload, 0, payload.Length);
        stream.Flush();
    }

    /// <summary>메시지 하나를 읽습니다. 스트림이 끝나면 null. 길이가 한도를 넘으면 예외.</summary>
    public static byte[]? ReadMessage(Stream stream)
    {
        var header = ReadExactly(stream, 4);
        if (header is null)
        {
            return null;
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(header);
        if (length < 0 || length > MaxMessageSize)
        {
            throw new InvalidDataException($"메시지 크기가 비정상입니다: {length}");
        }

        return ReadExactly(stream, length) ?? throw new EndOfStreamException("메시지가 중간에 끊겼습니다.");
    }

    private static byte[]? ReadExactly(Stream stream, int count)
    {
        var buffer = new byte[count];
        var read = 0;
        while (read < count)
        {
            var n = stream.Read(buffer, read, count - read);
            if (n == 0)
            {
                return read == 0 ? null : throw new EndOfStreamException("스트림이 중간에 끊겼습니다.");
            }

            read += n;
        }

        return buffer;
    }

    // ===================================================================== 메시지 만들기·읽기

    public static byte[] EncodeHello(string pin)
    {
        var pinBytes = System.Text.Encoding.UTF8.GetBytes(pin ?? string.Empty);
        var payload = new byte[1 + 4 + 2 + pinBytes.Length];
        payload[0] = (byte)MsgType.Hello;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(1, 4), Version);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(5, 2), (ushort)pinBytes.Length);
        pinBytes.CopyTo(payload, 7);
        return payload;
    }

    public static bool TryDecodeHello(byte[] payload, out int version, out string pin)
    {
        version = 0;
        pin = string.Empty;
        if (payload.Length < 7 || payload[0] != (byte)MsgType.Hello)
        {
            return false;
        }

        version = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(1, 4));
        var pinLen = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(5, 2));
        if (7 + pinLen > payload.Length)
        {
            return false;
        }

        pin = System.Text.Encoding.UTF8.GetString(payload, 7, pinLen);
        return true;
    }

    public static byte[] EncodeHelloOk(int width, int height, IReadOnlyList<(int X, int Y, int W, int H)> monitors)
    {
        var payload = new byte[1 + 4 + 4 + 4 + monitors.Count * 16];
        payload[0] = (byte)MsgType.HelloOk;
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(1, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(5, 4), height);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(9, 4), monitors.Count);
        var off = 13;
        foreach (var (x, y, w, h) in monitors)
        {
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(off, 4), x);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(off + 4, 4), y);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(off + 8, 4), w);
            BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(off + 12, 4), h);
            off += 16;
        }

        return payload;
    }

    public static byte[] EncodeHelloError() => [(byte)MsgType.HelloError];

    public static bool TryDecodeHelloOk(byte[] payload, out int width, out int height, out IReadOnlyList<(int X, int Y, int W, int H)> monitors)
    {
        width = 0;
        height = 0;
        monitors = [];
        if (payload.Length < 13 || payload[0] != (byte)MsgType.HelloOk)
        {
            return false;
        }

        width = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(1, 4));
        height = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(5, 4));
        var count = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(9, 4));
        if (count < 0 || 13 + count * 16 > payload.Length)
        {
            return false;
        }

        var list = new List<(int, int, int, int)>(count);
        var off = 13;
        for (var i = 0; i < count; i++)
        {
            list.Add((
                BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(off, 4)),
                BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(off + 4, 4)),
                BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(off + 8, 4)),
                BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(off + 12, 4))));
            off += 16;
        }

        monitors = list;
        return true;
    }

    public static byte[] EncodeFrame(byte[] jpeg)
    {
        var payload = new byte[1 + jpeg.Length];
        payload[0] = (byte)MsgType.Frame;
        jpeg.CopyTo(payload, 1);
        return payload;
    }

    public static bool TryDecodeFrame(byte[] payload, out byte[] jpeg)
    {
        jpeg = [];
        if (payload.Length < 1 || payload[0] != (byte)MsgType.Frame)
        {
            return false;
        }

        jpeg = payload[1..];
        return true;
    }

    // ===================================================================== 제어·입력 (버전 2)

    /// <summary>호스트 → 뷰어: 제어 허용 여부 알림.</summary>
    public static byte[] EncodeControlGrant(bool granted) => [(byte)MsgType.ControlGrant, (byte)(granted ? 1 : 0)];

    public static bool TryDecodeControlGrant(byte[] payload, out bool granted)
    {
        granted = false;
        if (payload.Length < 2 || payload[0] != (byte)MsgType.ControlGrant)
        {
            return false;
        }

        granted = payload[1] != 0;
        return true;
    }

    /// <summary>뷰어 → 호스트: 마우스 입력. <paramref name="nx"/>·<paramref name="ny"/> 는 전송 이미지 기준 0~1 좌표.</summary>
    public static byte[] EncodeInputMouse(MouseAction action, float nx, float ny, int data)
    {
        var payload = new byte[1 + 1 + 4 + 4 + 4];
        payload[0] = (byte)MsgType.InputMouse;
        payload[1] = (byte)action;
        BinaryPrimitives.WriteSingleBigEndian(payload.AsSpan(2, 4), nx);
        BinaryPrimitives.WriteSingleBigEndian(payload.AsSpan(6, 4), ny);
        BinaryPrimitives.WriteInt32BigEndian(payload.AsSpan(10, 4), data);
        return payload;
    }

    public static bool TryDecodeInputMouse(byte[] payload, out MouseAction action, out float nx, out float ny, out int data)
    {
        action = MouseAction.Move;
        nx = 0;
        ny = 0;
        data = 0;
        if (payload.Length < 14 || payload[0] != (byte)MsgType.InputMouse)
        {
            return false;
        }

        action = (MouseAction)payload[1];
        nx = BinaryPrimitives.ReadSingleBigEndian(payload.AsSpan(2, 4));
        ny = BinaryPrimitives.ReadSingleBigEndian(payload.AsSpan(6, 4));
        data = BinaryPrimitives.ReadInt32BigEndian(payload.AsSpan(10, 4));
        return true;
    }

    /// <summary>뷰어 → 호스트: 키보드 입력. <paramref name="vk"/> 는 Win32 가상 키 코드.</summary>
    public static byte[] EncodeInputKey(ushort vk, bool down, ushort modifiers)
    {
        var payload = new byte[1 + 2 + 1 + 2];
        payload[0] = (byte)MsgType.InputKey;
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(1, 2), vk);
        payload[3] = (byte)(down ? 1 : 0);
        BinaryPrimitives.WriteUInt16BigEndian(payload.AsSpan(4, 2), modifiers);
        return payload;
    }

    public static bool TryDecodeInputKey(byte[] payload, out ushort vk, out bool down, out ushort modifiers)
    {
        vk = 0;
        down = false;
        modifiers = 0;
        if (payload.Length < 6 || payload[0] != (byte)MsgType.InputKey)
        {
            return false;
        }

        vk = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(1, 2));
        down = payload[3] != 0;
        modifiers = BinaryPrimitives.ReadUInt16BigEndian(payload.AsSpan(4, 2));
        return true;
    }

    public static MsgType TypeOf(byte[] payload) => payload.Length == 0 ? 0 : (MsgType)payload[0];
}
