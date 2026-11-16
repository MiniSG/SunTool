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

    private void RestoreFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }
}
