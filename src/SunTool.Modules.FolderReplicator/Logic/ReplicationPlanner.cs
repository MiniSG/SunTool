using System.Security;
using SunTool.Core.FileSystem;
using SunTool.Core.Logging;

namespace SunTool.Modules.FolderReplicator.Logic;

/// <summary>
/// 복제 계획을 세웁니다.
/// - 모든 폴더(숨김 제외 옵션 적용)는 대상에 그대로 생성
/// - 선택된 폴더는 "바로 아래 파일"만 복사 (그 안의 하위 폴더 내용은 선택 여부에 따름)
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
        IReadOnlySet<string> selectedRelativePaths,
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

            // 숨김/시스템 폴더는 하위 트리 전체를 제외 (루트는 예외)
            if (!node.IsRoot && options.SkipHidden && node.IsHiddenOrSystem)
            {
                continue;
            }

            var destinationFolder = node.RelativePath.Length == 0
                ? destinationBase
                : Path.Combine(destinationBase, node.RelativePath);
            plan.Folders.Add(destinationFolder);

            if (selectedRelativePaths.Contains(node.RelativePath))
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
