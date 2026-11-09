namespace SunTool.Wpf.Services;

/// <summary>ViewModel 이 Window/MessageBox 를 직접 만들지 않도록 감싼 대화상자 서비스.</summary>
public interface IDialogService
{
    string? PickFolder(string title, string? initialDirectory = null);

    string? SaveFile(string title, string filter, string defaultFileName);

    void ShowInfo(string message, string title = "SunTool");

    void ShowWarning(string message, string title = "SunTool");

    void ShowError(string message, string title = "SunTool 오류");

    bool Confirm(string message, string title = "확인");

    /// <summary>한 줄 텍스트 입력. 취소하면 null.</summary>
    string? Prompt(string message, string title, string defaultValue = "");
}
