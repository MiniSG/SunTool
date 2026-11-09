using System.Windows.Threading;

namespace SunTool.Wpf.Services;

/// <summary>백그라운드 스레드에서 UI 스레드로 작업을 넘기기 위한 추상화.</summary>
public interface IUiDispatcher
{
    bool CheckAccess();

    /// <summary>UI 스레드에 낮은 우선순위로 비동기 예약합니다.</summary>
    void Post(Action action);
}

public sealed class WpfUiDispatcher(Dispatcher dispatcher) : IUiDispatcher
{
    public bool CheckAccess() => dispatcher.CheckAccess();

    public void Post(Action action) => dispatcher.BeginInvoke(action, DispatcherPriority.Background);
}
