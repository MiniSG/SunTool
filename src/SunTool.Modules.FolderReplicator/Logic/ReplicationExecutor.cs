using System.Diagnostics;
using SunTool.Core.FileSystem;
using SunTool.Core.Logging;

namespace SunTool.Modules.FolderReplicator.Logic;

/// <summary>계획을 실제로 실행합니다. 취소하면 예외 대신 Cancelled=true 결과를 돌려줍니다.</summary>
public static class ReplicationExecutor
{
    private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(80);

    public static Task<ReplicationResult> ExecuteAsync(
        ReplicationPlan plan,
        ConflictPolicy policy,
        IProgress<ReplicationProgress>? progress,
        IAppLogger? log,
        CancellationToken cancellationToken) =>
        Task.Run(() => Execute(plan, policy, progress, log, cancellationToken), CancellationToken.None);

    public static ReplicationResult Execute(
        ReplicationPlan plan,
        ConflictPolicy policy,
        IProgress<ReplicationProgress>? progress = null,
        IAppLogger? log = null,
        CancellationToken cancellationToken = default)
    {
        log ??= NullLogger.Instance;
        var stopwatch = Stopwatch.StartNew();
        var lastReport = TimeSpan.Zero;
        int foldersDone = 0, foldersCreated = 0, foldersExisting = 0;
        int filesDone = 0, filesCopied = 0, filesSkipped = 0, errors = 0;
        long bytesDone = 0, bytesCopied = 0;

        void Report(string current, bool force = false)
        {
            if (progress is null || (!force && stopwatch.Elapsed - lastReport < ReportInterval))
            {
                return;
            }

            lastReport = stopwatch.Elapsed;
            progress.Report(new ReplicationProgress(
                foldersDone, plan.Folders.Count, filesDone, plan.Files.Count, bytesDone, plan.TotalBytes, current));
        }

        ReplicationResult Result(bool cancelled) => new(
            foldersCreated, foldersExisting, filesCopied, filesSkipped, errors, bytesCopied, stopwatch.Elapsed, cancelled);

        // 1) 폴더 구조 생성
        foreach (var folder in plan.Folders)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                log.Warn("사용자가 취소했습니다 (폴더 생성 단계).");
                return Result(true);
            }

            try
            {
                if (Directory.Exists(folder))
                {
                    foldersExisting++;
                }
                else
                {
                    Directory.CreateDirectory(folder);
                    foldersCreated++;
                    log.Trace($"폴더 생성: {folder}");
                }
            }
            catch (Exception ex)
            {
                errors++;
                log.Error($"폴더 생성 실패: {folder}", ex);
            }

            foldersDone++;
            Report(folder);
        }

        // 2) 선택된 폴더의 파일 복사
        foreach (var item in plan.Files)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                log.Warn("사용자가 취소했습니다 (파일 복사 단계).");
                return Result(true);
            }

            try
            {
                if (CopyFile(item, policy, log))
                {
                    filesCopied++;
                    bytesCopied += item.Size;
                }
                else
                {
                    filesSkipped++;
                }
            }
            catch (Exception ex)
            {
                errors++;
                log.Error($"파일 복사 실패: {item.SourcePath} → {item.DestinationPath}", ex);
            }

            filesDone++;
            bytesDone += item.Size;
            Report(item.SourcePath);
        }

        Report(string.Empty, force: true);
        return Result(false);
    }

    /// <summary>복사했으면 true, 정책에 따라 건너뛰었으면 false.</summary>
    private static bool CopyFile(FileCopyItem item, ConflictPolicy policy, IAppLogger log)
    {
        var target = item.DestinationPath;
        var overwrite = false;

        if (File.Exists(target))
        {
            switch (policy)
            {
                case ConflictPolicy.Skip:
                    log.Trace($"이미 있어 건너뜀: {target}");
                    return false;

                case ConflictPolicy.Overwrite:
                    overwrite = true;
                    break;

                case ConflictPolicy.OverwriteIfNewer:
                    if (File.GetLastWriteTimeUtc(item.SourcePath) <= File.GetLastWriteTimeUtc(target))
                    {
                        log.Trace($"대상이 최신이라 건너뜀: {target}");
                        return false;
                    }

                    overwrite = true;
                    break;

                case ConflictPolicy.KeepBoth:
                    target = PathUtil.GetUniqueFilePath(target);
                    break;
            }
        }

        File.Copy(item.SourcePath, target, overwrite);
        log.Trace($"복사: {item.SourcePath} → {target}");
        return true;
    }
}
