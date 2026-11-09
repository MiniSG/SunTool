using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using SunTool.App.ViewModels;
using SunTool.App.Views;

namespace SunTool.App.Services;

/// <summary>디버그 터미널 창 열기/숨기기. ViewModel 은 이 인터페이스만 알면 됩니다.</summary>
public interface IConsoleWindowService
{
    bool IsVisible { get; }

    void Show();

    void Toggle();

    void CloseForShutdown();
}

internal sealed class ConsoleWindowService(IServiceProvider services) : IConsoleWindowService
{
    private LogConsoleWindow? _window;

    public bool IsVisible => _window?.IsVisible == true;

    public void Show()
    {
        _window ??= new LogConsoleWindow { DataContext = services.GetRequiredService<LogConsoleViewModel>() };

        if (!_window.IsVisible)
        {
            _window.Show();
        }

        if (_window.WindowState == WindowState.Minimized)
        {
            _window.WindowState = WindowState.Normal;
        }

        _window.Activate();
    }

    public void Toggle()
    {
        if (IsVisible)
        {
            _window!.Hide();
        }
        else
        {
            Show();
        }
    }

    public void CloseForShutdown()
    {
        if (_window is null)
        {
            return;
        }

        _window.AllowClose = true;
        _window.Close();
        _window = null;
    }
}
