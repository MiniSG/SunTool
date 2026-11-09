namespace SunTool.Tests;

/// <summary>테스트마다 쓰고 지우는 임시 폴더.</summary>
internal sealed class TempFolder : IDisposable
{
    public TempFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "SunToolTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path);
    }

    public string Path { get; }

    public string Dir(params string[] parts)
    {
        var full = System.IO.Path.Combine([Path, .. parts]);
        Directory.CreateDirectory(full);
        return full;
    }

    public string File(string relativePath, string content = "x")
    {
        var full = System.IO.Path.Combine(Path, relativePath);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        System.IO.File.WriteAllText(full, content);
        return full;
    }

    public void Dispose()
    {
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch
        {
            // 정리 실패는 무시
        }
    }
}
