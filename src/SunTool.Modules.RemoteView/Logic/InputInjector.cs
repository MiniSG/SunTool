using System.Runtime.InteropServices;

namespace SunTool.Modules.RemoteView.Logic;

/// <summary>
/// 호스트에서 받은 원격 입력을 OS 에 주입합니다 (SendInput).
/// 마우스 좌표는 전송 이미지 기준 0~1 정규화 값으로 오고, 전송 이미지는 가상 화면 전체의
/// 균일 축소본이므로 그대로 가상 데스크톱 절대좌표(0~65535)로 바꿔 넣습니다.
/// 호스트가 제어를 허용했을 때만 호출됩니다.
/// </summary>
public static class InputInjector
{
    public static void Mouse(RemoteProtocol.MouseAction action, float nx, float ny, int data)
    {
        var x = Normalize(nx);
        var y = Normalize(ny);
        var (flags, mouseData) = action switch
        {
            RemoteProtocol.MouseAction.Move => (MOUSEEVENTF_MOVE, 0),
            RemoteProtocol.MouseAction.LeftDown => (MOUSEEVENTF_MOVE | MOUSEEVENTF_LEFTDOWN, 0),
            RemoteProtocol.MouseAction.LeftUp => (MOUSEEVENTF_MOVE | MOUSEEVENTF_LEFTUP, 0),
            RemoteProtocol.MouseAction.RightDown => (MOUSEEVENTF_MOVE | MOUSEEVENTF_RIGHTDOWN, 0),
            RemoteProtocol.MouseAction.RightUp => (MOUSEEVENTF_MOVE | MOUSEEVENTF_RIGHTUP, 0),
            RemoteProtocol.MouseAction.MiddleDown => (MOUSEEVENTF_MOVE | MOUSEEVENTF_MIDDLEDOWN, 0),
            RemoteProtocol.MouseAction.MiddleUp => (MOUSEEVENTF_MOVE | MOUSEEVENTF_MIDDLEUP, 0),
            RemoteProtocol.MouseAction.Wheel => (MOUSEEVENTF_MOVE | MOUSEEVENTF_WHEEL, data),
            _ => (MOUSEEVENTF_MOVE, 0),
        };

        var input = new INPUT
        {
            type = INPUT_MOUSE,
            U = new InputUnion
            {
                mi = new MOUSEINPUT
                {
                    dx = x,
                    dy = y,
                    mouseData = mouseData,
                    dwFlags = flags | MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    public static void Key(ushort vk, bool down)
    {
        if (vk == 0)
        {
            return;
        }

        var flags = KEYEVENTF_SCANCODE_NONE | (down ? 0u : KEYEVENTF_KEYUP);
        if (IsExtendedKey(vk))
        {
            flags |= KEYEVENTF_EXTENDEDKEY;
        }

        var input = new INPUT
        {
            type = INPUT_KEYBOARD,
            U = new InputUnion
            {
                ki = new KEYBDINPUT
                {
                    wVk = vk,
                    wScan = 0,
                    dwFlags = flags,
                    time = 0,
                    dwExtraInfo = IntPtr.Zero,
                },
            },
        };
        SendInput(1, [input], Marshal.SizeOf<INPUT>());
    }

    // 0~1 → 0~65535 (가상 데스크톱 절대좌표). SendInput 은 이 값을 가상 화면 경계에 맞춥니다.
    private static int Normalize(float n) => Math.Clamp((int)Math.Round(n * 65535f), 0, 65535);

    // 확장 키(화살표·Home/End·Ins/Del·PgUp/Dn·우측 Ctrl/Alt·방향 키패드 등)는 EXTENDEDKEY 플래그가 필요합니다.
    private static bool IsExtendedKey(ushort vk) => vk switch
    {
        0x21 or 0x22 or 0x23 or 0x24 => true,          // PageUp, PageDown, End, Home
        0x25 or 0x26 or 0x27 or 0x28 => true,          // Left, Up, Right, Down
        0x2D or 0x2E => true,                           // Insert, Delete
        0x5B or 0x5C or 0x5D => true,                   // LWin, RWin, Apps
        0xA3 or 0xA5 => true,                           // RControl, RMenu(Right Alt)
        0x90 => true,                                   // NumLock
        0x6F => true,                                   // Divide (numpad /)
        _ => false,
    };

    // ===================================================================== P/Invoke

    private const int INPUT_MOUSE = 0;
    private const int INPUT_KEYBOARD = 1;

    private const uint MOUSEEVENTF_MOVE = 0x0001;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
    private const uint MOUSEEVENTF_RIGHTUP = 0x0010;
    private const uint MOUSEEVENTF_MIDDLEDOWN = 0x0020;
    private const uint MOUSEEVENTF_MIDDLEUP = 0x0040;
    private const uint MOUSEEVENTF_WHEEL = 0x0800;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;

    private const uint KEYEVENTF_SCANCODE_NONE = 0x0000;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x0001;
    private const uint KEYEVENTF_KEYUP = 0x0002;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public int type;
        public InputUnion U;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public int mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}
