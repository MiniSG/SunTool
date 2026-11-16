using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace SunTool.Modules.RemoteView.Views;

/// <summary>화면을 공유 중일 때 화면 위쪽에 띄우는 작은 알림 띠. "다른 PC가 내 화면을 보는 중".</summary>
public sealed class RemoteIndicatorWindow : Window
{
    private readonly TextBlock _text;

    public RemoteIndicatorWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        Topmost = true;
        SizeToContent = SizeToContent.WidthAndHeight;
        ShowActivated = false;
        WindowStartupLocation = WindowStartupLocation.Manual;

        _text = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };

        Content = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(235, 209, 36, 47)), // 경고 빨강
            CornerRadius = new CornerRadius(0, 0, 8, 8),
            Padding = new Thickness(16, 7, 16, 7),
            Child = _text,
        };

        Loaded += (_, _) => Reposition();
        SizeChanged += (_, _) => Reposition();
    }

    public void SetCount(int count)
    {
        _text.Text = Loc.F("● 다른 PC가 내 화면을 보는 중 · {0}대", count);
    }

    /// <summary>제어 중이면 더 강한 문구로 바꿉니다.</summary>
    public void SetState(int count, bool beingControlled, string? controller)
    {
        _text.Text = beingControlled
            ? Loc.F("● 다른 PC가 내 화면을 제어 중 · {0}", controller ?? "?")
            : Loc.F("● 다른 PC가 내 화면을 보는 중 · {0}대", count);
    }

    private void Reposition()
    {
        // 주 화면 위쪽 가운데.
        Left = (SystemParameters.PrimaryScreenWidth - ActualWidth) / 2;
        Top = 0;
    }
}
