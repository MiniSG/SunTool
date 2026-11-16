using System.Windows;
using System.Windows.Controls;
using SunTool.Modules.RemoteView.ViewModels;

namespace SunTool.Modules.RemoteView.Views;

public partial class RemoteViewView : UserControl
{
    private RemoteFullScreenWindow? _fullScreen;

    public RemoteViewView()
    {
        InitializeComponent();
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
