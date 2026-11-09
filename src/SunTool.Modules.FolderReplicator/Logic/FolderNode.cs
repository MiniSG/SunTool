namespace SunTool.Modules.FolderReplicator.Logic;

/// <summary>스캔한 폴더 트리의 한 노드 (UI 와 무관한 순수 모델).</summary>
public sealed class FolderNode
{
    public FolderNode(string fullPath, string relativePath, string name, FolderNode? parent)
    {
        FullPath = fullPath;
        RelativePath = relativePath;
        Name = name;
        Parent = parent;
    }

    public string FullPath { get; }

    /// <summary>원본 루트 기준 상대 경로. 루트는 빈 문자열.</summary>
    public string RelativePath { get; }

    public string Name { get; }

    public FolderNode? Parent { get; }

    public List<FolderNode> Children { get; } = new();

    /// <summary>이 폴더 바로 아래의 파일 수 (하위 폴더 제외).</summary>
    public int FileCount { get; set; }

    /// <summary>이 폴더 바로 아래 파일들의 총 크기.</summary>
    public long FileBytes { get; set; }

    public bool IsHiddenOrSystem { get; set; }

    /// <summary>읽기 실패 시 오류 메시지.</summary>
    public string? Error { get; set; }

    public bool IsRoot => Parent is null;

    public IEnumerable<FolderNode> DescendantsAndSelf()
    {
        var stack = new Stack<FolderNode>();
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
}
