using System.Globalization;
using System.Text;
using SunTool.Core.Text;

namespace SunTool.Modules.MemoryWatch.Logic;

/// <summary>측정값 CSV (엑셀에서 한글이 깨지지 않도록 UTF-8 BOM). 메모리는 MB.</summary>
public static class MemoryCsv
{
    public static string Header => CsvWriter.Line(
        Loc.T("시각"), "PID", Loc.T("커밋 (MB)"), Loc.T("페이징 풀 (KB)"), Loc.T("NP 풀 (KB)"), Loc.T("가상 크기 (MB)"),
        Loc.T("핸들"), "GDI", "USER", Loc.T("스레드"), Loc.T("시스템 커밋 (MB)"), Loc.T("시스템 커밋 한도 (MB)"), Loc.T("메모"));

    public static string Row(MemorySample s, int processId) => CsvWriter.Line(
        s.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        processId.ToString(CultureInfo.InvariantCulture),
        Mb(s.PrivateBytes),
        Kb(s.PagedPool),
        Kb(s.NonPagedPool),
        Mb(s.VirtualSize),
        Count(s.Handles),
        Count(s.Gdi),
        Count(s.User),
        Count(s.Threads),
        Mb(s.SystemCommit),
        Mb(s.SystemCommitLimit),
        string.Empty);

    /// <summary>측정값 칸 수 (커밋 ~ 시스템 커밋 한도).</summary>
    private const int ValueColumns = 10;

    public static string EventRow(WatchEvent e, int processId) => CsvWriter.Line(
    [
        e.Time.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture),
        processId > 0 ? processId.ToString(CultureInfo.InvariantCulture) : string.Empty,
        .. Enumerable.Repeat(string.Empty, ValueColumns),
        e.Text,
    ]);

    /// <summary>파일 이름에 쓸 수 없는 글자를 바꿉니다.</summary>
    public static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "process" : cleaned;
    }

    private static string Mb(long bytes) => bytes < 0 ? string.Empty : (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture);

    private static string Kb(long bytes) => bytes < 0 ? string.Empty : (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture);

    private static string Count(int value) => value < 0 ? string.Empty : value.ToString(CultureInfo.InvariantCulture);
}

/// <summary>감시하는 동안 한 줄씩 바로 써 두는 CSV (SunTool 이 갑자기 꺼져도 그때까지 남음).</summary>
public sealed class CsvRecorder : IDisposable
{
    private readonly StreamWriter _writer;

    public CsvRecorder(string path)
    {
        Path = path;
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        _writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read), new UTF8Encoding(true)) { AutoFlush = true };
        _writer.WriteLine(MemoryCsv.Header);
    }

    public string Path { get; }

    public void Write(string line) => _writer.WriteLine(line);

    public void Dispose() => _writer.Dispose();
}
