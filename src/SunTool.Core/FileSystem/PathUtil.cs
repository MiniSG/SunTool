namespace SunTool.Core.FileSystem;

public static class PathUtil
{
    /// <summary>전체 경로로 바꾸고 끝의 구분자를 제거합니다 (드라이브 루트 "C:\" 는 유지).</summary>
    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path);
        var root = Path.GetPathRoot(full);
        if (!string.Equals(full, root, StringComparison.OrdinalIgnoreCase))
        {
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        return full;
    }

    /// <summary>candidate 가 parent 와 같거나 parent 아래에 있으면 true.</summary>
    public static bool IsSameOrSubPath(string candidate, string parent)
    {
        var c = Normalize(candidate);
        var p = Normalize(parent);
        if (string.Equals(c, p, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = p.EndsWith(Path.DirectorySeparatorChar) ? p : p + Path.DirectorySeparatorChar;
        return c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>"name.ext" 가 이미 있으면 "name (2).ext", "name (3).ext" ... 중 비어 있는 경로를 찾습니다.</summary>
    public static string GetUniqueFilePath(string path)
    {
        if (!File.Exists(path) && !Directory.Exists(path))
        {
            return path;
        }

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var stem = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({i}){extension}");
            if (!File.Exists(candidate) && !Directory.Exists(candidate))
            {
                return candidate;
            }
        }
    }

    /// <summary>폴더 이름 (드라이브 루트면 "C" 처럼 문자만).</summary>
    public static string GetFolderName(string path)
    {
        var normalized = Normalize(path);
        var name = Path.GetFileName(normalized);
        if (!string.IsNullOrEmpty(name))
        {
            return name;
        }

        var cleaned = new string(normalized.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrEmpty(cleaned) ? "Root" : cleaned;
    }
}
