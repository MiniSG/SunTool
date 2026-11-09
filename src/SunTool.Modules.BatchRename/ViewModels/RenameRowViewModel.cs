using CommunityToolkit.Mvvm.ComponentModel;
using SunTool.Modules.BatchRename.Logic;

namespace SunTool.Modules.BatchRename.ViewModels;

/// <summary>미리보기 표의 한 행.</summary>
public sealed partial class RenameRowViewModel : ObservableObject
{
    public RenameRowViewModel(RenamePreview preview)
    {
        Item = preview.Item;
        Update(preview);
    }

    public RenameItem Item { get; }

    public string OriginalName => Item.FileName;

    public string Folder => Item.Directory;

    [ObservableProperty]
    private string _newName = string.Empty;

    [ObservableProperty]
    private RenameStatus _status;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string? _message;

    public void Update(RenamePreview preview)
    {
        NewName = preview.NewName;
        Status = preview.Status;
        Message = preview.Message;
        StatusText = preview.Status switch
        {
            RenameStatus.Unchanged => Loc.T("변경 없음"),
            RenameStatus.Ok => Loc.T("변경"),
            RenameStatus.Conflict => Loc.T("충돌"),
            RenameStatus.Invalid => Loc.T("이름 오류"),
            RenameStatus.Error => Loc.T("규칙 오류"),
            _ => preview.Status.ToString(),
        };
    }
}
