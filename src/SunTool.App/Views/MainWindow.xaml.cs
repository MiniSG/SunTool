using System.Diagnostics;
using System.Windows;
using WinForms = System.Windows.Forms;

namespace SunTool.App.Views;

public partial class MainWindow : Window
{
    private WinForms.NotifyIcon? _tray;
    private bool _balloonShown;

    public MainWindow()
    {
        InitializeComponent();
        SetupTray();
        StateChanged += OnStateChanged;
        Closed += (_, _) =>
        {
            _tray?.Dispose();
            _tray = null;
        };
    }

    private void SetupTray()
    {
        _tray = new WinForms.NotifyIcon
        {
            Text = "SunTool",
            Visible = true,
        };

        try
        {
            var exe = Process.GetCurrentProcess().MainModule?.FileName;
            if (exe is not null)
            {
                _tray.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
            }
        }
        catch
        {
            // 아이콘을 못 얻으면 기본 아이콘으로 둡니다.
        }

        var menu = new WinForms.ContextMenuStrip();
        menu.Items.Add(Loc.T("열기"), null, (_, _) => RestoreFromTray());
        menu.Items.Add(Loc.T("종료"), null, (_, _) => System.Windows.Application.Current.Shutdown());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        // 최소화하면 작업 표시줄에서 빠지고 트레이에만 남깁니다.
        if (WindowState == WindowState.Minimized)
        {
            Hide();
            if (!_balloonShown)
            {
                _balloonShown = true;
                _tray?.ShowBalloonTip(2000, "SunTool",
                    Loc.T("트레이로 보냈습니다. 트레이 아이콘을 두 번 누르면 다시 엽니다."), WinForms.ToolTipIcon.Info);
            }
        }
    }

    private void RestoreFromTray() => BringToForeground();

    /// <summary>창을 트레이·최소화에서 꺼내 앞으로 가져옵니다(다른 인스턴스의 요청 포함).</summary>
    public void BringToForeground()
    {
        Show();
        if (WindowState == WindowState.Minimized)
        {
            WindowState = WindowState.Normal;
        }

        Activate();

        // 다른 프로세스의 신호로 호출될 때도 확실히 맨 앞으로 오도록 잠깐 Topmost 를 깜빡입니다.
        var wasTopmost = Topmost;
        Topmost = true;
        Topmost = wasTopmost;
        Focus();
    }
}
