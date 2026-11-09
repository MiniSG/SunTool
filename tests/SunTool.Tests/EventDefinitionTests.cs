using SunTool.Modules.EventLogViewer.Logic;

namespace SunTool.Tests;

public class EventDefinitionTests
{
    [Fact]
    public void MessageTable_ReadsServiceControlManagerMessages()
    {
        // 서비스 제어 관리자(services.exe)의 메시지 테이블: 7000 = 서비스 시작 실패
        var table = MessageTableReader.Read(@"%SystemRoot%\system32\services.exe");

        Assert.NotEmpty(table);
        var entry = Assert.Single(table, t => (t.MessageId & 0xFFFF) == 7000 && (t.MessageId & 0x2FFF0000) == 0);
        Assert.Contains("%1", entry.Text);
        Assert.Equal(3u, entry.MessageId >> 30); // 심각도: 오류
    }

    [Fact]
    public void MessageTable_MissingFile_ReturnsEmpty() =>
        Assert.Empty(MessageTableReader.Read(@"C:\does\not\exist.dll"));

    [Theory]
    [InlineData("서비스를 시작하지 못했습니다.%n%2%0", "서비스를 시작하지 못했습니다.\n%2")]
    [InlineData("값 %1 은 100%% 입니다%.", "값 %1 은 100% 입니다.")]
    [InlineData("%10 번째 인수", "%10 번째 인수")]
    public void Clean_HandlesMessageEscapes(string raw, string expected) =>
        Assert.Equal(expected, MessageTableReader.Clean(raw));

    [Fact]
    public void Build_FindsManifestAndLegacyDefinitions()
    {
        var definitions = EventDefinitionCatalog.Build(["Service Control Manager", "Microsoft-Windows-DistributedCOM"]);

        // 예전 방식: 매니페스트에는 없고 메시지 파일에만 있는 SCM 7000
        var scm = Assert.Single(definitions, d => d.ProviderName == "Service Control Manager" && d.EventId == 7000);
        Assert.Equal(EventDefinitionSource.MessageFile, scm.Source);
        Assert.Equal(EventLevelKind.Error, scm.LevelKind);
        Assert.Contains("%1", scm.Description);

        // 최신 방식: 매니페스트에 있는 DCOM 10016
        var dcom = Assert.Single(definitions, d => d.ProviderName == "Microsoft-Windows-DistributedCOM" && d.EventId == 10016);
        Assert.Equal(EventDefinitionSource.Manifest, dcom.Source);
        Assert.Contains("CLSID", dcom.Description);
    }

    [Fact]
    public void Find_FiltersByProviderAndPutsExactMatchFirst()
    {
        var catalog = new EventDefinitionCatalog(cacheDirectory: null);
        catalog.Load(
            [
                Def("Microsoft-Windows-Kernel-Power", 41),
                Def("Kernel-Power", 41),
                Def("Service Control Manager", 41),
                Def("Service Control Manager", 7000),
            ],
            providerCount: 3,
            builtAt: DateTime.Now,
            fromCache: false);

        Assert.Equal(3, catalog.Find(41, null).Count);

        var filtered = catalog.Find(41, "kernel-power");
        Assert.Equal(2, filtered.Count);
        Assert.Equal("Kernel-Power", filtered[0].ProviderName); // 정확히 일치하는 원본이 먼저

        var providerOnly = catalog.Find(null, "Service Control");
        Assert.Equal([41, 7000], providerOnly.Select(d => d.EventId));

        Assert.Empty(catalog.Find(9999, null));
        Assert.Empty(catalog.Find(null, ""));
    }

    [Fact]
    public async Task Catalog_SavesAndReloadsCache()
    {
        using var temp = new TempFolder();

        // 처음: 이 PC 의 전체 공급자로 새로 만들고 저장 → 두 번째: 저장본에서 불러옴
        var catalog = new EventDefinitionCatalog(temp.Path);
        await catalog.EnsureLoadedAsync(forceRebuild: false, progress: null, CancellationToken.None);
        Assert.True(catalog.IsLoaded);
        Assert.False(catalog.LoadedFromCache);
        Assert.True(File.Exists(Path.Combine(temp.Path, "event-definitions.json.gz")));

        var reloaded = new EventDefinitionCatalog(temp.Path);
        await reloaded.EnsureLoadedAsync(forceRebuild: false, progress: null, CancellationToken.None);
        Assert.True(reloaded.LoadedFromCache);
        Assert.Equal(catalog.DefinitionCount, reloaded.DefinitionCount);
        Assert.NotEmpty(reloaded.Find(7000, "Service Control Manager"));
    }

    private static EventDefinition Def(string provider, int id) =>
        new() { ProviderName = provider, EventId = id, Level = 2, Description = "test" };
}
