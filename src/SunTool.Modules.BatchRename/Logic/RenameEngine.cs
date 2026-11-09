using SunTool.Core.FileSystem;

namespace SunTool.Modules.BatchRename.Logic;

public enum RenameStatus
{
    /// <summary>이름이 바뀌지 않음.</summary>
    Unchanged,

    /// <summary>변경 가능.</summary>
    Ok,

    /// <summary>다른 파일과 이름이 겹침.</summary>
    Conflict,

    /// <summary>Windows 에서 쓸 수 없는 이름.</summary>
    Invalid,

    /// <summary>규칙 설정 오류.</summary>
    Error,
}

public sealed record RenamePreview(RenameItem Item, string NewName, RenameStatus Status, string? Message);

/// <summary>
/// 규칙을 적용해 새 이름을 계산하고, 충돌/오류를 검사합니다. 파일 시스템을 바꾸지 않습니다.
/// </summary>
public static class RenameEngine
{
    public static IReadOnlyList<RenamePreview> Preview(
        IReadOnlyList<RenameItem> items,
        IReadOnlyList<IRenameRule> rules,
        bool includeExtension,
        Func<string, IEnumerable<string>>? listDirectory = null)
    {
        listDirectory ??= DefaultListDirectory;
        var count = items.Count;
        var newNames = new string[count];
        var statuses = new RenameStatus[count];
        var messages = new string?[count];
        var folderCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var activeRules = rules.Where(r => r.IsEnabled).ToList();

        for (var i = 0; i < count; i++)
        {
            var item = items[i];
            folderCounters.TryGetValue(item.Directory, out var indexInFolder);
            folderCounters[item.Directory] = indexInFolder + 1;
            var context = new RenameContext(item, i, indexInFolder, count);

            try
            {
                var name = includeExtension ? item.FileName : item.Stem;
                foreach (var rule in activeRules)
                {
                    name = rule.Apply(name, context) ?? string.Empty;
                }

                var fullName = includeExtension ? name : name + item.Extension;
                newNames[i] = fullName;

                if (string.Equals(fullName, item.FileName, StringComparison.Ordinal))
                {
                    statuses[i] = RenameStatus.Unchanged;
                    continue;
                }

                var error = string.IsNullOrWhiteSpace(name) ? Loc.T("이름이 비어 있습니다.") : FileNameValidator.Validate(fullName);
                if (error is not null)
                {
                    statuses[i] = RenameStatus.Invalid;
                    messages[i] = error;
                    continue;
                }

                statuses[i] = RenameStatus.Ok;
            }
            catch (RenameRuleException ex)
            {
                newNames[i] = item.FileName;
                statuses[i] = RenameStatus.Error;
                messages[i] = ex.Message;
            }
        }

        ResolveConflicts(items, newNames, statuses, messages, listDirectory);

        var results = new RenamePreview[count];
        for (var i = 0; i < count; i++)
        {
            results[i] = new RenamePreview(items[i], newNames[i], statuses[i], messages[i]);
        }

        return results;
    }

    /// <summary>
    /// 새 이름끼리 겹치거나, 이름을 바꾸지 않는 기존 파일과 겹치면 Conflict 로 표시합니다.
    /// 충돌로 빠진 파일은 원래 이름을 계속 차지하므로, 더 이상 바뀌지 않을 때까지 반복합니다.
    /// </summary>
    private static void ResolveConflicts(
        IReadOnlyList<RenameItem> items,
        string[] newNames,
        RenameStatus[] statuses,
        string?[] messages,
        Func<string, IEnumerable<string>> listDirectory)
    {
        var directoryEntries = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        HashSet<string> EntriesOf(string directory)
        {
            if (!directoryEntries.TryGetValue(directory, out var set))
            {
                set = new HashSet<string>(listDirectory(directory), StringComparer.OrdinalIgnoreCase);
                directoryEntries[directory] = set;
            }

            return set;
        }

        var changed = true;
        for (var pass = 0; changed && pass < 100; pass++)
        {
            changed = false;

            // 이번 작업으로 비워질 원래 경로들
            var freed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            // 작업 후 각 경로를 차지할 항목 수
            var occupancy = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < items.Count; i++)
            {
                string finalPath;
                if (statuses[i] == RenameStatus.Ok)
                {
                    freed.Add(items[i].FullPath);
                    finalPath = Path.Combine(items[i].Directory, newNames[i]);
                }
                else
                {
                    finalPath = items[i].FullPath;
                }

                occupancy[finalPath] = occupancy.GetValueOrDefault(finalPath) + 1;
            }

            for (var i = 0; i < items.Count; i++)
            {
                if (statuses[i] != RenameStatus.Ok)
                {
                    continue;
                }

                var item = items[i];
                var target = Path.Combine(item.Directory, newNames[i]);
                if (occupancy[target] > 1)
                {
                    statuses[i] = RenameStatus.Conflict;
                    messages[i] = Loc.T("다른 파일과 새 이름이 겹칩니다.");
                    changed = true;
                    continue;
                }

                var isSelf = string.Equals(newNames[i], item.FileName, StringComparison.OrdinalIgnoreCase); // 대소문자만 변경
                if (!isSelf && !freed.Contains(target) && EntriesOf(item.Directory).Contains(newNames[i]))
                {
                    statuses[i] = RenameStatus.Conflict;
                    messages[i] = Loc.T("같은 이름의 파일(또는 폴더)이 이미 있습니다.");
                    changed = true;
                }
            }
        }
    }

    private static IEnumerable<string> DefaultListDirectory(string directory)
    {
        try
        {
            return Directory.EnumerateFileSystemEntries(directory).Select(p => Path.GetFileName(p)).OfType<string>().ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }
}
