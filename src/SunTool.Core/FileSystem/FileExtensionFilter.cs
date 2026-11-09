namespace SunTool.Core.FileSystem;

/// <summary>
/// "jpg;png", "*.jpg, *.png", ".txt" 같은 입력을 확장자 집합으로 바꿉니다.
/// 비어 있으면 모든 파일을 통과시킵니다.
/// </summary>
public sealed class FileExtensionFilter
{
    public static readonly FileExtensionFilter All = new(new HashSet<string>(StringComparer.OrdinalIgnoreCase));

    private readonly HashSet<string> _extensions;

    private FileExtensionFilter(HashSet<string> extensions) => _extensions = extensions;

    public bool IsEmpty => _extensions.Count == 0;

    public IReadOnlyCollection<string> Extensions => _extensions;

    public static FileExtensionFilter Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return All;
        }

        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split(new[] { ';', ',', ' ', '|' },StringSplitOptions.RemoveEmptyEntries))
        {
            var ext = raw.Trim().TrimStart('*').TrimStart('.');
            if (ext.Length > 0)
            {
                set.Add("." + ext);
            }
        }

        return set.Count == 0 ? All : new FileExtensionFilter(set);
    }

    public bool Matches(string fileName) => IsEmpty || _extensions.Contains(Path.GetExtension(fileName));
}
