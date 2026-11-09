namespace SunTool.Modules.BatchRename.Logic;

/// <summary>이름을 바꿀 파일 한 개 (불변 스냅샷).</summary>
public sealed class RenameItem
{
    public RenameItem(string fullPath, long size, DateTime modified, DateTime created)
    {
        FullPath = fullPath;
        Directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        FileName = Path.GetFileName(fullPath);
        Stem = Path.GetFileNameWithoutExtension(fullPath);
        Extension = Path.GetExtension(fullPath);
        Size = size;
        Modified = modified;
        Created = created;
    }

    public RenameItem(FileInfo file)
        : this(file.FullName, file.Length, file.LastWriteTime, file.CreationTime)
    {
    }

    public string FullPath { get; }

    public string Directory { get; }

    /// <summary>확장자 포함 이름.</summary>
    public string FileName { get; }

    /// <summary>확장자 제외 이름.</summary>
    public string Stem { get; }

    /// <summary>점 포함 확장자 (예: ".jpg"), 없으면 빈 문자열.</summary>
    public string Extension { get; }

    public long Size { get; }

    public DateTime Modified { get; }

    public DateTime Created { get; }

    public static RenameItem FromPath(string path) => new(new FileInfo(path));

    public override string ToString() => FullPath;
}

/// <summary>규칙이 이름을 만들 때 참고하는 정보.</summary>
/// <param name="Index">전체 목록에서의 순번 (0부터).</param>
/// <param name="IndexInFolder">같은 폴더 안에서의 순번 (0부터).</param>
/// <param name="Count">전체 파일 수.</param>
public sealed record RenameContext(RenameItem Item, int Index, int IndexInFolder, int Count);

/// <summary>규칙 설정이 잘못되어 이름을 만들 수 없을 때 (예: 잘못된 정규식).</summary>
public sealed class RenameRuleException(string message) : Exception(message);
