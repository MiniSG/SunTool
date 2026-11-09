namespace SunTool.Modules.FolderReplicator.Logic;

/// <summary>대상 위치에 같은 이름의 파일이 있을 때의 처리 방식.</summary>
public enum ConflictPolicy
{
    Skip,
    Overwrite,
    OverwriteIfNewer,
    KeepBoth,
}

public sealed class ReplicationOptions
{
    public string DestinationRoot { get; init; } = string.Empty;

    /// <summary>true 면 대상 아래에 원본 폴더 이름으로 최상위 폴더를 만듭니다.</summary>
    public bool CreateRootFolder { get; init; } = true;

    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.Skip;

    /// <summary>숨김/시스템 폴더(하위 포함)와 파일을 제외합니다.</summary>
    public bool SkipHidden { get; init; } = true;

    /// <summary>복사할 파일 확장자 필터 (비우면 전체).</summary>
    public string? ExtensionFilter { get; init; }
}

public sealed record FileCopyItem(string SourcePath, string DestinationPath, long Size);

public sealed class ReplicationPlan
{
    public string DestinationBase { get; init; } = string.Empty;

    public List<string> Folders { get; } = new();

    public List<FileCopyItem> Files { get; } = new();

    public List<string> Warnings { get; } = new();

    public long TotalBytes { get; set; }

    public int SelectedFolderCount { get; set; }
}

public sealed record ReplicationProgress(
    int FoldersDone,
    int FoldersTotal,
    int FilesDone,
    int FilesTotal,
    long BytesDone,
    long BytesTotal,
    string CurrentItem)
{
    public double Percent
    {
        get
        {
            // 폴더 생성 10%, 파일 복사 90% 비중으로 계산
            var folderPart = FoldersTotal == 0 ? 1.0 : (double)FoldersDone / FoldersTotal;
            double filePart;
            if (BytesTotal > 0)
            {
                filePart = (double)BytesDone / BytesTotal;
            }
            else
            {
                filePart = FilesTotal == 0 ? 1.0 : (double)FilesDone / FilesTotal;
            }

            return Math.Clamp((folderPart * 10) + (filePart * 90), 0, 100);
        }
    }
}

public sealed record ReplicationResult(
    int FoldersCreated,
    int FoldersExisting,
    int FilesCopied,
    int FilesSkipped,
    int Errors,
    long BytesCopied,
    TimeSpan Elapsed,
    bool Cancelled);
