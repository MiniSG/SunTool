namespace SunTool.Core;

/// <summary>
/// 포터블 경로 규칙.
/// exe가 있는 폴더에 쓸 수 있으면 그 폴더에 설정/로그를 두고(포터블 모드),
/// 쓸 수 없으면 %AppData%\SunTool 로 대체합니다.
/// </summary>
public static class AppPaths
{
    private static readonly Lazy<(string Directory, bool Portable)> Data = new(ResolveDataDirectory);

    public static string BaseDirectory => AppContext.BaseDirectory;

    public static string DataDirectory => Data.Value.Directory;

    public static bool IsPortable => Data.Value.Portable;

    public static string SettingsFile => Path.Combine(DataDirectory, "SunTool.settings.json");

    public static string LogDirectory => Path.Combine(DataDirectory, "logs");

    public static string UndoDirectory => Path.Combine(DataDirectory, "undo");

    private static (string, bool) ResolveDataDirectory()
    {
        var baseDir = BaseDirectory;
        if (CanWrite(baseDir))
        {
            return (baseDir, true);
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "SunTool");
        Directory.CreateDirectory(fallback);
        return (fallback, false);
    }

    private static bool CanWrite(string directory)
    {
        try
        {
            var probe = Path.Combine(directory, $".write-test-{Guid.NewGuid():N}");
            File.WriteAllText(probe, string.Empty);
            File.Delete(probe);
            return true;
        }
        catch
        {
            return false;
        }
    }
}
