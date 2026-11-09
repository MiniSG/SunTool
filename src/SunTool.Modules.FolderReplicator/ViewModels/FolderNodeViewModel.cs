using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.Core.Text;
using SunTool.Modules.FolderReplicator.Logic;

namespace SunTool.Modules.FolderReplicator.ViewModels;

/// <summary>
/// 트리뷰의 폴더 한 줄. 체크는 노드별로 독립적입니다
/// (부모를 체크해도 자식이 자동 체크되지 않음 — 우클릭 메뉴로 일괄 선택).
/// </summary>
public sealed partial class FolderNodeViewModel : ObservableObject
{
    private readonly Action<FolderNodeViewModel, bool> _checkedChanged;

    public FolderNodeViewModel(FolderNode model, FolderNodeViewModel? parent, Action<FolderNodeViewModel, bool> checkedChanged)
    {
        Model = model;
        Parent = parent;
        _checkedChanged = checkedChanged;
        Children = new ObservableCollection<FolderNodeViewModel>(
            model.Children.Select(child => new FolderNodeViewModel(child, this, checkedChanged)));
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
        ? "파일 없음"
        : $"파일 {Model.FileCount:N0}개 · {SizeFormatter.Format(Model.FileBytes)}";

    [ObservableProperty]
    private bool _isChecked;

    [ObservableProperty]
    private bool _isExpanded;

    partial void OnIsCheckedChanged(bool value) => _checkedChanged(this, value);

    [RelayCommand]
    private void CheckSubtree() => SetSubtree(true);

    [RelayCommand]
    private void UncheckSubtree() => SetSubtree(false);

    [RelayCommand]
    private void CheckChildren()
    {
        foreach (var child in Children)
        {
            child.IsChecked = true;
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

    private void SetSubtree(bool value)
    {
        foreach (var node in DescendantsAndSelf())
        {
            node.IsChecked = value;
        }
    }
}
