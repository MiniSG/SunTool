using System.Windows;
using System.Windows.Controls;
using SunTool.Modules.RemoteView.ViewModels;

namespace SunTool.Modules.RemoteView.Views;

public partial class RemoteViewView : UserControl
{
    private RemoteFullScreenWindow? _fullScreen;

    private bool _inputAttached;

    public RemoteViewView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => AttachInput();
        Loaded += (_, _) => AttachInput();
    }

    private void AttachInput()
    {
        if (_inputAttached || DataContext is not RemoteViewViewModel vm)
        {
            return;
        }

        _inputAttached = true;
        RemoteInput.Attach(RemoteImageElement, RemoteImageElement, vm);
    }

    private void FullScreen_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RemoteViewViewModel vm || !vm.IsConnected)
        {
            return;
        }

        if (_fullScreen is not null)
        {
            _fullScreen.Activate();
            return;
        }

        _fullScreen = new RemoteFullScreenWindow(vm)
        {
            Owner = Window.GetWindow(this),
        };
        _fullScreen.Closed += (_, _) => _fullScreen = null;
        _fullScreen.Show();
    }
}
