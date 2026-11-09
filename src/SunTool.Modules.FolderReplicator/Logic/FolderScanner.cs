using System.Security;
using SunTool.Core.FileSystem;
using SunTool.Core.Logging;
using SunTool.Core.Text;

namespace SunTool.Modules.FolderReplicator.Logic;

/// <summary>원본 폴더의 전체 하위 폴더 트리를 읽습니다. 심볼릭 링크/정션은 따라가지 않습니다.</summary>
public static class FolderScanner
{
    private static readonly EnumerationOptions Options = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0, // 숨김 폴더도 트리에는 보여주고, 제외 여부는 옵션으로 결정
        ReturnSpecialDirectories = false,
    };

    public static Task<FolderNode> ScanAsync(
        string rootPath,
        IProgress<int>? progress,
        IAppLogger? log,
        CancellationToken cancellationToken) =>
        Task.Run(() => Scan(rootPath, progress, log, cancellationToken), cancellationToken);

    public static FolderNode Scan(
        string rootPath,
        IProgress<int>? progress = null,
        IAppLogger? log = null,
        CancellationToken cancellationToken = default)
    {
        log ??= NullLogger.Instance;
        var fullRoot = PathUtil.Normalize(rootPath);
        if (!Directory.Exists(fullRoot))
        {
            throw new DirectoryNotFoundException(Loc.F("폴더를 찾을 수 없습니다: {0}", fullRoot));
        }

        var root = new FolderNode(fullRoot, string.Empty, PathUtil.GetFolderName(fullRoot), null);
        var stack = new Stack<FolderNode>();
        stack.Push(root);
        var visited = 0;

        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = stack.Pop();
            visited++;
            if (visited % 200 == 0)
            {
                progress?.Report(visited);
            }

            try
            {
                var subfolders = new List<FolderNode>();
                foreach (var info in new DirectoryInfo(node.FullPath).EnumerateFileSystemInfos("*", Options))
                {
                    if (info is DirectoryInfo dir)
                    {
                        if ((dir.Attributes & FileAttributes.ReparsePoint) != 0)
                        {
                            log.Debug($"링크/정션 폴더는 건너뜁니다: {dir.FullName}");
                            continue;
                        }

                        var relative = node.RelativePath.Length == 0 ? dir.Name : Path.Combine(node.RelativePath, dir.Name);
                        subfolders.Add(new FolderNode(dir.FullName, relative, dir.Name, node)
                        {
                            IsHiddenOrSystem = (dir.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0,
                        });
                    }
                    else if (info is FileInfo file)
                    {
                        node.FileCount++;
                        node.FileBytes += file.Length;
                    }
                }

                subfolders.Sort((a, b) => NaturalStringComparer.Instance.Compare(a.Name, b.Name));
                node.Children.AddRange(subfolders);
                for (var i = subfolders.Count - 1; i >= 0; i--)
                {
                    stack.Push(subfolders[i]);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
            {
                node.Error = ex.Message;
                log.Warn($"폴더를 읽지 못했습니다: {node.FullPath} ({ex.Message})");
            }
        }

        progress?.Report(visited);
        return root;
    }
}
