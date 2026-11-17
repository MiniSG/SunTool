using System.Security;
using SunTool.Core.FileSystem;
using SunTool.Core.Logging;

namespace SunTool.Modules.FolderReplicator.Logic;

/// <summary>
/// 복제 계획을 세웁니다. 폴더별 모드(<see cref="NodeCopyMode"/>)에 따라:
/// - FolderOnly(기본): 폴더만 대상에 생성
/// - FolderAndFiles: 폴더 생성 + "바로 아래 파일" 복사
/// - Exclude: 폴더·파일 모두 제외하고 그 하위 트리 전체를 건너뜀
/// </summary>
public static class ReplicationPlanner
{
    private static readonly EnumerationOptions TopLevelFiles = new()
    {
        RecurseSubdirectories = false,
        IgnoreInaccessible = true,
        AttributesToSkip = 0,
    };

    /// <summary>문제가 있으면 사용자에게 보여줄 메시지, 없으면 null.</summary>
    public static string? Validate(string? sourceRoot, string? destinationRoot)
    {
        if (string.IsNullOrWhiteSpace(sourceRoot) || !Directory.Exists(sourceRoot))
        {
            return Loc.T("원본 폴더를 먼저 지정하세요.");
        }

        if (string.IsNullOrWhiteSpace(destinationRoot))
        {
            return Loc.T("목적 폴더를 지정하세요.");
        }

        try
        {
            if (PathUtil.IsSameOrSubPath(destinationRoot, sourceRoot))
            {
                return Loc.T("목적 폴더가 원본 폴더와 같거나 그 안에 있습니다.\n다른 위치를 선택하세요.");
            }
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Loc.F("경로가 올바르지 않습니다: {0}", ex.Message);
        }

        return null;
    }

    public static string GetDestinationBase(FolderNode root, ReplicationOptions options) =>
        options.CreateRootFolder
            ? Path.Combine(options.DestinationRoot, root.Name)
            : options.DestinationRoot;

    public static ReplicationPlan Build(
        FolderNode root,
        IReadOnlyDictionary<string, NodeCopyMode> modes,
        ReplicationOptions options,
        IAppLogger? log = null,
        CancellationToken cancellationToken = default)
    {
        log ??= NullLogger.Instance;
        var filter = FileExtensionFilter.Parse(options.ExtensionFilter);
        var destinationBase = GetDestinationBase(root, options);
        var plan = new ReplicationPlan { DestinationBase = destinationBase };

        var stack = new Stack<FolderNode>();
        stack.Push(root);
        while (stack.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var node = stack.Pop();

            var mode = modes.TryGetValue(node.RelativePath, out var m) ? m : NodeCopyMode.FolderOnly;

            // 제외한 폴더는 하위 트리 전체를 건너뜁니다 (부모를 안 만들면 자식도 만들 수 없으므로).
            if (mode == NodeCopyMode.Exclude)
            {
                continue;
            }

            // 숨김/시스템 폴더도 하위 트리 전체를 제외 (루트는 예외)
            if (!node.IsRoot && options.SkipHidden && node.IsHiddenOrSystem)
            {
                continue;
            }

            var destinationFolder = node.RelativePath.Length == 0
                ? destinationBase
                : Path.Combine(destinationBase, node.RelativePath);
            plan.Folders.Add(destinationFolder);

            if (mode == NodeCopyMode.FolderAndFiles)
            {
                plan.SelectedFolderCount++;
                AddFiles(node, destinationFolder, options, filter, plan, log);
            }

            for (var i = node.Children.Count - 1; i >= 0; i--)
            {
                stack.Push(node.Children[i]);
            }
        }

        log.Debug($"계획: 폴더 {plan.Folders.Count}개, 파일 {plan.Files.Count}개, {plan.TotalBytes:N0} bytes");
        return plan;
    }

    private static void AddFiles(
        FolderNode node,
        string destinationFolder,
        ReplicationOptions options,
        FileExtensionFilter filter,
        ReplicationPlan plan,
        IAppLogger log)
    {
        try
        {
            foreach (var file in new DirectoryInfo(node.FullPath).EnumerateFiles("*", TopLevelFiles))
            {
                if (options.SkipHidden && (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0)
                {
                    continue;
                }

                if (!filter.Matches(file.Name))
                {
                    continue;
                }

                plan.Files.Add(new FileCopyItem(file.FullName, Path.Combine(destinationFolder, file.Name), file.Length));
                plan.TotalBytes += file.Length;
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or SecurityException)
        {
            var message = Loc.F("파일 목록을 읽지 못했습니다: {0} ({1})", node.FullPath, ex.Message);
            plan.Warnings.Add(message);
            log.Warn(message);
        }
    }
}
