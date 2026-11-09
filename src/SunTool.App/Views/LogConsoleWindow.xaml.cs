using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using SunTool.App.ViewModels;
using SunTool.Core.Logging;

namespace SunTool.App.Views;

/// <summary>
/// 디버그 터미널 창. 닫기 버튼은 창을 숨기기만 하고(로그 수집은 계속),
/// 앱 종료 시에만 실제로 닫힙니다. 코드 비하인드는 스크롤/포커스/복사 같은 순수 View 동작만 담당합니다.
/// </summary>
public partial class LogConsoleWindow : Window
{
    private LogConsoleViewModel? _viewModel;

    public LogConsoleWindow()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        Activated += (_, _) => CommandInput.Focus();
        PreviewKeyDown += OnPreviewKeyDown;
        CommandBindings.Add(new CommandBinding(ApplicationCommands.Copy, OnCopy, (_, e) => e.CanExecute = LogList.SelectedItems.Count > 0));
    }

    /// <summary>true 면 Close() 가 실제로 창을 닫습니다 (앱 종료 시).</summary>
    public bool AllowClose { get; set; }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (!AllowClose)
        {
            e.Cancel = true;
            Hide();
        }

        base.OnClosing(e);
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.ScrollToEndRequested -= OnScrollToEndRequested;
        }

        _viewModel = e.NewValue as LogConsoleViewModel;
        if (_viewModel is not null)
        {
            _viewModel.ScrollToEndRequested += OnScrollToEndRequested;
        }
    }

    private void OnScrollToEndRequested(object? sender, EventArgs e)
    {
        if (LogList.Items.Count > 0 && IsVisible)
        {
            LogList.ScrollIntoView(LogList.Items[^1]);
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.F12 || (e.Key == Key.Oem3 && Keyboard.Modifiers == ModifierKeys.Control))
        {
            Hide();
            e.Handled = true;
        }
    }

    private void OnCopy(object sender, ExecutedRoutedEventArgs e)
    {
        // 명령 입력줄에 포커스가 있으면 TextBox 기본 복사가 먼저 처리됩니다.
        var selected = LogList.SelectedItems.OfType<LogEntry>().OrderBy(entry => LogList.Items.IndexOf(entry));
        _viewModel?.CopyEntries(selected);
    }
}
