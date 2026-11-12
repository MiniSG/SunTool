using System.Text.RegularExpressions;
using SunTool.Core;
using SunTool.Core.Text;
using SunTool.Modules.SystemInfo.Logic;
using SunTool.Modules.SystemInfo.Logic.Providers;

namespace SunTool.Tests;

public class SystemInfoTests
{
    private static InstalledProgram Program(string name, string version = "1.0") => new(name, version, "Vendor", @"C:\Program Files\X", "20260101");

    [Fact]
    public void DefaultDefinitions_LoadWithValidPatterns_AndBaslerDalsaHalconInSummary()
    {
        var definitions = SoftwareCatalog.Parse(SoftwareCatalog.ReadDefaultJson());

        Assert.NotEmpty(definitions);
        Assert.All(definitions, d =>
        {
            Assert.Contains(d.Category, new[] { "camera", "vision", "devtools" });
            foreach (var pattern in d.ProgramPatterns.Concat(d.ExcludePatterns))
            {
                _ = new Regex(pattern); // 잘못된 정규식이면 예외
            }
        });

        var summary = definitions.Where(d => d.Summary).Select(d => d.Name).ToList();
        Assert.Contains("Basler pylon", summary);
        Assert.Contains("Teledyne DALSA Sapera LT", summary);
        Assert.Contains("MVTec HALCON", summary);

        // 카메라 분류는 Basler·DALSA 가 맨 앞
        var catalog = SoftwareCatalog.Load(userFilePath: null);
        var firstTwo = catalog.ForCategory("camera").Take(2).Select(d => d.Vendor).ToList();
        Assert.Equal(new[] { "Basler", "Teledyne DALSA" }, firstTwo);
    }

    [Fact]
    public void UserDefinitionFile_OverridesAndAdds()
    {
        using var temp = new TempFolder();
        var path = Path.Combine(temp.Path, SoftwareCatalog.UserFileName);
        File.WriteAllText(path, """
            {
              // 주석 허용
              "software": [
                { "category": "vision", "name": "MVTec HALCON", "programPatterns": [ "^MyHalcon" ] },
                { "category": "vision", "name": "사내 라이브러리", "programPatterns": [ "^Inhouse" ] },
              ]
            }
            """);

        var catalog = SoftwareCatalog.Load(path);

        Assert.Equal(path, catalog.UserFilePath);
        var halcon = Assert.Single(catalog.Definitions, d => d.Name == "MVTec HALCON");
        Assert.Equal(new[] { "^MyHalcon" }, halcon.ProgramPatterns);
        Assert.Contains(catalog.Definitions, d => d.Name == "사내 라이브러리");
    }

    [Fact]
    public void Detector_FindsEachInstalledHalconVersion_AndIgnoresDocumentation()
    {
        var definition = SoftwareCatalog.Load(null).Definitions.Single(d => d.Name == "MVTec HALCON");
        var programs = new[]
        {
            Program("MVTec HALCON 23.11 Progress", "23.11.0.0"),
            Program("MVTec HALCON 20.11 Steady", "20.11.3.0"),
            Program("MVTec HALCON 23.11 Documentation", "23.11.0.0"),
            Program("Something Else"),
        };

        var detection = SoftwareDetector.Detect(definition, programs, getEnv: _ => null, registryExists: _ => false);
        var items = SoftwareDetector.ToItems(detection);

        Assert.True(detection.IsInstalled);
        Assert.Equal(2, items.Count);
        Assert.All(items, i => Assert.Equal(InfoStatus.Installed, i.Status));
        Assert.Contains(items, i => i.Value == "23.11.0.0");
        Assert.Contains(items, i => i.Value == "20.11.3.0");
    }

    [Fact]
    public void Detector_UsesEnvAndFileVersion_WhenNotInProgramList()
    {
        using var temp = new TempFolder();
        var dll = Path.Combine(temp.Dir("bin", "x64-win64"), "halcon.dll");
        File.Copy(typeof(object).Assembly.Location, dll); // 버전 정보가 있는 아무 DLL

        var definition = SoftwareCatalog.Load(null).Definitions.Single(d => d.Name == "MVTec HALCON");
        var env = new Dictionary<string, string> { ["HALCONROOT"] = temp.Path };
        var detection = SoftwareDetector.Detect(definition, Array.Empty<InstalledProgram>(), n => env.GetValueOrDefault(n), _ => false);
        var item = Assert.Single(SoftwareDetector.ToItems(detection));

        Assert.Equal(InfoStatus.Installed, item.Status);
        Assert.False(string.IsNullOrWhiteSpace(detection.Version));
        Assert.Contains("HALCONROOT=", item.Detail);
    }

    [Fact]
    public void Detector_ReportsNotInstalled()
    {
        var definition = SoftwareCatalog.Load(null).Definitions.Single(d => d.Name == "Basler pylon");
        var item = Assert.Single(SoftwareDetector.ToItems(
            SoftwareDetector.Detect(definition, [Program("Notepad++")], _ => null, _ => false)));

        Assert.Equal(InfoStatus.NotInstalled, item.Status);
        Assert.Equal("설치되지 않음", item.Value);
    }

    [Theory]
    [InlineData(@"ACPI\PNP0501\1", SerialPortKind.Builtin)]
    [InlineData(@"USB\VID_0403&PID_6001\A12345", SerialPortKind.Usb)]
    [InlineData(@"FTDIBUS\VID_0403+PID_6001+A1\0000", SerialPortKind.Usb)]
    [InlineData(@"PCI\VEN_1415&DEV_C158\4&1", SerialPortKind.PciCard)]
    [InlineData(@"MF\PCI9710_COM1\5&2", SerialPortKind.PciCard)]
    [InlineData(@"ROOT\PORTS\0000", SerialPortKind.Virtual)]
    public void SerialPort_ClassifiedByDeviceId(string deviceId, SerialPortKind expected) =>
        Assert.Equal(expected, CommunicationInfoProvider.ClassifyPort(deviceId));

    [Theory]
    [InlineData("Default string", true)]
    [InlineData("To be filled by O.E.M.", true)]
    [InlineData("", true)]
    [InlineData("ARB19D074B18A0933", false)]
    public void ValueText_DetectsPlaceholderSerials(string value, bool placeholder) =>
        Assert.Equal(placeholder, ValueText.IsPlaceholder(value));

    [Fact]
    public void Camera_DeviceFilter_MatchesVisionVendors()
    {
        Assert.True(CameraInfoProvider.IsCameraDevice(new PnpDevice("Xtium-CL MX4", "Teledyne DALSA", "Imaging", @"PCI\VEN_11EC", "OK", 0)));
        Assert.True(CameraInfoProvider.IsCameraDevice(new PnpDevice("Basler GenTL Producer", "", "System", @"ROOT\X", "OK", 0)));
        Assert.True(CameraInfoProvider.IsCameraDevice(new PnpDevice("USB Camera", "", "Camera", @"USB\VID", "OK", 0)));
        Assert.False(CameraInfoProvider.IsCameraDevice(new PnpDevice("AMD High Definition Audio Device", "AMD", "MEDIA", @"HDAUDIO\X", "OK", 0)));
    }

    [Fact]
    public void VsWhere_JsonParsed()
    {
        const string json = """
            [ { "displayName": "Visual Studio Professional 2022", "installationPath": "C:\\VS",
                "installationVersion": "17.3.32901.215", "isComplete": true,
                "catalog": { "productDisplayVersion": "17.3.4" } } ]
            """;
        var instance = Assert.Single(DevToolsInfoProvider.ParseVsWhere(json));
        Assert.Equal("Visual Studio Professional 2022", instance.Name);
        Assert.Equal("17.3.4", instance.Version);
        Assert.True(instance.Complete);
    }

    [Theory]
    [InlineData(533320, "4.8.1")]
    [InlineData(528372, "4.8")]
    [InlineData(461808, "4.7.2")]
    [InlineData(1000, null)]
    public void NetFramework_ReleaseMapped(int release, string? expected) =>
        Assert.Equal(expected, DevToolsInfoProvider.NetFrameworkFromRelease(release));

    [Fact]
    public void Report_CsvHasHeaderMetadataAndRows()
    {
        var sections = new[] { ("컴퓨터", new InfoSection("CPU", [new InfoItem("이름", "Ryzen, 8코어", InfoStatus.Ok, "비고")])) };
        var rows = SystemInfoReport.ToRows(sections, new DateTime(2026, 11, 9, 10, 0, 0));

        Assert.Equal(SystemInfoReport.Header, rows[0]);
        Assert.Contains(rows, r => r[2] == "SunTool 버전" && r[3] == AppVersion.Text);
        var last = rows[^1];
        Assert.Equal(new[] { "컴퓨터", "CPU", "이름", "Ryzen, 8코어", "정상", "비고" }, last);
        Assert.Equal("컴퓨터,CPU,이름,\"Ryzen, 8코어\",정상,비고", CsvWriter.Line(last));
    }

    [Fact]
    public void Providers_CollectOnThisPc_WithoutErrors()
    {
        // 실제 이 PC 에서 모든 분류를 수집해 오류 항목이 없는지 확인 (WMI·레지스트리·vswhere)
        var context = new CollectionContext(SoftwareCatalog.Load(null));
        ISystemInfoProvider[] providers =
        [
            new ComputerInfoProvider(), new CommunicationInfoProvider(), new CameraInfoProvider(),
            new VisionLibraryInfoProvider(), new DevToolsInfoProvider(),
        ];

        foreach (var provider in providers)
        {
            var result = provider.Collect(context, CancellationToken.None);
            Assert.NotEmpty(result.Sections);
            Assert.DoesNotContain(result.Sections.SelectMany(s => s.Items), i => i.Status == InfoStatus.Error && i.Name == "읽기 실패");
        }

        var cpu = new ComputerInfoProvider().Collect(context, CancellationToken.None).Sections.Single(s => s.Title == "CPU");
        Assert.False(string.IsNullOrWhiteSpace(cpu.Items[0].Value));
    }

    [Fact]
    public void NetworkSections_CarryAdapterName_ForPropertiesButton()
    {
        // 네트워크 카드에만 [어댑터 속성 열기] 버튼용 연결 이름이 붙고, COM 포트 등 다른 카드에는 없어야 함
        var result = new CommunicationInfoProvider().Collect(new CollectionContext(SoftwareCatalog.Load(null)), CancellationToken.None);
        var network = result.Sections.Where(s => s.Title.StartsWith("네트워크 · ", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(network);
        Assert.All(network, s => Assert.Equal(s.Title["네트워크 · ".Length..], s.AdapterName));
        Assert.All(result.Sections.Except(network), s => Assert.False(s.HasAdapter));
    }

    // ===== 사용자 감지 목록 파일 오류 표시

    private const string TwoListsNoComma = """
        {
          // 주석
          "software": [
            { "category": "vision", "name": "예시", "programPatterns": [ "^MyCompany Vision" ] }
          ]
          "software": [
            { "category": "devtools", "name": "Notepad++", "programPatterns": [ "^Notepad++" ], }
          ]
        }
        """;

    [Fact]
    public void UserFile_MissingComma_ReportsLineAndHint()
    {
        var problems = new List<string>();
        Assert.Null(SoftwareCatalog.ParseUser(TwoListsNoComma, problems));
        var problem = Assert.Single(problems);
        Assert.Contains("6번째 줄", problem);   // 두 번째 "software" 가 있는 줄
        Assert.Contains("쉼표", problem);
    }

    [Fact]
    public void UserFile_TwoLists_ReadsBoth_AndWarns_BadRegexMarksOnlyThatItem()
    {
        var json = TwoListsNoComma.Replace("  ]\n  \"software\"", "  ],\n  \"software\"").Replace("  ]\r\n  \"software\"", "  ],\r\n  \"software\"");
        var problems = new List<string>();
        var definitions = SoftwareCatalog.ParseUser(json, problems)!;

        Assert.Equal(2, definitions.Count);
        Assert.Contains(problems, p => p.Contains("\"software\" 목록이 2개"));
        Assert.Contains(problems, p => p.StartsWith("Notepad++:") && p.Contains("^Notepad++") && p.Contains("Nested quantifier"));
        Assert.Null(definitions[0].Error);

        // 잘못된 정규식 항목은 예외 없이 '확인 필요' 한 줄로
        var item = Assert.Single(SoftwareDetector.ToItems(SoftwareDetector.Detect(definitions[1], [])));
        Assert.Equal(InfoStatus.Warning, item.Status);
        Assert.Equal("Notepad++", item.Name);
    }

    [Fact]
    public void UserFile_EscapedRegex_IsFine_AndMatchesNotepadPlusPlus()
    {
        const string json = """
            { "software": [ { "category": "devtools", "name": "Notepad++", "programPatterns": [ "^Notepad\\+\\+" ] } ] }
            """;
        var problems = new List<string>();
        var definition = Assert.Single(SoftwareCatalog.ParseUser(json, problems)!);
        Assert.Empty(problems);
        var program = new InstalledProgram("Notepad++ (64-bit x64)", "8.6.9", "Notepad++ Team", string.Empty, string.Empty);
        var item = Assert.Single(SoftwareDetector.ToItems(SoftwareDetector.Detect(definition, [program], _ => null, _ => false)));
        Assert.Equal(InfoStatus.Installed, item.Status);
        Assert.Equal("8.6.9", item.Value);
    }

    [Fact]
    public void UserFile_BadItems_AreSkipped_OthersApplied()
    {
        const string json = """
            { "software": [
                { "category": "tools", "name": "잘못된 분류" },
                { "name": "분류 없음" },
                { "category": "vision", "name": "정상" }
            ] }
            """;
        var problems = new List<string>();
        var definition = Assert.Single(SoftwareCatalog.ParseUser(json, problems)!);
        Assert.Equal("정상", definition.Name);
        Assert.Equal(2, problems.Count);
    }

    [Fact]
    public void Load_BrokenUserFile_KeepsDefaults_AndExposesProblems()
    {
        var path = Path.Combine(Path.GetTempPath(), $"suntool-sw-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, TwoListsNoComma);
        try
        {
            var catalog = SoftwareCatalog.Load(path);
            Assert.Null(catalog.UserFilePath);
            Assert.Single(catalog.UserFileProblems);
            Assert.NotEmpty(catalog.ForCategory("vision"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UserFile_ComputerAndCommunication_ShowAsAdditionalCards()
    {
        // 환경 변수 WINDIR 은 모든 Windows 에 있으므로 '설치됨'으로 감지되는 항목
        const string json = """
            { "software": [
                { "category": "computer", "name": "사내 백신", "envVars": [ "WINDIR" ] },
                { "category": "communication", "name": "USB-시리얼 드라이버", "envVars": [ "WINDIR" ], "summary": true }
            ] }
            """;
        var path = Path.Combine(Path.GetTempPath(), $"suntool-sw-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        try
        {
            var catalog = SoftwareCatalog.Load(path);
            Assert.Empty(catalog.UserFileProblems);
            var context = new CollectionContext(catalog);

            var computer = new ComputerInfoProvider().Collect(context, CancellationToken.None);
            var card = Assert.Single(computer.Sections, s => s.Title == "추가 감지 항목");
            var item = Assert.Single(card.Items);
            Assert.Equal("사내 백신", item.Name);
            Assert.Equal(InfoStatus.Warning, item.Status);   // 설치 흔적(환경 변수)만 있고 버전은 모름

            var comm = new CommunicationInfoProvider().Collect(context, CancellationToken.None);
            Assert.Equal("USB-시리얼 드라이버", Assert.Single(Assert.Single(comm.Sections, s => s.Title == "추가 감지 항목").Items).Name);
            Assert.Contains(comm.Summary, i => i.Name == "USB-시리얼 드라이버");
        }
        finally
        {
            File.Delete(path);
        }

        // 사용자 항목이 없으면 카드도 없음
        var plain = new ComputerInfoProvider().Collect(new CollectionContext(SoftwareCatalog.Load(null)), CancellationToken.None);
        Assert.DoesNotContain(plain.Sections, s => s.Title == "추가 감지 항목");
    }

    [Fact]
    public void UserFileTemplate_IsValid_AndMentionsAllCategories()
    {
        // [감지 목록 편집]으로 처음 만드는 파일 (주석 포함) 이 문제 없이 읽혀야 함
        var template = (string)typeof(SunTool.Modules.SystemInfo.ViewModels.SystemInfoViewModel)
            .GetField("UserFileTemplate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!
            .GetValue(null)!;
        var problems = new List<string>();
        var definition = Assert.Single(SoftwareCatalog.ParseUser(template, problems)!);
        Assert.Empty(problems);
        Assert.Equal("vision", definition.Category);
        Assert.All(SoftwareCatalog.KnownCategories, c => Assert.Contains(c, template));
    }

    [Fact]
    public void UserFile_UnknownCategory_ListsAllFiveScreens()
    {
        var problems = new List<string>();
        SoftwareCatalog.ParseUser("""{ "software": [ { "category": "tools", "name": "X" } ] }""", problems);
        var problem = Assert.Single(problems);
        Assert.Contains("computer(컴퓨터)", problem);
        Assert.Contains("communication(통신)", problem);
        Assert.Contains("devtools(개발 도구)", problem);
    }

    [Fact]
    public void AppVersion_IsDateBased()
    {
        // 테스트 실행 파일이 아니라 같은 빌드 설정을 쓰는 어셈블리의 버전 형식 확인 (yyyy.M.d)
        var version = typeof(AppVersion).Assembly.GetName().Version!;
        Assert.InRange(version.Major, 2026, 2100);
        Assert.InRange(version.Minor, 1, 12);
        Assert.InRange(version.Build, 1, 31);
    }
}
