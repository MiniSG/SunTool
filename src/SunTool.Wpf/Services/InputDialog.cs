using System.Windows;
using System.Windows.Controls;

namespace SunTool.Wpf.Services;

/// <summary>한 줄 입력용 간단한 대화상자 (XAML 없이 코드로 구성).</summary>
internal sealed class InputDialog : Window
{
    private readonly TextBox _input;

    public InputDialog(string title, string message, string defaultValue)
    {
        Title = title;
        Width = 400;
        SizeToContent = SizeToContent.Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ShowInTaskbar = false;

        var panel = new StackPanel { Margin = new Thickness(16) };
        panel.Children.Add(new TextBlock
        {
            Text = message,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 0, 0, 8),
        });

        _input = new TextBox { Text = defaultValue, Padding = new Thickness(4, 3, 4, 3) };
        panel.Children.Add(_input);

        var buttons = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 14, 0, 0),
        };

        var ok = new Button { Content = "확인", IsDefault = true, MinWidth = 80, Margin = new Thickness(0, 0, 8, 0) };
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "취소", IsCancel = true, MinWidth = 80 };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);
        panel.Children.Add(buttons);

        Content = panel;
        Loaded += (_, _) =>
        {
            _input.Focus();
            _input.SelectAll();
        };
    }

    public string Value => _input.Text;
}
