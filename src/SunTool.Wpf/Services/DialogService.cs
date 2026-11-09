using System.Windows;
using Microsoft.Win32;

namespace SunTool.Wpf.Services;

public sealed class DialogService : IDialogService
{
    public string? PickFolder(string title, string? initialDirectory = null)
    {
        var dialog = new OpenFolderDialog { Title = title, Multiselect = false };
        if (!string.IsNullOrWhiteSpace(initialDirectory) && Directory.Exists(initialDirectory))
        {
            dialog.InitialDirectory = initialDirectory;
        }

        var owner = GetOwner();
        var result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return result == true ? dialog.FolderName : null;
    }

    public string? SaveFile(string title, string filter, string defaultFileName)
    {
        var dialog = new SaveFileDialog { Title = title, Filter = filter, FileName = defaultFileName };
        var owner = GetOwner();
        var result = owner is null ? dialog.ShowDialog() : dialog.ShowDialog(owner);
        return result == true ? dialog.FileName : null;
    }

    public void ShowInfo(string message, string title = "SunTool") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void ShowWarning(string message, string title = "SunTool") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);

    public void ShowError(string message, string title = "SunTool 오류") =>
        Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);

    public bool Confirm(string message, string title = "확인") =>
        Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public string? Prompt(string message, string title, string defaultValue = "")
    {
        var dialog = new InputDialog(title, message, defaultValue) { Owner = GetOwner() };
        return dialog.ShowDialog() == true ? dialog.Value : null;
    }

    private static MessageBoxResult Show(string message, string title, MessageBoxButton buttons, MessageBoxImage image)
    {
        var owner = GetOwner();
        return owner is null
            ? MessageBox.Show(message, title, buttons, image)
            : MessageBox.Show(owner, message, title, buttons, image);
    }

    private static Window? GetOwner()
    {
        var app = Application.Current;
        if (app is null)
        {
            return null;
        }

        return app.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w.IsVisible)
               ?? (app.MainWindow is { IsVisible: true } main ? main : null);
    }
}
