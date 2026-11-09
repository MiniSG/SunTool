using SunTool.Core.FileSystem;
using SunTool.Core.Logging;
using SunTool.Core.Text;

namespace SunTool.Modules.BatchRename.Logic;

public enum SortKey
{
    NaturalName,
    Name,
    Modified,
    Created,
    Size,
    Manual,
}

/// <summary>드롭된 폴더/파일에서 이름을 바꿀 파일 목록을 모읍니다 (대상은 파일만).</summary>
public static class FileCollector
{
    public static List<RenameItem> Collect(
        IEnumerable<string> sources,
        bool includeSubfolders,
        FileExtensionFilter filter,
        bool skipHidden,
        IReadOnlySet<string>? excluded = null,
        IAppLogger? log = null)
    {
        log ??= NullLogger.Instance;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var items = new List<RenameItem>();
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = includeSubfolders,
            IgnoreInaccessible = true,
            AttributesToSkip = skipHidden ? FileAttributes.Hidden | FileAttributes.System : 0,
        };

        void Add(FileInfo file)
        {
            if (!filter.Matches(file.Name) || excluded?.Contains(file.FullName) == true)
            {
                return;
            }

            if (seen.Add(file.FullName))
            {
                items.Add(new RenameItem(file));
            }
        }

        foreach (var source in sources)
        {
            try
            {
                if (Directory.Exists(source))
                {
                    foreach (var file in new DirectoryInfo(source).EnumerateFiles("*", options))
                    {
                        Add(file);
                    }
                }
                else if (File.Exists(source))
                {
                    var file = new FileInfo(source);
                    if (!skipHidden || (file.Attributes & (FileAttributes.Hidden | FileAttributes.System)) == 0)
                    {
                        Add(file);
                    }
                }
                else
                {
                    log.Warn($"경로를 찾을 수 없습니다: {source}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                log.Warn($"파일 목록을 읽지 못했습니다: {source} ({ex.Message})");
            }
        }

        return items;
    }

    /// <summary>
    /// 폴더별로 묶은 뒤 기준에 따라 정렬합니다. Manual 이면 이전 순서를 유지하고 새 파일은 뒤에 붙입니다.
    /// </summary>
    public static List<RenameItem> Sort(
        IEnumerable<RenameItem> items,
        SortKey key,
        bool descending,
        IReadOnlyList<RenameItem>? previousOrder = null)
    {
        var natural = NaturalStringComparer.Instance;

        if (key == SortKey.Manual)
        {
            var order = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (previousOrder is not null)
            {
                for (var i = 0; i < previousOrder.Count; i++)
                {
                    order.TryAdd(previousOrder[i].FullPath, i);
                }
            }

            return items
                .OrderBy(item => order.TryGetValue(item.FullPath, out var index) ? index : int.MaxValue)
                .ThenBy(item => item.Directory, natural)
                .ThenBy(item => item.FileName, natural)
                .ToList();
        }

        var byFolder = items.OrderBy(item => item.Directory, natural);
        var sorted = key switch
        {
            SortKey.Name => descending
                ? byFolder.ThenByDescending(i => i.FileName, StringComparer.CurrentCultureIgnoreCase)
                : byFolder.ThenBy(i => i.FileName, StringComparer.CurrentCultureIgnoreCase),
            SortKey.Modified => descending ? byFolder.ThenByDescending(i => i.Modified) : byFolder.ThenBy(i => i.Modified),
            SortKey.Created => descending ? byFolder.ThenByDescending(i => i.Created) : byFolder.ThenBy(i => i.Created),
            SortKey.Size => descending ? byFolder.ThenByDescending(i => i.Size) : byFolder.ThenBy(i => i.Size),
            _ => descending ? byFolder.ThenByDescending(i => i.FileName, natural) : byFolder.ThenBy(i => i.FileName, natural),
        };

        return sorted.ThenBy(i => i.FileName, natural).ToList();
    }
}
