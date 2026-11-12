using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.Core;
using SunTool.Core.Logging;
using SunTool.Modules.SystemInfo.Logic;
using SunTool.Wpf.Services;

namespace SunTool.Modules.SystemInfo.ViewModels;

/// <summary>왼쪽 분류 목록의 한 항목.</summary>
public sealed partial class InfoCategoryViewModel(string id, string title, string glyph) : ObservableObject
{
    public string Id { get; } = id;

    public string Title { get; } = title;

    public string IconGlyph { get; } = glyph;

    [ObservableProperty]
    private IReadOnlyList<InfoSection> _sections = Array.Empty<InfoSection>();

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _stateText = string.Empty;
}

public sealed partial class SystemInfoViewModel : ObservableObject
{
    private const string SummaryId = "summary";
    private static readonly TimeSpan ProviderTimeout = TimeSpan.FromSeconds(60);

    private readonly IReadOnlyList<ISystemInfoProvider> _providers;
    private readonly IAppLogger _log;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly string _userDefinitionPath = Path.Combine(AppPaths.DataDirectory, SoftwareCatalog.UserFileName);

    public SystemInfoViewModel(IEnumerable<ISystemInfoProvider> providers, ILogService logs, IDialogService dialogs, IClipboardService clipboard)
    {
        _providers = providers.OrderBy(p => p.Order).ToList();
        _log = logs.For("SystemInfo");
        _dialogs = dialogs;
        _clipboard = clipboard;

        Categories.Add(new InfoCategoryViewModel(SummaryId, Loc.T("요약"), ""));
        foreach (var provider in _providers)
        {
            Categories.Add(new InfoCategoryViewModel(provider.Id, provider.Title, provider.IconGlyph));
        }

        SelectedCategory = Categories[0];

        // 화면을 처음 열면 바로 수집을 시작합니다.
        RefreshCommand.Execute(null);
    }

    public ObservableCollection<InfoCategoryViewModel> Categories { get; } = new();

    [ObservableProperty]
    private InfoCategoryViewModel? _selectedCategory;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = Loc.T("시스템 정보를 수집하는 중...");

    [ObservableProperty]
    private DateTime? _collectedAt;

    public string DefinitionFileHint => Loc.F("감지 목록 파일: {0}", _userDefinitionPath);

    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        ExportCsvCommand.NotifyCanExecuteChanged();
        CopyAllCommand.NotifyCanExecuteChanged();
    }

    private bool CanRefresh() => !IsBusy;

    private bool HasData() => !IsBusy && CollectedAt is not null;

    [RelayCommand(CanExecute = nameof(CanRefresh))]
    private async Task RefreshAsync()
    {
        IsBusy = true;
        StatusText = Loc.T("시스템 정보를 수집하는 중...");
        var stopwatch = Stopwatch.StartNew();
        var catalog = SoftwareCatalog.Load(_userDefinitionPath, _log);
        var context = new CollectionContext(catalog, _log);
        var summaries = new Dictionary<string, List<InfoItem>>();

        foreach (var category in Categories)
        {
            category.IsLoading = true;
            category.StateText = Loc.T("수집 중…");
        }

        var tasks = _providers.Select(async provider =>
        {
            var category = Categories.First(c => c.Id == provider.Id);
            var watch = Stopwatch.StartNew();
            try
            {
                var result = await Task.Run(() => provider.Collect(context, CancellationToken.None)).WaitAsync(ProviderTimeout);
                category.Sections = result.Sections;
                summaries[provider.Id] = result.Summary;
                category.StateText = Loc.F("{0:0.0}초", watch.Elapsed.TotalSeconds);
            }
            catch (Exception ex)
            {
                _log.Error($"'{provider.Title}' 정보 수집 실패", ex);
                category.Sections = [new InfoSection(Loc.T("수집 실패"), [new InfoItem(Loc.T("오류"), ex.Message, InfoStatus.Error)])];
                category.StateText = Loc.T("오류");
            }
            finally
            {
                category.IsLoading = false;
            }
        }).ToList();

        await Task.WhenAll(tasks);

        BuildSummary(summaries, catalog);
        CollectedAt = DateTime.Now;
        IsBusy = false;
        StatusText = Loc.F("수집 완료 {0:HH:mm:ss} ({1:0.0}초)", CollectedAt, stopwatch.Elapsed.TotalSeconds) +
                     (catalog.UserFilePath is null ? string.Empty : Loc.T(" · 사용자 감지 목록 적용")) +
                     (catalog.UserFileProblems.Count == 0 ? string.Empty : Loc.F(" · 감지 목록 파일 문제 {0}건 (요약 화면 맨 위)", catalog.UserFileProblems.Count));
        _log.Info($"시스템 정보 수집 완료 ({stopwatch.ElapsedMilliseconds}ms)");
    }

    [RelayCommand(CanExecute = nameof(HasData))]
    private void ExportCsv()
    {
        var path = _dialogs.SaveFile(Loc.T("시스템 정보 CSV로 저장"), Loc.T("CSV 파일 (*.csv)|*.csv"),
            Loc.F("시스템정보_{0}_{1:yyyyMMdd_HHmm}.csv", Environment.MachineName, DateTime.Now));
        if (path is null)
        {
            return;
        }

        try
        {
            SystemInfoReport.WriteCsv(path, AllSections(), CollectedAt ?? DateTime.Now);
            StatusText = Loc.F("저장했습니다: {0}", path);
            _log.Info(StatusText);
        }
        catch (Exception ex)
        {
            _log.Error("시스템 정보 CSV 저장 실패", ex);
            _dialogs.ShowError(Loc.F("저장하지 못했습니다.\n\n{0}", ex.Message));
        }
    }

    [RelayCommand(CanExecute = nameof(HasData))]
    private void CopyAll()
    {
        if (_clipboard.SetText(SystemInfoReport.ToText(AllSections(), CollectedAt ?? DateTime.Now)))
        {
            StatusText = Loc.T("전체 내용을 클립보드에 복사했습니다.");
        }
    }

    /// <summary>감지 목록 파일(SunTool.software.json)을 열어 새 SDK 를 추가할 수 있게 합니다. 없으면 예시로 만듭니다.</summary>
    [RelayCommand]
    private void OpenDefinitionFile()
    {
        try
        {
            if (!File.Exists(_userDefinitionPath))
            {
                File.WriteAllText(_userDefinitionPath, UserFileTemplate);
                _log.Info($"감지 목록 파일을 만들었습니다: {_userDefinitionPath}");
            }

            Process.Start(new ProcessStartInfo("notepad.exe", $"\"{_userDefinitionPath}\"") { UseShellExecute = true });
            StatusText = Loc.T("감지 목록 파일을 저장한 뒤 [새로고침]을 누르면 적용됩니다.");
        }
        catch (Exception ex)
        {
            _log.Error("감지 목록 파일을 열지 못했습니다.", ex);
            _dialogs.ShowError(Loc.F("파일을 열지 못했습니다.\n\n{0}", ex.Message));
        }
    }

    /// <summary>랜카드의 Windows 속성 창을 엽니다 (IP 직접 수정용). 찾지 못하면 네트워크 연결 창 전체를 엽니다.</summary>
    [RelayCommand]
    private void OpenAdapterProperties(string? adapterName)
    {
        if (string.IsNullOrEmpty(adapterName))
        {
            return;
        }

        try
        {
            if (NetworkAdapterLauncher.TryOpenProperties(adapterName))
            {
                _log.Info($"어댑터 속성 창 열기: {adapterName}");
                StatusText = Loc.F("'{0}' 속성 창을 열었습니다. IP 는 인터넷 프로토콜 버전 4(TCP/IPv4)에서 바꿉니다.", adapterName);
                return;
            }

            _log.Warn($"네트워크 연결에서 '{adapterName}' 을(를) 찾지 못해 네트워크 연결 창을 엽니다.");
        }
        catch (Exception ex)
        {
            // 관리자 확인(UAC)에서 [아니요]를 누른 경우 등
            _log.Warn($"어댑터 속성 창을 열지 못했습니다: {adapterName}", ex);
        }

        try
        {
            NetworkAdapterLauncher.OpenConnectionsFolder();
            StatusText = Loc.F("'{0}' 속성 창을 바로 열지 못해 네트워크 연결 창을 열었습니다. 해당 어댑터를 우클릭 → [속성]을 누르세요.", adapterName);
        }
        catch (Exception ex)
        {
            _log.Error("네트워크 연결 창을 열지 못했습니다.", ex);
            _dialogs.ShowError(Loc.F("네트워크 연결 창을 열지 못했습니다.\n\n{0}", ex.Message));
        }
    }

    /// <summary>보고서용 전체 섹션 (요약의 'PC 정보'는 보고서 머리글과 같아서 제외).</summary>
    private IEnumerable<(string Category, InfoSection Section)> AllSections() =>
        Categories.SelectMany(c => c.Sections
            .Where(s => !(c.Id == SummaryId && (s.Title == ThisPcTitle || s.Title == UserFileProblemTitle)))
            .Select(s => (c.Title, s)));

    private static string ThisPcTitle => Loc.T("이 PC");

    private static string UserFileProblemTitle => Loc.F("감지 목록 파일 확인 필요 ({0})", SoftwareCatalog.UserFileName);

    private void BuildSummary(Dictionary<string, List<InfoItem>> summaries, SoftwareCatalog catalog)
    {
        var sections = new List<InfoSection>
        {
            new(ThisPcTitle, [
                new InfoItem(Loc.T("PC 이름"), Environment.MachineName),
                new InfoItem(Loc.T("수집 시각"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")),
                new InfoItem(Loc.T("SunTool 버전"), AppVersion.Text),
            ]),
        };

        foreach (var provider in _providers)
        {
            if (summaries.TryGetValue(provider.Id, out var items) && items.Count > 0)
            {
                sections.Add(new InfoSection(provider.Title, items));
            }
        }

        // 사용자 감지 목록 파일의 문제는 맨 위에 (화면에만, 보고서·CSV 에는 넣지 않음)
        if (catalog.UserFileProblems.Count > 0)
        {
            sections.Insert(0, new InfoSection(
                UserFileProblemTitle,
                catalog.UserFileProblems.Select((p, i) => new InfoItem(Loc.F("문제 {0}", i + 1), p, InfoStatus.Warning)),
                Loc.T("[감지 목록 편집]으로 파일을 고쳐 저장한 뒤 [새로고침]을 누르세요.")));
        }

        var summary = Categories.First(c => c.Id == SummaryId);
        summary.Sections = sections;
        summary.IsLoading = false;
        summary.StateText = string.Empty;
    }

    private const string UserFileTemplate = """
        {
          // SunTool 감지 목록 (사용자 추가분)
          // 기본 목록에 없는 SDK/라이브러리를 여기에 추가하면 [새로고침] 때 함께 확인합니다.
          // 같은 category + name 이면 기본 항목을 덮어씁니다.
          //
          // category        : 어느 화면에 표시할지
          //                     computer      = 컴퓨터 ('추가 감지 항목' 카드)
          //                     communication = 통신 ('추가 감지 항목' 카드)
          //                     camera        = 카메라 · 그랩보드
          //                     vision        = 이미지 라이브러리
          //                     devtools      = 개발 도구 ('기타 개발 도구' 카드)
          // programPatterns : '프로그램 및 기능'의 이름과 비교할 정규식
          // envVars         : 설치 시 생기는 환경 변수
          // versionFiles    : 버전을 읽을 파일 (%환경변수% 사용 가능, 파일 이름에 * 가능)
          // summary         : true 이면 '요약' 화면에도 표시
          //
          // 항목을 더 넣을 때는 아래 [ ] 목록 안에 { ... } 를 쉼표로 이어서 넣습니다 ("software" 는 한 번만).
          // 정규식에서 + . ( ) [ ] 를 글자 그대로 찾으려면 \\ 를 붙입니다. 예) "^Notepad\\+\\+"
          "software": [
            {
              "category": "vision",   // → '이미지 라이브러리' 화면에 표시
              "name": "예시 - 사내 비전 라이브러리",
              "priority": 1,
              "summary": false,
              "programPatterns": [ "^MyCompany Vision" ],
              "envVars": [ "MYVISION_ROOT" ],
              "versionFiles": [ "%MYVISION_ROOT%\\bin\\myvision.dll" ]
            }
          ]
        }
        """;
}
