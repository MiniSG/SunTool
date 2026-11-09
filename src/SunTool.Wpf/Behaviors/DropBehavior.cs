using System.Windows;
using System.Windows.Input;

namespace SunTool.Wpf.Behaviors;

/// <summary>
/// 탐색기에서 끌어다 놓은 파일/폴더 경로(string[])를 ViewModel 커맨드로 전달하는 첨부 속성.
/// 사용: &lt;Border b:DropBehavior.Command="{Binding DropCommand}" /&gt;
/// 드래그 중에는 b:DropBehavior.IsDragOver 가 true 가 되어 스타일 트리거에서 강조할 수 있습니다.
/// </summary>
public static class DropBehavior
{
    public static readonly DependencyProperty CommandProperty = DependencyProperty.RegisterAttached(
        "Command",
        typeof(ICommand),
        typeof(DropBehavior),
        new PropertyMetadata(null, OnCommandChanged));

    public static readonly DependencyProperty IsDragOverProperty = DependencyProperty.RegisterAttached(
        "IsDragOver",
        typeof(bool),
        typeof(DropBehavior),
        new PropertyMetadata(false));

    public static ICommand? GetCommand(DependencyObject element) => (ICommand?)element.GetValue(CommandProperty);

    public static void SetCommand(DependencyObject element, ICommand? value) => element.SetValue(CommandProperty, value);

    public static bool GetIsDragOver(DependencyObject element) => (bool)element.GetValue(IsDragOverProperty);

    public static void SetIsDragOver(DependencyObject element, bool value) => element.SetValue(IsDragOverProperty, value);

    private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
        {
            return;
        }

        element.PreviewDragEnter -= OnDragOver;
        element.PreviewDragOver -= OnDragOver;
        element.PreviewDragLeave -= OnDragLeave;
        element.PreviewDrop -= OnDrop;

        if (e.NewValue is null)
        {
            return;
        }

        element.AllowDrop = true;
        element.PreviewDragEnter += OnDragOver;
        element.PreviewDragOver += OnDragOver;
        element.PreviewDragLeave += OnDragLeave;
        element.PreviewDrop += OnDrop;
    }

    private static void OnDragOver(object sender, DragEventArgs e)
    {
        var element = (DependencyObject)sender;
        var accepted = e.Data.GetDataPresent(DataFormats.FileDrop)
                       && GetCommand(element) is { } command
                       && command.CanExecute(GetPaths(e));

        e.Effects = accepted ? DragDropEffects.Copy : DragDropEffects.None;
        SetIsDragOver(element, accepted);
        e.Handled = true;
    }

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        SetIsDragOver((DependencyObject)sender, false);
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        var element = (DependencyObject)sender;
        SetIsDragOver(element, false);

        var paths = GetPaths(e);
        if (paths.Length > 0 && GetCommand(element) is { } command && command.CanExecute(paths))
        {
            command.Execute(paths);
        }

        e.Handled = true;
    }

    private static string[] GetPaths(DragEventArgs e) =>
        e.Data.GetData(DataFormats.FileDrop) as string[] ?? Array.Empty<string>();
}
