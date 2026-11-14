using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SunTool.Tests;

/// <summary>영어 번역 사전 검사. Loc.Language 는 바꾸지 않습니다 (다른 테스트가 한국어 문구를 확인하므로).</summary>
public class LocalizationTests
{
    private static readonly Assembly[] Assemblies =
    [
        typeof(Loc).Assembly,                                                     // Core
        typeof(SunTool.Wpf.Localization.TExtension).Assembly,                     // Wpf
        typeof(SunTool.Modules.FolderReplicator.FolderReplicatorModule).Assembly,
        typeof(SunTool.Modules.BatchRename.BatchRenameModule).Assembly,
        typeof(SunTool.Modules.EventLogViewer.EventLogViewerModule).Assembly,
        typeof(SunTool.Modules.SystemInfo.SystemInfoModule).Assembly,
        typeof(SunTool.Modules.Comm.CommModule).Assembly,
        typeof(SunTool.Modules.MemoryWatch.MemoryWatchModule).Assembly,
        typeof(SunTool.Modules.NetScan.NetScanModule).Assembly,
    ];

    public static IEnumerable<object[]> AssemblyNames => Assemblies.Select(a => new object[] { a.GetName().Name! });

    [Theory]
    [MemberData(nameof(AssemblyNames))]
    public void EmbeddedDictionary_IsCompleteAndKeepsPlaceholders(string assemblyName)
    {
        var assembly = Assemblies.Single(a => a.GetName().Name == assemblyName);
        var resource = assembly.GetManifestResourceNames().Single(r => r.EndsWith(".i18n.en.json", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(resource)!;
        var map = JsonSerializer.Deserialize<Dictionary<string, string>>(stream)!;

        Assert.NotEmpty(map);
        var placeholder = new Regex(@"\{\d+[^}]*\}");
        foreach (var (korean, english) in map)
        {
            Assert.False(string.IsNullOrWhiteSpace(english), $"번역 없음: {korean}");
            Assert.Equal(
                placeholder.Matches(korean).Select(m => m.Value).Order(),
                placeholder.Matches(english).Select(m => m.Value).Order());
            if (placeholder.IsMatch(korean))
            {
                // Loc.F 로 쓰는 서식 문자열: 영어 서식도 깨지지 않았는지 ({name} 같은 이름 변경 토큰 문구는 Loc.T 로만 씀)
                _ = string.Format(english, new object[10].Select(_ => (object)DateTime.Now).ToArray());
            }
        }
    }

    [Fact]
    public void Korean_IsDefault_AndReturnsKeyUnchanged()
    {
        Assert.False(Loc.IsEnglish);
        Assert.Equal("시스템 정보", Loc.T("시스템 정보"));
        Assert.Equal("파일 3개", Loc.F("파일 {0}개", 3));
    }
}
