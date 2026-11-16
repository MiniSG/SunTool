using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using SunTool.Modules.ScreenCapture.ViewModels;

namespace SunTool.Modules.ScreenCapture.Views;

public partial class ScreenCaptureView : UserControl
{
    private readonly List<UIElement> _added = [];
    private ScreenCaptureViewModel? _vm;
    private Brush _color = Brushes.Red;
    private double _thickness = 3;
    private double _fontSize = 32;
    private int[] _customColors = [];

    private Rectangle? _dragRect;
    private Point _dragStart;
    private bool _dragging;

    public ScreenCaptureView()
    {
        InitializeComponent();
        BuildSizeChoices();
        DataContextChanged += OnDataContextChanged;
    }

    // ===================================================================== 데이터 연결

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }

        _vm = DataContext as ScreenCaptureViewModel;
        if (_vm is not null)
        {
            _vm.PropertyChanged += OnVmPropertyChanged;
        }

        SyncToImage();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ScreenCaptureViewModel.LastImage))
        {
            // 새로 캡처하면 주석을 지우고 호스트 크기를 이미지 픽셀에 맞춥니다.
            SyncToImage();
        }
    }

    private void SyncToImage()
    {
        ClearAnnotations();
        if (_vm?.LastImage is { } img)
        {
            AnnotationHost.Width = img.PixelWidth;
            AnnotationHost.Height = img.PixelHeight;
        }
        else
        {
            AnnotationHost.Width = double.NaN;
            AnnotationHost.Height = double.NaN;
        }
    }

    // ===================================================================== 도구·크기·색

    private void BuildSizeChoices()
    {
        foreach (var s in new[] { 16, 20, 24, 32, 40, 56, 72, 96 })
        {
            FontSizeCombo.Items.Add(s);
        }

        FontSizeCombo.SelectedItem = (int)_fontSize;

        foreach (var t in new[] { 1, 2, 3, 5, 8, 12, 16 })
        {
            ThicknessCombo.Items.Add(t);
        }

        ThicknessCombo.SelectedItem = (int)_thickness;
    }

    private void FontSize_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (FontSizeCombo.SelectedItem is int v)
        {
            _fontSize = v;
        }
    }

    private void Thickness_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (ThicknessCombo.SelectedItem is int v)
        {
            _thickness = v;
        }
    }

    private void Color_Click(object sender, RoutedEventArgs e)
    {
        using var dialog = new System.Windows.Forms.ColorDialog
        {
            AllowFullOpen = true,
            FullOpen = true,
            Color = ToDrawingColor(_color),
            CustomColors = _customColors,
        };

        if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _customColors = dialog.CustomColors;
            var c = dialog.Color;
            var brush = new SolidColorBrush(Color.FromArgb(c.A, c.R, c.G, c.B));
            brush.Freeze();
            _color = brush;
            ColorSwatch.Background = _color;
        }
    }

    private static System.Drawing.Color ToDrawingColor(Brush brush)
    {
        if (brush is SolidColorBrush s)
        {
            return System.Drawing.Color.FromArgb(s.Color.A, s.Color.R, s.Color.G, s.Color.B);
        }

        return System.Drawing.Color.Red;
    }

    private void Tool_Changed(object sender, RoutedEventArgs e)
    {
        if (AnnotationCanvas is not null)
        {
            AnnotationCanvas.Cursor = ToolBox.IsChecked == true || ToolText.IsChecked == true ? Cursors.Cross : Cursors.Arrow;
        }
    }

    // ===================================================================== 캔버스 그리기

    private void Canvas_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_vm?.LastImage is null)
        {
            return;
        }

        var p = e.GetPosition(AnnotationCanvas);

        if (ToolBox.IsChecked == true)
        {
            _dragging = true;
            _dragStart = p;
            _dragRect = new Rectangle
            {
                Stroke = _color,
                StrokeThickness = _thickness,
                Fill = Brushes.Transparent,
            };
            Canvas.SetLeft(_dragRect, p.X);
            Canvas.SetTop(_dragRect, p.Y);
            AnnotationCanvas.Children.Add(_dragRect);
            AnnotationCanvas.CaptureMouse();
        }
        else if (ToolText.IsChecked == true)
        {
            AddTextBox(p);
        }
    }

    private void Canvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_dragging || _dragRect is null)
        {
            return;
        }

        var p = e.GetPosition(AnnotationCanvas);
        Canvas.SetLeft(_dragRect, Math.Min(p.X, _dragStart.X));
        Canvas.SetTop(_dragRect, Math.Min(p.Y, _dragStart.Y));
        _dragRect.Width = Math.Abs(p.X - _dragStart.X);
        _dragRect.Height = Math.Abs(p.Y - _dragStart.Y);
    }

    private void Canvas_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_dragging)
        {
            return;
        }

        _dragging = false;
        AnnotationCanvas.ReleaseMouseCapture();

        if (_dragRect is not null)
        {
            if (_dragRect.Width < 4 || _dragRect.Height < 4)
            {
                AnnotationCanvas.Children.Remove(_dragRect);
            }
            else
            {
                _added.Add(_dragRect);
            }

            _dragRect = null;
        }
    }

    private void AddTextBox(Point p)
    {
        var box = new TextBox
        {
            FontSize = _fontSize,
            FontWeight = FontWeights.Bold,
            Foreground = _color,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            MinWidth = 24,
            AcceptsReturn = false,
        };
        Canvas.SetLeft(box, p.X);
        Canvas.SetTop(box, p.Y);
        box.LostKeyboardFocus += (_, _) =>
        {
            if (string.IsNullOrEmpty(box.Text))
            {
                AnnotationCanvas.Children.Remove(box);
                _added.Remove(box);
            }
        };

        AnnotationCanvas.Children.Add(box);
        _added.Add(box);
        box.Focus();
    }

    // ===================================================================== 도구 버튼

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_added.Count == 0)
        {
            return;
        }

        var last = _added[^1];
        _added.RemoveAt(_added.Count - 1);
        AnnotationCanvas.Children.Remove(last);
    }

    private void ClearAnno_Click(object sender, RoutedEventArgs e) => ClearAnnotations();

    private void ClearAnnotations()
    {
        if (AnnotationCanvas is not null)
        {
            AnnotationCanvas.Children.Clear();
        }

        _added.Clear();
        _dragRect = null;
        _dragging = false;
    }

    private void SaveAnno_Click(object sender, RoutedEventArgs e)
    {
        if (Compose() is { } image)
        {
            _vm?.SaveComposed(image);
        }
    }

    private void CopyAnno_Click(object sender, RoutedEventArgs e)
    {
        if (Compose() is { } image)
        {
            _vm?.CopyComposed(image);
        }
    }

    /// <summary>이미지 + 주석을 이미지 원래 해상도로 렌더링합니다.</summary>
    private BitmapSource? Compose()
    {
        if (_vm?.LastImage is not { } img)
        {
            return null;
        }

        // 편집 중 텍스트 상자의 커서가 찍히지 않도록 포커스를 뺍니다.
        AnnotationHost.Focus();
        Keyboard.ClearFocus();
        AnnotationHost.UpdateLayout();

        var rtb = new RenderTargetBitmap(img.PixelWidth, img.PixelHeight, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(AnnotationHost);
        rtb.Freeze();
        return rtb;
    }
}
