using System.Net;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.Core.Logging;
using SunTool.Core.Settings;
using SunTool.Modules.RemoteView.Logic;
using SunTool.Modules.RemoteView.Views;

namespace SunTool.Modules.RemoteView.ViewModels;

/// <summary>바인딩할 주소 선택지.</summary>
public sealed record BindOption(string Address, string Text)
{
    public override string ToString() => Text;
}

/// <summary>뷰어에서 볼 모니터 선택지. <see cref="Index"/> 가 -1 이면 전체, 그 외는 보낼 이미지 안의 잘라낼 영역.</summary>
public sealed record ViewMonitorOption(int Index, string Text, int X, int Y, int W, int H)
{
    public override string ToString() => Text;
}

/// <summary>보낼 해상도 선택지. <see cref="MaxWidth"/> 가 0 이면 원본(축소 안 함).</summary>
public sealed record ResolutionOption(int MaxWidth, string Text)
{
    public override string ToString() => Text;
}

/// <summary>설정 파일의 "RemoteView" 항목.</summary>
public sealed class RemoteViewSettings
{
    public int Port { get; set; } = 5900;

    public string? Bind { get; set; }

    public int Quality { get; set; } = 60;

    public int MaxWidth { get; set; } = 1920;

    public int Fps { get; set; } = 5;

    public bool AllowControl { get; set; }

    public string? HostAddress { get; set; }

    public int ViewerPort { get; set; } = 5900;
}

/// <summary>
/// 원격 화면 보기. 한 모듈에서 역할을 고릅니다.
/// 공유(호스트): 내 화면을 PIN 으로 공유. 보기(뷰어): 다른 PC 화면에 접속해 봅니다.
/// 보기 전용 — 마우스·키보드 입력은 주고받지 않습니다.
/// </summary>
public sealed partial class RemoteViewViewModel : ObservableObject, IDisposable
{
    private const string SettingsKey = "RemoteView";

    private readonly IAppLogger _log;
    private readonly ISettingsService _settings;
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _fpsTimer;
    private RemoteHost? _host;
    private RemoteViewerClient? _client;
    private int _framesThisSecond;
    private bool _loading;

    public RemoteViewViewModel(ILogService logs, ISettingsService settings)
    {
        _log = logs.For("RemoteView");
        _settings = settings;
        _dispatcher = Dispatcher.CurrentDispatcher;

        BindOptions.Add(new BindOption("0.0.0.0", Loc.T("모든 주소 (0.0.0.0)")));
        foreach (var ip in NetInfo.LocalIPv4())
        {
            BindOptions.Add(new BindOption(ip, ip));
        }

        ResolutionChoices.Add(new ResolutionOption(0, Loc.T("원본 (가장 선명)")));
        ResolutionChoices.Add(new ResolutionOption(1920, Loc.T("높음 (1920)")));
        ResolutionChoices.Add(new ResolutionOption(1600, Loc.T("보통 (1600)")));
        ResolutionChoices.Add(new ResolutionOption(1280, Loc.T("낮음 (1280)")));

        _fpsTimer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
        {
            if (IsConnected)
            {
                Fps = _framesThisSecond;
            }

            _framesThisSecond = 0;
        }, _dispatcher);
        _fpsTimer.Start();

        LoadSettings();
    }

    // ===================================================================== 공통

    public System.Collections.ObjectModel.ObservableCollection<BindOption> BindOptions { get; } = [];

    public IReadOnlyList<int> QualityChoices { get; } = [30, 45, 60, 75, 90];

    public System.Collections.ObjectModel.ObservableCollection<ResolutionOption> ResolutionChoices { get; } = [];

    [ObservableProperty]
    private ResolutionOption? _selectedResolution;

    public IReadOnlyList<int> FpsChoices { get; } = [1, 3, 5, 10, 15];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditMode), nameof(IsViewerMode))]
    private bool _isHostMode = true;

    /// <summary>뷰어 라디오 버튼용. 호스트 모드의 반대 (TwoWay 바인딩 가능).</summary>
    public bool IsViewerMode
    {
        get => !IsHostMode;
        set => IsHostMode = !value;
    }

    public bool CanEditMode => !IsHosting && !IsConnected && !IsConnecting;

    // ===================================================================== 호스트(공유)

    [ObservableProperty]
    private int _port = 5900;

    [ObservableProperty]
    private BindOption? _selectedBind;

    [ObservableProperty]
    private int _quality = 60;

    [ObservableProperty]
    private int _fpsSetting = 5;

    /// <summary>원격 제어 허용(호스트). 기본 꺼짐. 켜면 버전 2 뷰어 한 명이 내 PC를 조작할 수 있습니다.</summary>
    [ObservableProperty]
    private bool _allowControl;

    /// <summary>지금 제어 중인 뷰어 주소(없으면 null).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBeingControlled))]
    private string? _controllerAddress;

    public bool IsBeingControlled => !string.IsNullOrEmpty(ControllerAddress);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditMode), nameof(CanEditHostSetup))]
    [NotifyCanExecuteChangedFor(nameof(StartHostCommand), nameof(StopHostCommand))]
    private bool _isHosting;

    [ObservableProperty]
    private string _pin = string.Empty;

    [ObservableProperty]
    private string _myIpText = string.Empty;

    [ObservableProperty]
    private int _viewerCount;

    [ObservableProperty]
    private string _hostStatus = Loc.T("공유를 켜면 PIN 이 나옵니다. 상대에게 내 IP·포트·PIN 을 알려 주세요.");

    [ObservableProperty]
    private bool _hostStatusIsError;

    public bool CanEditHostSetup => !IsHosting;

    private bool CanStartHost() => !IsHosting;

    [RelayCommand(CanExecute = nameof(CanStartHost))]
    private void StartHost()
    {
        if (!IPAddress.TryParse(SelectedBind?.Address ?? "0.0.0.0", out var bind))
        {
            bind = IPAddress.Any;
        }

        try
        {
            Pin = Logic.Pin.Generate();
            _host = new RemoteHost(Pin, Quality, maxWidth: SelectedResolution?.MaxWidth ?? 1920, fps: FpsSetting);
            _host.ViewerCountChanged += HandleViewerCount;
            _host.ControllerChanged += HandleController;
            _host.ErrorOccurred += OnHostError;
            _host.Start(bind, Port);
            _host.SetAllowControl(AllowControl);

            IsHosting = true;
            ViewerCount = 0;
            // 특정 주소에 바인딩하면 그 주소를, 모든 주소(0.0.0.0)면 실제 LAN 대표 주소를 보여 줍니다.
            var bindAddr = SelectedBind?.Address ?? "0.0.0.0";
            var ip = bindAddr is "0.0.0.0" or "" ? NetInfo.PrimaryIPv4() : bindAddr;
            MyIpText = string.IsNullOrEmpty(ip) ? Loc.F("포트 {0}", Port) : Loc.F("{0} : {1}", ip, Port);
            SaveSettings();
            SetHostStatus(Loc.T("공유 중입니다. 상대 PC 의 [보기]에서 내 IP·포트·PIN 을 입력하면 연결됩니다."));
            _log.Info($"원격 공유 시작: bind {bind}, 포트 {Port}, 화질 {Quality}, 해상도 {SelectedResolution?.MaxWidth ?? 1920}, {FpsSetting}fps");
        }
        catch (Exception ex)
        {
            StopHostInternal();
            SetHostStatus(Loc.F("공유를 켜지 못했습니다. 포트가 쓰이는 중일 수 있습니다.\n\n{0}", ex.Message), isError: true);
            _log.Error("원격 공유 시작 실패", ex);
        }
    }

    private bool CanStopHost() => IsHosting;

    [RelayCommand(CanExecute = nameof(CanStopHost))]
    private void StopHost()
    {
        StopHostInternal();
        SetHostStatus(Loc.T("공유를 멈췄습니다."));
        _log.Info("원격 공유 중지");
    }

    private void StopHostInternal()
    {
        if (_host is not null)
        {
            _host.ViewerCountChanged -= HandleViewerCount;
            _host.ControllerChanged -= HandleController;
            _host.ErrorOccurred -= OnHostError;
            _host.Dispose();
            _host = null;
        }

        IsHosting = false;
        ViewerCount = 0;
        ControllerAddress = null;
        Pin = string.Empty;
        MyIpText = string.Empty;
        UpdateIndicator();
    }

    private void HandleViewerCount(int count) => _dispatcher.BeginInvoke(() =>
    {
        ViewerCount = count;
        SetHostStatus(count > 0
            ? Loc.F("지금 {0}대가 내 화면을 보고 있습니다.", count)
            : Loc.T("공유 중입니다. 보는 사람이 없습니다."));
        UpdateIndicator();
    });

    private void HandleController(string? address) => _dispatcher.BeginInvoke(() =>
    {
        ControllerAddress = address;
        if (!string.IsNullOrEmpty(address))
        {
            SetHostStatus(Loc.F("{0} 가 지금 내 PC를 제어하고 있습니다.", address));
            _log.Info($"원격 제어 시작: {address}");
        }

        UpdateIndicator();
    });

    partial void OnAllowControlChanged(bool value)
    {
        _host?.SetAllowControl(value);
        SaveSettings();
        _log.Info($"원격 제어 허용: {value}");
    }

    private void OnHostError(string message) => _dispatcher.BeginInvoke(() =>
        _log.Warn($"원격 공유 오류: {message}"));

    // ===================================================================== 뷰어(보기)

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _hostAddress = string.Empty;

    [ObservableProperty]
    private int _viewerPort = 5900;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    private string _inputPin = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditMode), nameof(CanEditViewerSetup), nameof(NotConnected), nameof(ShowMonitorPicker), nameof(CanToggleControl), nameof(ControlHintText))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand))]
    private bool _isConnected;

    public bool NotConnected => !IsConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEditMode), nameof(CanEditViewerSetup))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand), nameof(DisconnectCommand))]
    private bool _isConnecting;

    [ObservableProperty]
    private BitmapSource? _remoteImage;

    private BitmapSource? _lastFullFrame;

    /// <summary>전체 전송 이미지 크기(입력 좌표 환산용).</summary>
    private int _remoteFullW;
    private int _remoteFullH;

    /// <summary>호스트가 제어를 허용했는지(뷰어).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleControl), nameof(ControlHintText))]
    private bool _canControl;

    /// <summary>뷰어가 실제로 입력을 보내는 중인지. CanControl 이고 켰을 때만 전송합니다.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ControlHintText))]
    private bool _isControlling;

    /// <summary>원격이 여러 모니터면 '전체' 모드에서는 좌표가 어긋나므로, 개별 모니터를 골랐을 때만 제어를 허용합니다.</summary>
    public bool ControlAllowedForView =>
        InputMapping.ControlAllowed(ViewMonitors.Count - 1, SelectedViewMonitor?.Index ?? -1);

    /// <summary>제어 토글을 쓸 수 있는 상태(연결됨 + 호스트 허용 + 이 보기에서 제어 가능).</summary>
    public bool CanToggleControl => IsConnected && CanControl && ControlAllowedForView;

    public string ControlHintText => !CanControl
        ? Loc.T("보기 전용입니다. 호스트가 제어를 허용하면 조작할 수 있습니다.")
        : !ControlAllowedForView
            ? Loc.T("여러 모니터입니다. 제어하려면 위에서 볼 모니터를 하나 선택하세요.")
            : IsControlling
                ? Loc.T("제어 중입니다. 이 화면 위의 마우스·키보드가 상대 PC로 전송됩니다.")
                : Loc.T("제어가 허용되었습니다. [제어 시작]을 누르면 조작할 수 있습니다.");

    public System.Collections.ObjectModel.ObservableCollection<ViewMonitorOption> ViewMonitors { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanToggleControl), nameof(ControlHintText), nameof(ControlAllowedForView))]
    private ViewMonitorOption? _selectedViewMonitor;

    /// <summary>모니터가 2대 이상일 때만 선택 칸을 보여 줍니다.</summary>
    public bool ShowMonitorPicker => IsConnected && ViewMonitors.Count > 2;

    [ObservableProperty]
    private string _remoteSizeText = string.Empty;

    [ObservableProperty]
    private int _fps;

    [ObservableProperty]
    private string _viewerStatus = Loc.T("접속할 PC 의 IP·포트·PIN 을 입력하고 [연결]을 누르세요.");

    [ObservableProperty]
    private bool _viewerStatusIsError;

    public bool CanEditViewerSetup => !IsConnected && !IsConnecting;

    private bool CanConnect() => !IsConnected && !IsConnecting
        && !string.IsNullOrWhiteSpace(HostAddress) && !string.IsNullOrWhiteSpace(InputPin);

    [RelayCommand(CanExecute = nameof(CanConnect))]
    private async Task ConnectAsync()
    {
        IsConnecting = true;
        RemoteImage = null;
        SetViewerStatus(Loc.F("{0} 에 연결하는 중…", HostAddress));
        SaveSettings();

        _client = new RemoteViewerClient();
        _client.Connected += OnConnected;
        _client.FrameReceived += OnFrameReceived;
        _client.ControlGranted += OnControlGranted;
        _client.Disconnected += OnDisconnected;

        await _client.ConnectAsync(HostAddress.Trim(), ViewerPort, InputPin.Trim(), CancellationToken.None).ConfigureAwait(true);
    }

    private bool CanDisconnect() => IsConnected || IsConnecting;

    [RelayCommand(CanExecute = nameof(CanDisconnect))]
    private void Disconnect()
    {
        DisconnectInternal();
        SetViewerStatus(Loc.T("연결을 끊었습니다."));
    }

    private void DisconnectInternal()
    {
        if (_client is not null)
        {
            _client.Connected -= OnConnected;
            _client.FrameReceived -= OnFrameReceived;
            _client.ControlGranted -= OnControlGranted;
            _client.Disconnected -= OnDisconnected;
            _client.Dispose();
            _client = null;
        }

        IsConnected = false;
        IsConnecting = false;
        Fps = 0;
        CanControl = false;
        IsControlling = false;
        _lastFullFrame = null;
        ViewMonitors.Clear();
        SelectedViewMonitor = null;
        OnPropertyChanged(nameof(ShowMonitorPicker));
    }

    private void OnConnected(int w, int h, IReadOnlyList<(int X, int Y, int W, int H)> monitors) => _dispatcher.BeginInvoke(() =>
    {
        IsConnecting = false;
        IsConnected = true;
        _remoteFullW = w;
        _remoteFullH = h;
        CanControl = false;
        IsControlling = false;
        RemoteSizeText = Loc.F("{0} × {1}", w, h);

        ViewMonitors.Clear();
        ViewMonitors.Add(new ViewMonitorOption(-1, Loc.T("전체 (모든 모니터)"), 0, 0, 0, 0));
        for (var i = 0; i < monitors.Count; i++)
        {
            var m = monitors[i];
            ViewMonitors.Add(new ViewMonitorOption(i, Loc.F("모니터 {0}", i + 1), m.X, m.Y, m.W, m.H));
        }

        SelectedViewMonitor = ViewMonitors[0];
        OnPropertyChanged(nameof(ShowMonitorPicker));

        SetViewerStatus(Loc.F("연결됨 · {0}", HostAddress));
        _log.Info($"원격 보기 연결: {HostAddress}:{ViewerPort} ({w}x{h}, 모니터 {monitors.Count}개)");
    });

    private void OnFrameReceived(byte[] jpeg) => _dispatcher.BeginInvoke(() =>
    {
        if (!IsConnected)
        {
            return;
        }

        try
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(jpeg);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            _lastFullFrame = bmp;
            RemoteImage = CropToSelected(bmp);
            _framesThisSecond++;
        }
        catch (Exception ex)
        {
            _log.Warn("프레임을 그리지 못했습니다.", ex);
        }
    }, DispatcherPriority.Render);

    /// <summary>고른 모니터 영역으로 자릅니다. 전체면 그대로.</summary>
    private BitmapSource CropToSelected(BitmapSource full)
    {
        if (SelectedViewMonitor is not { Index: >= 0 } m || m.W <= 0 || m.H <= 0)
        {
            return full;
        }

        if (m.X < 0 || m.Y < 0 || m.X + m.W > full.PixelWidth || m.Y + m.H > full.PixelHeight)
        {
            return full;
        }

        var cropped = new CroppedBitmap(full, new System.Windows.Int32Rect(m.X, m.Y, m.W, m.H));
        cropped.Freeze();
        return cropped;
    }

    partial void OnSelectedViewMonitorChanged(ViewMonitorOption? value)
    {
        if (_lastFullFrame is not null && IsConnected)
        {
            RemoteImage = CropToSelected(_lastFullFrame);
        }

        // 제어 불가한 보기(여러 모니터 + 전체)로 바뀌면 제어를 즉시 멈춥니다.
        if (!ControlAllowedForView)
        {
            IsControlling = false;
        }
    }

    private void OnControlGranted(bool granted) => _dispatcher.BeginInvoke(() =>
    {
        CanControl = granted;
        if (!granted)
        {
            IsControlling = false;
        }

        SetViewerStatus(granted
            ? Loc.T("호스트가 제어를 허용했습니다. [제어 시작]으로 조작할 수 있습니다.")
            : Loc.F("연결됨 · {0}", HostAddress));
    });

    partial void OnCanControlChanged(bool value)
    {
        if (!value)
        {
            IsControlling = false;
        }
    }

    [RelayCommand]
    private void ToggleControl()
    {
        if (!CanToggleControl)
        {
            IsControlling = false;
            return;
        }

        IsControlling = !IsControlling;
    }

    // ===== 뷰어 입력 전송 (View 에서 호출) =====

    /// <summary>제어 중일 때만 마우스 입력을 보냅니다. u,v 는 표시 이미지 기준 0~1.</summary>
    public void SendMouseInput(RemoteProtocol.MouseAction action, double u, double v, int data = 0)
    {
        if (!IsControlling || !CanControl || !ControlAllowedForView || _client is null)
        {
            return;
        }

        var (cx, cy, cw, ch) = CurrentCrop();
        var (nx, ny) = InputMapping.ToFull(u, v, cx, cy, cw, ch, _remoteFullW, _remoteFullH);
        _client.SendMouse(action, nx, ny, data);
    }

    /// <summary>제어 중일 때만 키 입력을 보냅니다.</summary>
    public void SendKeyInput(ushort vk, bool down)
    {
        if (!IsControlling || !CanControl || !ControlAllowedForView || _client is null)
        {
            return;
        }

        _client.SendKey(vk, down);
    }

    /// <summary>지금 보고 있는 영역(개별 모니터면 그 크롭, 전체면 0).</summary>
    private (int X, int Y, int W, int H) CurrentCrop()
        => SelectedViewMonitor is { Index: >= 0 } m ? (m.X, m.Y, m.W, m.H) : (0, 0, 0, 0);

    private void OnDisconnected(string reason) => _dispatcher.BeginInvoke(() =>
    {
        var wasConnected = IsConnected || IsConnecting;
        DisconnectInternal();
        if (wasConnected)
        {
            SetViewerStatus(reason, isError: true);
            _log.Info($"원격 보기 종료: {reason}");
        }
    });

    // ===================================================================== 공유 중 표시

    private RemoteIndicatorWindow? _indicator;

    private void UpdateIndicator()
    {
        if (IsHosting && ViewerCount > 0)
        {
            _indicator ??= new RemoteIndicatorWindow();
            _indicator.SetState(ViewerCount, IsBeingControlled, ControllerAddress);
            _indicator.Show();
        }
        else
        {
            _indicator?.Hide();
        }
    }

    // ===================================================================== 설정

    partial void OnPortChanged(int value) => SaveSettings();

    partial void OnQualityChanged(int value) => SaveSettings();

    partial void OnSelectedResolutionChanged(ResolutionOption? value) => SaveSettings();

    partial void OnFpsSettingChanged(int value) => SaveSettings();

    partial void OnSelectedBindChanged(BindOption? value) => SaveSettings();

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var s = _settings.Get(SettingsKey, new RemoteViewSettings());
            Port = s.Port is > 0 and < 65536 ? s.Port : 5900;
            SelectedBind = BindOptions.FirstOrDefault(b => b.Address == s.Bind) ?? BindOptions[0];
            Quality = QualityChoices.Contains(s.Quality) ? s.Quality : 60;
            SelectedResolution = ResolutionChoices.FirstOrDefault(r => r.MaxWidth == s.MaxWidth)
                ?? ResolutionChoices.First(r => r.MaxWidth == 1920);
            FpsSetting = FpsChoices.Contains(s.Fps) ? s.Fps : 5;
            AllowControl = s.AllowControl;
            HostAddress = s.HostAddress ?? string.Empty;
            ViewerPort = s.ViewerPort is > 0 and < 65536 ? s.ViewerPort : 5900;
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

        _settings.Set(SettingsKey, new RemoteViewSettings
        {
            Port = Port,
            Bind = SelectedBind?.Address,
            Quality = Quality,
            MaxWidth = SelectedResolution?.MaxWidth ?? 1920,
            Fps = FpsSetting,
            AllowControl = AllowControl,
            HostAddress = HostAddress,
            ViewerPort = ViewerPort,
        });
        _settings.Save();
    }

    private void SetHostStatus(string text, bool isError = false)
    {
        HostStatus = text;
        HostStatusIsError = isError;
    }

    private void SetViewerStatus(string text, bool isError = false)
    {
        ViewerStatus = text;
        ViewerStatusIsError = isError;
    }

    public void Dispose()
    {
        _fpsTimer.Stop();
        StopHostInternal();
        DisconnectInternal();
        _indicator?.Close();
        _indicator = null;
    }
}
