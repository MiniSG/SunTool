using SunTool.Modules.SystemInfo.Logic;

namespace SunTool.Tests;

/// <summary>다른 PC 에서 보고된 감지 문제 재현: pylon 버전 확인 불가, 압축만 푼 OpenCV 미감지.</summary>
public class SoftwareDetectionTests
{
    private static SoftwareDefinition Def(string name) => SoftwareCatalog.Load(null).Definitions.Single(d => d.Name == name);

    private static Func<string, string?> Env(Dictionary<string, string> values) => name => values.GetValueOrDefault(name);

    [Fact]
    public void Pylon_NewInstallerName_AsHiddenEntry_IsDetectedWithVersion()
    {
        // pylon 7 은 "pylon 7.4.0.14900 Camera Software Suite" 같은 이름을 숨김 항목으로 등록하는 경우가 있음
        var programs = new[]
        {
            new InstalledProgram("pylon 7.4.0.14900 Camera Software Suite", "7.4.0.14900", "Basler AG", "", "", Hidden: true),
            new InstalledProgram("Notepad++", "8.0", "", "", ""),
        };

        var item = Assert.Single(SoftwareDetector.ToItems(SoftwareDetector.Detect(Def("Basler pylon"), programs, _ => null, _ => false)));

        Assert.Equal(InfoStatus.Installed, item.Status);
        Assert.Equal("7.4.0.14900", item.Value);
    }

    [Fact]
    public void Pylon_OnlyEnvVar_GetsVersionFromRuntimeDll()
    {
        // 설치 목록에서 못 찾고 PYLON_DEV_DIR 만 있는 경우: Runtime\x64\PylonBase_v7_4.dll 로 버전 확인
        using var temp = new TempFolder();
        var pylonRoot = temp.Dir("Basler", "pylon 7");
        var development = temp.Dir("Basler", "pylon 7", "Development");
        File.WriteAllText(Path.Combine(temp.Dir("Basler", "pylon 7", "Runtime", "x64"), "PylonBase_v7_4.dll"), "no version resource");

        var detection = SoftwareDetector.Detect(
            Def("Basler pylon"),
            Array.Empty<InstalledProgram>(),
            Env(new() { ["PYLON_DEV_DIR"] = development }),
            _ => true);
        var item = Assert.Single(SoftwareDetector.ToItems(detection));

        Assert.Equal(InfoStatus.Installed, item.Status);
        Assert.Equal("7.4", item.Value); // 파일 이름에서 추출
        Assert.Contains(pylonRoot, item.Detail);
    }

    [Fact]
    public void Pylon_OnlyTraces_IsWarningNotInstalled()
    {
        var item = Assert.Single(SoftwareDetector.ToItems(SoftwareDetector.Detect(
            Def("Basler pylon"), Array.Empty<InstalledProgram>(), _ => null, _ => true)));

        Assert.Equal(InfoStatus.Warning, item.Status);
        Assert.Equal("설치 흔적 있음 (버전 확인 불가)", item.Value);
    }

    [Fact]
    public void OpenCV_ExtractedFolderOnDriveRoot_IsFound()
    {
        // C:\opencv\build\x64\vc16\bin\opencv_world4100.dll 형태 (설치 프로그램·환경 변수 없음)
        using var temp = new TempFolder();
        var bin = temp.Dir("opencv", "build", "x64", "vc16", "bin");
        File.WriteAllText(Path.Combine(bin, "opencv_world4100.dll"), "x");
        File.WriteAllText(Path.Combine(bin, "opencv_world4100d.dll"), "x");

        var detection = SoftwareDetector.Detect(
            Def("OpenCV"),
            Array.Empty<InstalledProgram>(),
            Env(new() { ["SystemDrive"] = temp.Path }), // 가짜 C:\
            _ => false);
        var item = Assert.Single(SoftwareDetector.ToItems(detection));

        Assert.Equal(InfoStatus.Installed, item.Status);
        Assert.Equal("4.10.0", item.Value);
        Assert.Contains("opencv_world4100.dll", item.Detail);
    }

    [Fact]
    public void OpenCV_NestedTwoLevels_IsFound()
    {
        // C:\Lib\opencv-4.5.5\... 처럼 한 단계 아래 폴더도 찾음
        using var temp = new TempFolder();
        var bin = temp.Dir("Lib", "opencv-4.5.5", "build", "x64", "vc15", "bin");
        File.WriteAllText(Path.Combine(bin, "opencv_world455.dll"), "x");

        var detection = SoftwareDetector.Detect(Def("OpenCV"), Array.Empty<InstalledProgram>(), Env(new() { ["SystemDrive"] = temp.Path }), _ => false);

        Assert.Equal("4.5.5", Assert.Single(SoftwareDetector.ToItems(detection)).Value);
    }

    [Fact]
    public void ResolveFiles_SupportsFolderWildcardsAndParent()
    {
        using var temp = new TempFolder();
        File.WriteAllText(Path.Combine(temp.Dir("Basler", "pylon 6", "Runtime", "x64"), "PylonBase_v6_3.dll"), "x");
        File.WriteAllText(Path.Combine(temp.Dir("Basler", "pylon 7", "Runtime", "x64"), "PylonBase_v7_4.dll"), "x");
        temp.Dir("Basler", "pylon 7", "Development");

        var env = Env(new() { ["ROOT"] = temp.Path });
        var wildcard = SoftwareDetector.ResolveFiles(@"%ROOT%\Basler\pylon*\Runtime\x64\PylonBase_v*.dll", env);
        var parent = SoftwareDetector.ResolveFiles(@"%ROOT%\Basler\pylon 7\Development\..\Runtime\x64\PylonBase_v*.dll", env);

        Assert.Equal(2, wildcard.Count);
        Assert.Single(parent);
        Assert.Empty(SoftwareDetector.ResolveFiles(@"%MISSING%\x.dll", env));
    }

    [Theory]
    [InlineData("opencv_world4100.dll", "4.10.0")]
    [InlineData("opencv_world455.dll", "4.5.5")]
    [InlineData("opencv_world4100d.dll", "4.10.0")]
    [InlineData("opencv_core3410.dll", "3.41.0")]
    public void VersionFromName_OpenCv(string file, string expected) =>
        Assert.Equal(expected, SoftwareDetector.VersionFromName(file, Def("OpenCV").VersionFromFileName));

    [Fact]
    public void ProgramsCommand_ListsMatchingProgramsAndEnvironment()
    {
        var output = new FakeConsole();
        Environment.SetEnvironmentVariable("SUNTOOL_TEST_PYLON_DIR", @"C:\Program Files\Basler\pylon 7");
        try
        {
            new FindProgramsCommand().Execute(["pylon"], output);
        }
        finally
        {
            Environment.SetEnvironmentVariable("SUNTOOL_TEST_PYLON_DIR", null);
        }

        Assert.Contains(output.Lines, l => l.StartsWith("[설치 프로그램] 'pylon'"));
        Assert.Contains(output.Lines, l => l.Contains("SUNTOOL_TEST_PYLON_DIR = C:\\Program Files\\Basler\\pylon 7"));
    }

    private sealed class FakeConsole : SunTool.Core.Diagnostics.IConsoleContext
    {
        public List<string> Lines { get; } = new();

        public IReadOnlyCollection<SunTool.Core.Diagnostics.IConsoleCommand> Commands => Array.Empty<SunTool.Core.Diagnostics.IConsoleCommand>();

        public void Write(string text, SunTool.Core.Logging.LogLevel level = SunTool.Core.Logging.LogLevel.Info) => Lines.Add(text);

        public void ClearOutput() => Lines.Clear();
    }

    [Fact]
    public void VisibleEntry_PreferredOverHidden()
    {
        var programs = new[]
        {
            new InstalledProgram("Basler pylon 7.4.0", "7.4.0", "Basler AG", "", ""),
            new InstalledProgram("pylon 7.4.0 Camera Software Suite", "7.4.0.14900", "Basler AG", "", "", Hidden: true),
        };

        var item = Assert.Single(SoftwareDetector.ToItems(SoftwareDetector.Detect(Def("Basler pylon"), programs, _ => null, _ => false)));
        Assert.Equal("7.4.0", item.Value);
    }
}
