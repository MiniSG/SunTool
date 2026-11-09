using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.Core.Logging;
using SunTool.Modules.EventLogViewer.Logic;
using SunTool.Wpf.Services;

namespace SunTool.Modules.EventLogViewer.ViewModels;

/// <summary>'이벤트 ID 검색' 탭: Windows 에 등록된 이벤트 정의를 ID/원본으로 찾습니다 (오프라인).</summary>
public sealed partial class EventIdLookupViewModel : ObservableObject
{
    private readonly EventDefinitionCatalog _catalog;
    private readonly IAppLogger _log;
    private readonly IClipboardService _clipboard;

    public EventIdLookupViewModel(EventDefinitionCatalog catalog, ILogService logs, IClipboardService clipboard)
    {
        _catalog = catalog;
        _log = logs.For("EventIdLookup");
        _clipboard = clipboard;
    }

    [ObservableProperty]
    private string _eventIdText = string.Empty;

    [ObservableProperty]
    private string _providerText = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<EventDefinition> _results = Array.Empty<EventDefinition>();

    [ObservableProperty]
    private EventDefinition? _selected;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _hasSearched;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _statusText = "이벤트 ID를 입력하고 [검색]을 누르세요. 처음 한 번은 Windows의 이벤트 정의 목록을 만드느라 몇 초 걸립니다.";

    [ObservableProperty]
    private string _catalogInfo = "정의 목록: 아직 만들지 않음";

    partial void OnIsBusyChanged(bool value)
    {
        SearchCommand.NotifyCanExecuteChanged();
        RebuildCommand.NotifyCanExecuteChanged();
    }

    partial void OnResultsChanged(IReadOnlyList<EventDefinition> value) => Selected = value.FirstOrDefault();

    /// <summary>로그 조회 탭에서 선택한 이벤트의 정의를 찾습니다.</summary>
    public Task LookupAsync(string providerName, int eventId)
    {
        ProviderText = providerName;
        EventIdText = eventId.ToString();
        return SearchAsync();
    }

    private bool CanSearch() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync()
    {
        int? eventId = null;
        var idText = EventIdText.Trim();
        if (idText.Length > 0)
        {
            if (!int.TryParse(idText, out var parsed) || parsed < 0)
            {
                StatusText = "이벤트 ID는 0 이상의 숫자로 입력하세요.";
                return;
            }

            eventId = parsed;
        }
        else if (string.IsNullOrWhiteSpace(ProviderText))
        {
            StatusText = "이벤트 ID를 입력하세요. (원본만 입력하면 그 원본의 모든 이벤트를 보여 줍니다)";
            return;
        }

        if (!await EnsureCatalogAsync(forceRebuild: false))
        {
            return;
        }

        Results = _catalog.Find(eventId, ProviderText);
        HasSearched = true;

        var providers = Results.Select(r => r.ProviderName).Distinct(StringComparer.OrdinalIgnoreCase).Count();
        var meanings = Results.Where(r => r.Description.Length > 0).Select(r => r.Summary).Distinct().Count();
        StatusText = Results.Count switch
        {
            0 when eventId is not null => $"ID {eventId} 의 정의를 찾지 못했습니다. 원본 이름을 비우고 다시 찾아보거나 [웹에서 찾기]를 이용하세요.",
            0 => "해당 원본의 이벤트 정의를 찾지 못했습니다.",
            _ when eventId is not null && providers > 1 =>
                $"{Results.Count:N0}건 — ID {eventId} 를 쓰는 원본 {providers}개, 서로 다른 설명 {meanings}가지. 원본에 따라 뜻이 다르니 원본을 확인하세요.",
            _ => $"{Results.Count:N0}건을 찾았습니다.",
        };
        _log.Debug($"이벤트 ID 검색: id={eventId?.ToString() ?? "-"}, 원본='{ProviderText}' → {Results.Count}건");
    }

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task RebuildAsync()
    {
        if (await EnsureCatalogAsync(forceRebuild: true) && (EventIdText.Length > 0 || ProviderText.Length > 0))
        {
            await SearchAsync();
        }
    }

    [RelayCommand]
    private void CopySelected()
    {
        if (Selected is not null && _clipboard.SetText(Selected.Details))
        {
            StatusText = "선택한 정의를 클립보드에 복사했습니다.";
        }
    }

    /// <summary>원인·해결 방법은 Windows 에 없으므로 Microsoft Learn 검색을 브라우저로 엽니다.</summary>
    [RelayCommand]
    private void SearchWeb()
    {
        var provider = Selected?.ProviderName ?? ProviderText.Trim();
        var id = Selected?.EventId.ToString() ?? EventIdText.Trim();
        var terms = $"event id {id} {provider}".Trim();
        var url = "https://learn.microsoft.com/ko-kr/search/?terms=" + Uri.EscapeDataString(terms);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Error("브라우저를 열지 못했습니다.", ex);
        }
    }

    private async Task<bool> EnsureCatalogAsync(bool forceRebuild)
    {
        if (_catalog.IsLoaded && !forceRebuild)
        {
            return true;
        }

        IsBusy = true;
        ProgressValue = 0;
        StatusText = "Windows 이벤트 정의 목록을 준비하는 중...";
        try
        {
            var progress = new Progress<CatalogProgress>(p =>
            {
                ProgressValue = p.Total == 0 ? 0 : p.Done * 100.0 / p.Total;
                StatusText = $"Windows 이벤트 정의를 읽는 중... {p.Done:N0} / {p.Total:N0} 원본 (처음 한 번만)";
            });
            await _catalog.EnsureLoadedAsync(forceRebuild, progress, CancellationToken.None);
            CatalogInfo = $"정의 목록: 원본 {_catalog.ProviderCount:N0}개 · 정의 {_catalog.DefinitionCount:N0}개 · " +
                          $"{(_catalog.LoadedFromCache ? "저장본" : "새로 만듦")} {_catalog.BuiltAt:yyyy-MM-dd HH:mm}";
            return true;
        }
        catch (Exception ex)
        {
            StatusText = "정의 목록을 만들지 못했습니다: " + ex.Message;
            _log.Error("이벤트 정의 목록 생성 실패", ex);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
