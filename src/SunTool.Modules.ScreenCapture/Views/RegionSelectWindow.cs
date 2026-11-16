using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SunTool.Modules.ScreenCapture.Logic;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Image = System.Windows.Controls.Image;
using Point = System.Windows.Point;
using Rectangle = System.Drawing.Rectangle;
using WpfRectangle = System.Windows.Shapes.Rectangle;

namespace SunTool.Modules.ScreenCapture.Views;

/// <summary>
/// 가상 화면 전체를 먼저 캡처해 그 위에 반투명 오버레이로 띄우고, 드래그한 영역을 잘라내는 창.
/// 화면을 얼려 두고 고르므로 DPI·모니터 여러 대에서도 선택한 그대로 잘립니다.
/// </summary>
public sealed class RegionSelectWindow : Window
{
    private readonly Bitmap _fullScreen;
    private readonly Rectangle _virtualScreen;
    private readonly WpfRectangle _selection;
    private Point _start;
    private bool _dragging;

    private RegionSelectWindow(Rectangle virtualScreen, BitmapSource frozen, Bitmap fullScreen)
    {
        _virtualScreen = virtualScreen;
        _fullScreen = fullScreen;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        AllowsTransparency = false;
        Topmost = true;
        Cursor = Cursors.Cross;
        Background = Brushes.Black;

        var root = new Grid();
        root.Children.Add(new Image { Source = frozen, Stretch = Stretch.Fill });
        root.Children.Add(new Border { Background = new SolidColorBrush(Color.FromArgb(110, 0, 0, 0)) });

        _selection = new WpfRectangle
        {
            Stroke = Brushes.White,
            StrokeThickness = 1.5,
            StrokeDashArray = [4, 3],
            Fill = new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)),
            Visibility = Visibility.Collapsed,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
        };
        var canvas = new Canvas();
        canvas.Children.Add(_selection);
        root.Children.Add(canvas);

        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(180, 0, 0, 0)),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(14, 8, 14, 8),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 24, 0, 0),
            Child = new TextBlock
            {
                Text = Loc.T("드래그해 영역을 고르세요 · Esc 취소"),
                Foreground = Brushes.White,
                FontSize = 14,
            },
        };
        root.Children.Add(hint);

        Content = root;

        MouseLeftButtonDown += OnDown;
        MouseMove += OnMove;
        MouseLeftButtonUp += OnUp;
        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };
        SourceInitialized += OnSourceInitialized;
    }

    /// <summary>잘라낸 영역 비트맵. 취소하면 null.</summary>
    public Bitmap? Result { get; private set; }

    /// <summary>고른 영역의 화면 좌표(절대, 픽셀). 재사용을 위해 돌려줍니다.</summary>
    public Rectangle ResultRegion { get; private set; }

    /// <summary>
    /// 가상 화면을 떠서 사용자가 영역을 고르게 하고, 그 부분을 잘라 돌려줍니다.
    /// <paramref name="region"/> 은 다시 쓸 수 있는 화면 좌표(절대). 취소·빈 선택이면 Image 는 null.
    /// </summary>
    public static (Bitmap? Image, Rectangle Region) CaptureRegionInteractive()
    {
        var monitors = MonitorEnumerator.List();
        var vs = MonitorEnumerator.VirtualScreen(monitors);
        if (vs.Width <= 0 || vs.Height <= 0)
        {
            return (null, Rectangle.Empty);
        }

        var full = ScreenCapturer.CaptureRegion(vs);
        try
        {
            var frozen = BitmapConvert.ToBitmapSource(full);
            var window = new RegionSelectWindow(vs, frozen, full);
            window.ShowDialog();
            return (window.Result, window.ResultRegion);
        }
        finally
        {
            full.Dispose();
        }
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        // DIP 가 아니라 물리 픽셀로 가상 화면 전체를 정확히 덮습니다.
        var handle = new WindowInteropHelper(this).Handle;
        NativeMethods.SetWindowPos(
            handle,
            NativeMethods.HWND_TOPMOST,
            _virtualScreen.Left,
            _virtualScreen.Top,
            _virtualScreen.Width,
            _virtualScreen.Height,
            NativeMethods.SWP_SHOWWINDOW | NativeMethods.SWP_NOACTIVATE);
        Activate();
        Focus();
    }

    private void OnDown(object sender, MouseButtonEventArgs e)
    {
        _dragging = true;
        _start = e.GetPosition(this);
        Canvas.SetLeft(_selection, _start.X);
        Canvas.SetTop(_selection, _start.Y);
        _selection.Width = 0;
        _selection.Height = 0;
        _selection.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    private void OnMove(object sender, MouseEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        var p = e.GetPosition(this);
        var x = Math.Min(p.X, _start.X);
        var y = Math.Min(p.Y, _start.Y);
        Canvas.SetLeft(_selection, x);
        Canvas.SetTop(_selection, y);
        _selection.Width = Math.Abs(p.X - _start.X);
        _selection.Height = Math.Abs(p.Y - _start.Y);
    }

    private void OnUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        ReleaseMouseCapture();

        var p = e.GetPosition(this);
        var x = Math.Min(p.X, _start.X);
        var y = Math.Min(p.Y, _start.Y);
        var w = Math.Abs(p.X - _start.X);
        var h = Math.Abs(p.Y - _start.Y);

        // 창 영역(DIP) → 얼려 둔 이미지 픽셀로 비례 변환.
        var scaleX = _fullScreen.Width / ActualWidth;
        var scaleY = _fullScreen.Height / ActualHeight;
        var rect = new Rectangle(
            (int)Math.Round(x * scaleX),
            (int)Math.Round(y * scaleY),
            (int)Math.Round(w * scaleX),
            (int)Math.Round(h * scaleY));

        rect.Intersect(new Rectangle(0, 0, _fullScreen.Width, _fullScreen.Height));
        if (rect.Width >= 2 && rect.Height >= 2)
        {
            Result = _fullScreen.Clone(rect, _fullScreen.PixelFormat);
            // 얼린 이미지 기준 좌표를 실제 화면 좌표(절대)로 옮깁니다.
            ResultRegion = new Rectangle(_virtualScreen.Left + rect.X, _virtualScreen.Top + rect.Y, rect.Width, rect.Height);
        }

        Close();
    }
}
