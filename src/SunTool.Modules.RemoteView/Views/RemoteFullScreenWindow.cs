using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using SunTool.Modules.RemoteView.ViewModels;

namespace SunTool.Modules.RemoteView.Views;

/// <summary>원격 화면을 전체 화면으로 보여 주는 창. 들어오는 프레임에 맞춰 바뀌고, Esc 로 닫습니다.</summary>
public sealed class RemoteFullScreenWindow : Window
{
    private readonly RemoteViewViewModel _vm;

    public RemoteFullScreenWindow(RemoteViewViewModel vm)
    {
        _vm = vm;
        DataContext = vm;

        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        WindowState = WindowState.Maximized;
        ShowInTaskbar = false;
        Topmost = true;
        Background = Brushes.Black;

        var image = new Image { Stretch = Stretch.Uniform };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        image.SetBinding(Image.SourceProperty, new Binding(nameof(RemoteViewViewModel.RemoteImage)));

        var hint = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(170, 0, 0, 0)),
            CornerRadius = new CornerRadius(0, 0, 8, 8),
            Padding = new Thickness(14, 6, 14, 6),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Top,
            Child = new TextBlock
            {
                Text = Loc.T("Esc 로 전체 화면을 닫습니다."),
                Foreground = Brushes.White,
                FontSize = 13,
            },
        };

        var grid = new Grid();
        grid.Children.Add(image);
        grid.Children.Add(hint);
        Content = grid;

        // 제어 중이면 이 창 위의 마우스·키보드를 상대 PC 로 보냅니다 (Esc 는 제외 → 닫기).
        RemoteInput.Attach(image, this, vm);

        KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
            }
        };

        // 연결이 끊기면 전체 화면도 닫습니다.
        _vm.PropertyChanged += OnVmPropertyChanged;
        Closed += (_, _) => _vm.PropertyChanged -= OnVmPropertyChanged;

        Loaded += (_, _) => Focus();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(RemoteViewViewModel.IsConnected) && !_vm.IsConnected)
        {
            Close();
        }
    }
}
