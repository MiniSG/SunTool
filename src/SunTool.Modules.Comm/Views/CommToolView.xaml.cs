using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using SunTool.Modules.Comm.Logic;
using SunTool.Modules.Comm.ViewModels;

namespace SunTool.Modules.Comm.Views;

public partial class CommToolView : UserControl
{
    private CommToolViewModel? _viewModel;

    public CommToolView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_viewModel is not null)
        {
            _viewModel.EntryAdded -= OnEntryAdded;
            _viewModel.ScrollToRequested -= OnScrollToRequested;
        }

        _viewModel = e.NewValue as CommToolViewModel;
        if (_viewModel is not null)
        {
            _viewModel.EntryAdded += OnEntryAdded;
            _viewModel.ScrollToRequested += OnScrollToRequested;
        }
    }

    /// <summary>검색 ▲▼ 로 고른 줄을 화면에 보이게.</summary>
    private void OnScrollToRequested(CommLogEntry entry) =>
        Dispatcher.BeginInvoke(() => LogGrid.ScrollIntoView(entry), DispatcherPriority.Background);

    /// <summary>F3 / Shift+F3: 화면 어디에서든 다음/이전 일치로 이동.</summary>
    protected override void OnPreviewKeyDown(System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.F3 && _viewModel is not null)
        {
            var back = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) != 0;
            (back ? _viewModel.PreviousMatchCommand : _viewModel.NextMatchCommand).Execute(null);
            e.Handled = true;
            return;
        }

        base.OnPreviewKeyDown(e);
    }

    /// <summary>자동 스크롤: 새 줄이 보이는 줄이면 맨 아래로.</summary>
    private void OnEntryAdded(CommLogEntry entry)
    {
        if (_viewModel?.AutoScroll != true || !IsVisible)
        {
            return;
        }

        Dispatcher.BeginInvoke(() =>
        {
            if (LogGrid.Items.Count > 0)
            {
                LogGrid.ScrollIntoView(LogGrid.Items[^1]);
            }
        }, DispatcherPriority.Background);
    }
}
