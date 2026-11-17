using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.Core.Text;
using SunTool.Modules.FolderReplicator.Logic;

namespace SunTool.Modules.FolderReplicator.ViewModels;

/// <summary>
/// 트리뷰의 폴더 한 줄. 복사 모드(폴더만 → 폴더+파일 → 제외)는 노드별로 독립적입니다
/// (부모를 바꿔도 자식이 자동으로 바뀌지 않음 — 우클릭 메뉴로 하위까지 일괄 적용).
/// </summary>
public sealed partial class FolderNodeViewModel : ObservableObject
{
    private readonly Action<FolderNodeViewModel> _modeChanged;

    public FolderNodeViewModel(FolderNode model, FolderNodeViewModel? parent, Action<FolderNodeViewModel> modeChanged)
    {
        Model = model;
        Parent = parent;
        _modeChanged = modeChanged;
        Children = new ObservableCollection<FolderNodeViewModel>(
            model.Children.Select(child => new FolderNodeViewModel(child, this, modeChanged)));
    }

    public FolderNode Model { get; }

    public FolderNodeViewModel? Parent { get; }

    public ObservableCollection<FolderNodeViewModel> Children { get; }

    public string Name => Model.Name;

    public string RelativePath => Model.RelativePath;

    public bool IsHidden => Model.IsHiddenOrSystem;

    public string? Error => Model.Error;

    public bool HasError => Model.Error is not null;

    public string Detail => Model.FileCount == 0
        ? Loc.T("파일 없음")
        : Loc.F("파일 {0:N0}개 · {1}", Model.FileCount, SizeFormatter.Format(Model.FileBytes));

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeGlyph), nameof(ModeLabel), nameof(IsFolderAndFiles), nameof(IsExcluded))]
    private NodeCopyMode _copyMode = NodeCopyMode.FolderOnly;

    [ObservableProperty]
    private bool _isExpanded;

    partial void OnCopyModeChanged(NodeCopyMode value) => _modeChanged(this);

    public bool IsFolderAndFiles => CopyMode == NodeCopyMode.FolderAndFiles;

    public bool IsExcluded => CopyMode == NodeCopyMode.Exclude;

    /// <summary>순환 버튼에 보일 아이콘 (Segoe MDL2).</summary>
    public string ModeGlyph => CopyMode switch
    {
        NodeCopyMode.FolderAndFiles => "", // 폴더(파일 포함) — 글자로 구분
        NodeCopyMode.Exclude => "",        // 취소(제외)
        _ => "",                            // 폴더만
    };

    /// <summary>순환 버튼에 보일 글자.</summary>
    public string ModeLabel => CopyMode switch
    {
        NodeCopyMode.FolderAndFiles => Loc.T("폴더+파일"),
        NodeCopyMode.Exclude => Loc.T("제외"),
        _ => Loc.T("폴더만"),
    };

    [RelayCommand]
    private void CycleMode() => CopyMode = CopyMode switch
    {
        NodeCopyMode.FolderOnly => NodeCopyMode.FolderAndFiles,
        NodeCopyMode.FolderAndFiles => NodeCopyMode.Exclude,
        _ => NodeCopyMode.FolderOnly,
    };

    [RelayCommand]
    private void CheckSubtree() => SetSubtree(NodeCopyMode.FolderAndFiles);

    [RelayCommand]
    private void UncheckSubtree() => SetSubtree(NodeCopyMode.FolderOnly);

    [RelayCommand]
    private void ExcludeSubtree() => SetSubtree(NodeCopyMode.Exclude);

    [RelayCommand]
    private void CheckChildren()
    {
        foreach (var child in Children)
        {
            child.CopyMode = NodeCopyMode.FolderAndFiles;
        }
    }

    [RelayCommand]
    private void ExpandSubtree()
    {
        foreach (var node in DescendantsAndSelf())
        {
            node.IsExpanded = true;
        }
    }

    public IEnumerable<FolderNodeViewModel> DescendantsAndSelf()
    {
        var stack = new Stack<FolderNodeViewModel>();
        stack.Push(this);
        while (stack.Count > 0)
        {
            var node = stack.Pop();
            yield return node;
            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
        }
    }

    private void SetSubtree(NodeCopyMode mode)
    {
        foreach (var node in DescendantsAndSelf())
        {
            node.CopyMode = mode;
        }
    }
}
