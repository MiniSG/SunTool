using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SunTool.App.Services;

/// <summary>
/// 중복 실행 감지 + 기존 인스턴스 창을 앞으로 가져오기.
/// 네임드 Mutex 로 이미 실행 중인지 보고, 네임드 EventWaitHandle 로
/// 기존(최초) 인스턴스에 "창을 앞으로 가져와라" 신호를 보냅니다. 범위는 Local\ (로그인 세션당).
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private const string MutexName = @"Local\SunTool.SingleInstance.v1";
    private const string EventName = @"Local\SunTool.Activate.v1";

    private readonly Mutex _mutex;
    private EventWaitHandle? _activateEvent;
    private RegisteredWaitHandle? _registration;

    private SingleInstance(Mutex mutex, bool isFirst)
    {
        _mutex = mutex;
        IsFirstInstance = isFirst;
    }

    /// <summary>이 인스턴스가 최초(= 뮤텍스 소유자)인지 여부.</summary>
    public bool IsFirstInstance { get; }

    /// <summary>
    /// 이미 실행 중인 인스턴스가 있는지 확인하고 이 인스턴스를 등록합니다.
    /// <paramref name="waitForRelease"/> 가 true 이면(언어 전환 재시작 등) 이전 인스턴스가
    /// 종료하며 뮤텍스를 놓을 때까지 잠시 기다렸다가 소유권을 넘겨받습니다.
    /// </summary>
    public static SingleInstance Acquire(bool waitForRelease)
    {
        var mutex = new Mutex(false, MutexName);
        bool isFirst;
        try
        {
            isFirst = mutex.WaitOne(waitForRelease ? TimeSpan.FromSeconds(5) : TimeSpan.Zero, false);
        }
        catch (AbandonedMutexException)
        {
            // 이전 인스턴스가 비정상 종료해 뮤텍스를 버린 경우 — 우리가 소유권을 가집니다.
            isFirst = true;
        }

        return new SingleInstance(mutex, isFirst);
    }

    /// <summary>
    /// 두 번째 인스턴스가 보내는 "앞으로 와라" 신호를 받을 때 실행할 동작을 등록합니다(최초 인스턴스용).
    /// </summary>
    public void ListenForActivation(Action onActivate)
    {
        _activateEvent = new EventWaitHandle(false, EventResetMode.AutoReset, EventName);
        _registration = ThreadPool.RegisterWaitForSingleObject(
            _activateEvent,
            (_, _) => onActivate(),
            null,
            Timeout.Infinite,
            executeOnlyOnce: false);
    }

    /// <summary>
    /// 같은 이름의 다른 SunTool 프로세스가 이미 떠 있는지 봅니다.
    /// 뮤텍스가 없던 구버전도 감지하기 위한 보조 수단입니다(이름 기준이라 MainModule 접근이 필요 없음).
    /// </summary>
    public static bool AnyOtherProcessRunning()
    {
        try
        {
            using var me = Process.GetCurrentProcess();
            var others = Process.GetProcessesByName(me.ProcessName);
            try
            {
                return others.Any(p => p.Id != me.Id);
            }
            finally
            {
                foreach (var p in others)
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 신호를 못 받는 구버전 인스턴스를 위해, 다른 SunTool 프로세스의 주 창을 직접 앞으로 가져옵니다(최선 노력).
    /// 트레이로 숨어 있거나 주 창 핸들이 없으면 활성화되지 않을 수 있습니다.
    /// </summary>
    public static void TryActivateOtherProcessWindow()
    {
        try
        {
            using var me = Process.GetCurrentProcess();
            foreach (var p in Process.GetProcessesByName(me.ProcessName))
            {
                try
                {
                    if (p.Id == me.Id)
                    {
                        continue;
                    }

                    var handle = p.MainWindowHandle;
                    if (handle != IntPtr.Zero)
                    {
                        ShowWindow(handle, SW_RESTORE);
                        SetForegroundWindow(handle);
                    }
                }
                catch
                {
                    // 권한 등으로 접근 못 하면 넘어갑니다.
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            // 전체 실패해도 치명적이지 않습니다.
        }
    }

    /// <summary>기존 인스턴스에게 창을 앞으로 가져오라고 신호를 보냅니다(두 번째 인스턴스용).</summary>
    public static void SignalExistingToForeground()
    {
        try
        {
            if (EventWaitHandle.TryOpenExisting(EventName, out var handle))
            {
                using (handle)
                {
                    handle.Set();
                }
            }
        }
        catch
        {
            // 신호를 못 보내도 치명적이지 않습니다(기존 창은 그대로 있습니다).
        }
    }

    public void Dispose()
    {
        _registration?.Unregister(null);
        _activateEvent?.Dispose();

        try
        {
            if (IsFirstInstance)
            {
                _mutex.ReleaseMutex();
            }
        }
        catch
        {
            // 이미 놓였거나 소유하지 않은 경우 무시.
        }

        _mutex.Dispose();
    }

    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
