using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.Core.Logging;
using SunTool.Core.Settings;
using SunTool.Core.Text;
using SunTool.Modules.NetScan.Logic;
using SunTool.Wpf.Services;

namespace SunTool.Modules.NetScan.ViewModels;

/// <summary>설정 파일의 "NetScan" 항목.</summary>
public sealed class NetScanSettings
{
    public string? LastRange { get; set; }

    public int MaxConcurrency { get; set; } = 64;

    public string? Exclude { get; set; }

    public bool ResolveNames { get; set; } = true;
}

/// <summary>
/// 장치 검색: 핑으로 같은 네트워크의 살아 있는 장치를 찾습니다. 포트 검사·서비스 탐지는 하지 않는,
/// ping · arp -a 수준의 LAN 목록 작성 도구입니다.
/// </summary>
public sealed partial class NetScanViewModel : ObservableObject, IDisposable
{
    private const string SettingsKey = "NetScan";

    private readonly IAppLogger _log;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly Dispatcher _dispatcher;
    private CancellationTokenSource? _cts;
    private int _total;
    private int _responderCount;
    private bool _loading;

    public NetScanViewModel(ILogService logs, IDialogService dialogs, ISettingsService settings)
    {
        _log = logs.For("NetScan");
        _dialogs = dialogs;
        _settings = settings;
        _dispatcher = Dispatcher.CurrentDispatcher;

        ResultsView = CollectionViewSource.GetDefaultView(Results);
        ResultsView.SortDescriptions.Add(new SortDescription(nameof(ScanRow.IpValue), ListSortDirection.Ascending));
        // 검색 중 들어오는 행도 IP 순서대로 꽂히도록 실시간 정렬을 켭니다.
        if (ResultsView is ICollectionViewLiveShaping live)
        {
            live.LiveSortingProperties.Add(nameof(ScanRow.IpValue));
            live.IsLiveSorting = true;
        }

        LoadSettings();
    }

    // ===================================================================== 입력·목록

    public ObservableCollection<ScanRow> Results { get; } = [];

    public ICollectionView ResultsView { get; }

    public IReadOnlyList<int> ConcurrencyChoices { get; } = [16, 32, 64, 128, 256];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand))]
    private string _rangeText = "192.168.0.0/24";

    [ObservableProperty]
    private int _maxConcurrency = 64;

    [ObservableProperty]
    private string _excludeText = string.Empty;

    [ObservableProperty]
    private bool _resolveNames = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditSetup))]
    [NotifyCanExecuteChangedFor(nameof(StartScanCommand), nameof(StopScanCommand), nameof(SaveCsvCommand), nameof(ClearCommand))]
    private bool _isScanning;

    [ObservableProperty]
    private int _foundCount;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private string _progressText = string.Empty;

    [ObservableProperty]
    private string _statusText = Loc.T("범위를 입력하고 [검색 시작]을 누르세요. 권한이 있는 네트워크만 검색하세요.");

    [ObservableProperty]
    private bool _statusIsError;

    public bool CanEditSetup => !IsScanning;

    // ===================================================================== 명령

    private bool CanStart() => !IsScanning && !string.IsNullOrWhiteSpace(RangeText);

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartScanAsync()
    {
        if (!IpRange.TryParse(RangeText, out var range, out var error) || range is null)
        {
            SetStatus(error ?? Loc.T("IP 범위를 이해할 수 없습니다."), isError: true);
            return;
        }

        var exclude = ExcludeList.Parse(ExcludeText);
        Results.Clear();
        FoundCount = 0;
        _responderCount = 0;
        _total = (int)range.Count;
        ProgressValue = 0;
        ProgressText = Loc.F("0 / {0:N0}", _total);
        IsScanning = true;
        SaveSettings();
        SetStatus(Loc.F("검색 중… 주소 {0:N0}개, 동시 {1}개.", _total, MaxConcurrency));
        _log.Info($"장치 검색 시작: {RangeText} ({_total}개 주소), 동시 {MaxConcurrency}");

        _cts = new CancellationTokenSource();
        var token = _cts.Token;
        var scanner = new PingScanner { ResolveNames = ResolveNames };

        try
        {
            var arp = await PingScanner.ReadArpCacheAsync(token).ConfigureAwait(true);
            await Task.Run(() => scanner.ScanAsync(
                range,
                exclude,
                MaxConcurrency,
                arp,
                OnFound,
                OnProgress,
                token), token).ConfigureAwait(true);

            var found = Volatile.Read(ref _responderCount);
            FoundCount = found; // 대기 중인 UI 반영을 기다리지 않고 최종 수치로 맞춤
            SetStatus(Loc.F("검색을 마쳤습니다. 장치 {0:N0}대를 찾았습니다.", found));
            _log.Info($"장치 검색 완료: {found}대");
        }
        catch (OperationCanceledException)
        {
            var found = Volatile.Read(ref _responderCount);
            FoundCount = found;
            SetStatus(Loc.F("검색을 멈췄습니다. 지금까지 장치 {0:N0}대를 찾았습니다.", found));
            _log.Info($"장치 검색 중지: {found}대");
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("검색 중 오류가 났습니다.\n\n{0}", ex.Message), isError: true);
            _log.Error("장치 검색 실패", ex);
        }
        finally
        {
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    private bool CanStop() => IsScanning;

    [RelayCommand(CanExecute = nameof(CanStop))]
    private void StopScan() => _cts?.Cancel();

    private bool CanUseResults() => !IsScanning && Results.Count > 0;

    [RelayCommand(CanExecute = nameof(CanUseResults))]
    private void SaveCsv()
    {
        var path = _dialogs.SaveFile(
            Loc.T("검색 결과 CSV 저장"),
            Loc.T("CSV 파일 (*.csv)|*.csv"),
            $"netscan-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        if (path is null)
        {
            return;
        }

        try
        {
            CsvWriter.Write(path, NetScanCsv.Rows(Results.OrderBy(r => r.IpValue)));
            SetStatus(Loc.F("CSV 로 저장했습니다: {0}", path));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("장치 검색 CSV 저장 실패", ex);
            _dialogs.ShowError(Loc.F("저장하지 못했습니다.\n\n{0}", ex.Message));
        }
    }

    private bool CanClear() => !IsScanning && Results.Count > 0;

    [RelayCommand(CanExecute = nameof(CanClear))]
    private void Clear()
    {
        Results.Clear();
        FoundCount = 0;
        ProgressValue = 0;
        ProgressText = string.Empty;
        RaiseResultState();
        SetStatus(Loc.T("결과를 지웠습니다."));
    }

    // ===================================================================== 콜백 (백그라운드 스레드)

    private void OnFound(ScanRow row)
    {
        // 백그라운드 스레드에서 동기적으로 센 수 (완료 메시지가 정확하도록). UI 반영은 비동기.
        Interlocked.Increment(ref _responderCount);
        _dispatcher.BeginInvoke(() =>
        {
            Results.Add(row);
            FoundCount = Results.Count;
            RaiseResultState();
        }, DispatcherPriority.Background);
    }

    private void OnProgress(int done) =>
        _dispatcher.BeginInvoke(() =>
        {
            ProgressValue = _total > 0 ? done * 100.0 / _total : 0;
            ProgressText = Loc.F("{0:N0} / {1:N0}", done, _total);
        }, DispatcherPriority.Background);

    private void RaiseResultState()
    {
        SaveCsvCommand.NotifyCanExecuteChanged();
        ClearCommand.NotifyCanExecuteChanged();
    }

    // ===================================================================== 설정

    partial void OnMaxConcurrencyChanged(int value) => SaveSettings();

    partial void OnResolveNamesChanged(bool value) => SaveSettings();

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var s = _settings.Get(SettingsKey, new NetScanSettings());
            if (!string.IsNullOrWhiteSpace(s.LastRange))
            {
                RangeText = s.LastRange;
            }

            MaxConcurrency = ConcurrencyChoices.Contains(s.MaxConcurrency) ? s.MaxConcurrency : 64;
            ExcludeText = s.Exclude ?? string.Empty;
            ResolveNames = s.ResolveNames;
        }
        finally
        {
            _loading = false;
        }
    }

    private void SaveSettings()
    {
        if (_loading)
        {
            return;
        }

        _settings.Set(SettingsKey, new NetScanSettings
        {
            LastRange = RangeText,
            MaxConcurrency = MaxConcurrency,
            Exclude = ExcludeText,
            ResolveNames = ResolveNames,
        });
        _settings.Save();
    }

    private void SetStatus(string text, bool isError = false)
    {
        StatusText = text;
        StatusIsError = isError;
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
