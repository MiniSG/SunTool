using System.Text.Json;
using SunTool.Core.Logging;
using SunTool.Core.Settings;

namespace SunTool.Modules.BatchRename.Logic;

public sealed record RenameOperation(string SourcePath, string TargetPath);

public sealed record RenameFailure(RenameOperation Operation, string Error);

public sealed record RenameExecutionResult(IReadOnlyList<RenameOperation> Completed, IReadOnlyList<RenameFailure> Failed);

/// <summary>
/// 2단계로 이름을 바꿉니다: (1) 모두 임시 이름으로 (2) 임시 → 최종 이름.
/// a→b, b→a 처럼 서로 이름을 맞바꾸는 경우나 대소문자만 바꾸는 경우도 안전합니다.
/// 1단계에서 실패하면 이미 옮긴 파일을 모두 원래대로 되돌립니다.
/// </summary>
public static class RenameExecutor
{
    private const string TempPrefix = "~srn_";

    public static RenameExecutionResult Execute(IReadOnlyList<RenameOperation> operations, IAppLogger? log = null)
    {
        log ??= NullLogger.Instance;
        var completed = new List<RenameOperation>();
        var failed = new List<RenameFailure>();
        var staged = new List<(RenameOperation Operation, string TempPath)>();

        // 1단계: 원래 이름 → 임시 이름
        foreach (var operation in operations)
        {
            var directory = Path.GetDirectoryName(operation.SourcePath) ?? string.Empty;
            var temp = Path.Combine(directory, $"{TempPrefix}{Guid.NewGuid():N}.tmp");
            try
            {
                File.Move(operation.SourcePath, temp);
                staged.Add((operation, temp));
            }
            catch (Exception ex)
            {
                log.Error($"이름 변경 준비 실패 — 전체 작업을 되돌립니다: {operation.SourcePath}", ex);
                failed.Add(new RenameFailure(operation, ex.Message));
                RollBack(staged, log);
                return new RenameExecutionResult(completed, failed);
            }
        }

        // 2단계: 임시 이름 → 최종 이름
        foreach (var (operation, temp) in staged)
        {
            try
            {
                if (File.Exists(operation.TargetPath) || Directory.Exists(operation.TargetPath))
                {
                    throw new IOException("같은 이름의 파일이 이미 있습니다.");
                }

                File.Move(temp, operation.TargetPath);
                completed.Add(operation);
                log.Trace($"이름 변경: {Path.GetFileName(operation.SourcePath)} → {Path.GetFileName(operation.TargetPath)}");
            }
            catch (Exception ex)
            {
                failed.Add(new RenameFailure(operation, ex.Message));
                log.Error($"이름 변경 실패: {operation.SourcePath} → {operation.TargetPath}", ex);
                TryRestore(temp, operation.SourcePath, log);
            }
        }

        return new RenameExecutionResult(completed, failed);
    }

    private static void RollBack(List<(RenameOperation Operation, string TempPath)> staged, IAppLogger log)
    {
        for (var i = staged.Count - 1; i >= 0; i--)
        {
            TryRestore(staged[i].TempPath, staged[i].Operation.SourcePath, log);
        }
    }

    private static void TryRestore(string temp, string original, IAppLogger log)
    {
        try
        {
            File.Move(temp, original);
        }
        catch (Exception ex)
        {
            log.Error($"원래 이름으로 되돌리지 못했습니다. 파일이 임시 이름으로 남아 있습니다: {temp} (원래: {original})", ex);
        }
    }
}

/// <summary>마지막 이름 변경 기록 (되돌리기용).</summary>
public sealed record RenameJournal(DateTime Timestamp, List<RenameOperation> Operations)
{
    public static void Save(string path, RenameJournal journal)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(path, JsonSerializer.Serialize(journal, JsonSettingsService.SerializerOptions));
    }

    public static RenameJournal? Load(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<RenameJournal>(File.ReadAllText(path), JsonSettingsService.SerializerOptions);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static void Delete(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    /// <summary>되돌리기 작업 목록 (역순, 대상 → 원래 이름).</summary>
    public List<RenameOperation> CreateUndoOperations() =>
        Operations.AsEnumerable().Reverse().Select(o => new RenameOperation(o.TargetPath, o.SourcePath)).ToList();
}
