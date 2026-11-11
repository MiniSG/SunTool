using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO.Ports;
using System.Net;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.Core.Logging;
using SunTool.Core.Settings;
using SunTool.Modules.Comm.Logic;
using SunTool.Wpf.Services;

namespace SunTool.Modules.Comm.ViewModels;

/// <summary>
/// 통신 테스트 화면.
/// COM 포트·소켓은 사용자가 [연결]을 누를 때만 엽니다. 포트 목록 갱신(2초마다)은 이름만 읽고 포트를 열지 않습니다.
/// </summary>
public sealed partial class CommToolViewModel : ObservableObject, IDisposable
{
    private const string SettingsKey = "CommTool";
    private const int MaxEntries = 50_000;
    private const int TrimCount = 5_000;
    private const int MaxHistory = 30;

    private readonly IAppLogger _log;
    private readonly IDialogService _dialogs;
    private readonly IClipboardService _clipboard;
    private readonly ISettingsService _settings;
    private readonly SerialPortCatalog _catalog;
    private readonly ConcurrentQueue<Incoming> _incoming = new();
    private readonly ReceiveAssembler _assembler;
    private readonly DispatcherTimer _drainTimer;
    private readonly DispatcherTimer _portTimer;
    private string[] _portNames = [];
    private bool _readingPorts;
    private ICommChannel? _channel;
    private CancellationTokenSource? _connectCts;
    private int _nextIndex = 1;
    private DateTime? _lastDataTime;
    private bool _loading;

    public CommToolViewModel(ILogService logs, IDialogService dialogs, IClipboardService clipboard, ISettingsService settings, SerialPortCatalog catalog)
    {
        _log = logs.For("Comm");
        _dialogs = dialogs;
        _clipboard = clipboard;
        _settings = settings;
        _catalog = catalog;
        _assembler = new ReceiveAssembler((peer, data, time) => _incoming.Enqueue(new Incoming(DirectionOfPeer(peer), peer, data, time, null)));

        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = o => o is CommLogEntry e && PassesFilter(e);

        LoadSettings();
        ApplyFraming();

        _drainTimer = new DispatcherTimer(TimeSpan.FromMilliseconds(50), DispatcherPriority.Background, (_, _) => Drain(), Dispatcher.CurrentDispatcher);
        _drainTimer.Start();
        _portTimer = new DispatcherTimer(TimeSpan.FromSeconds(2), DispatcherPriority.Background, (_, _) => RefreshPorts(force: false), Dispatcher.CurrentDispatcher);
        _portTimer.Start();
        RefreshPorts(force: true);
        UpdatePreview();
    }

    // ===================================================================== 선택지

    public IReadOnlyList<Option<ConnectionKind>> Kinds { get; } =
    [
        new(ConnectionKind.Serial, Loc.T("시리얼 (COM)")),
        new(ConnectionKind.TcpClient, Loc.T("TCP 클라이언트")),
        new(ConnectionKind.TcpServer, Loc.T("TCP 서버")),
        new(ConnectionKind.Udp, "UDP"),
        new(ConnectionKind.Relay, Loc.T("Steal (COM 2개 중계)")),
    ];

    public IReadOnlyList<int> BaudRates { get; } = [300, 600, 1200, 2400, 4800, 9600, 14400, 19200, 28800, 38400, 57600, 115200, 230400, 460800, 921600];

    public IReadOnlyList<int> DataBitsOptions { get; } = [5, 6, 7, 8];

    public IReadOnlyList<Option<Parity>> Parities { get; } =
    [
        new(Parity.None, Loc.T("없음 (None)")),
        new(Parity.Even, Loc.T("짝수 (Even)")),
        new(Parity.Odd, Loc.T("홀수 (Odd)")),
        new(Parity.Mark, "Mark"),
        new(Parity.Space, "Space"),
    ];

    public IReadOnlyList<Option<StopBits>> StopBitsOptions { get; } =
    [
        new(StopBits.One, "1"),
        new(StopBits.OnePointFive, "1.5"),
        new(StopBits.Two, "2"),
    ];

    public IReadOnlyList<Option<Handshake>> Handshakes { get; } =
    [
        new(Handshake.None, Loc.T("없음")),
        new(Handshake.XOnXOff, "Xon/Xoff"),
        new(Handshake.RequestToSend, "RTS/CTS"),
        new(Handshake.RequestToSendXOnXOff, "RTS/CTS + Xon/Xoff"),
    ];

    public IReadOnlyList<Option<InputMode>> InputModes { get; } =
    [
        new(InputMode.Text, Loc.T("텍스트")),
        new(InputMode.Hex, "HEX"),
    ];

    public IReadOnlyList<Option<TextEncodingKind>> Encodings { get; } =
    [
        new(TextEncodingKind.Ascii, "ASCII"),
        new(TextEncodingKind.Utf8, "UTF-8"),
        new(TextEncodingKind.EucKr, Loc.T("EUC-KR (한글)")),
    ];

    public IReadOnlyList<Option<CheckKind>> Checks { get; } =
    [
        new(CheckKind.None, Loc.T("없음")),
        new(CheckKind.Sum8, Loc.T("CHECKSUM (합 8비트)")),
        new(CheckKind.Xor8, Loc.T("XOR (BCC)")),
        new(CheckKind.Crc16Modbus, "CRC-16 (Modbus)"),
        new(CheckKind.Crc16Ccitt, "CRC-16 (CCITT)"),
    ];

    public IReadOnlyList<Option<CheckByteOrder>> CheckOrders { get; } =
    [
        new(CheckByteOrder.HighFirst, "HI, LO"),
        new(CheckByteOrder.LowFirst, "LO, HI"),
    ];

    public IReadOnlyList<Option<LogViewMode>> ViewModes { get; } =
    [
        new(LogViewMode.Text, Loc.T("텍스트")),
        new(LogViewMode.Hex, "HEX"),
        new(LogViewMode.Both, Loc.T("둘 다")),
    ];

    // ===================================================================== 연결 설정

    [ObservableProperty]
    private ConnectionKind _kind = ConnectionKind.Serial;

    public ObservableCollection<SerialPortEntry> Ports { get; } = [];

    [ObservableProperty]
    private string? _portName;

    [ObservableProperty]
    private int _baudRate = 9600;

    [ObservableProperty]
    private int _dataBits = 8;

    [ObservableProperty]
    private Parity _parity = Parity.None;

    [ObservableProperty]
    private StopBits _stopBits = StopBits.One;

    [ObservableProperty]
    private Handshake _handshake = Handshake.None;

    [ObservableProperty]
    private string _tcpHost = "127.0.0.1";

    [ObservableProperty]
    private int _tcpPort = 5000;

    [ObservableProperty]
    private string _serverAddress = "0.0.0.0";

    [ObservableProperty]
    private int _serverPort = 5000;

    [ObservableProperty]
    private int _udpLocalPort = 6000;

    [ObservableProperty]
    private string _udpRemoteHost = "127.0.0.1";

    [ObservableProperty]
    private int _udpRemotePort = 6001;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectButtonText), nameof(CanEditConnection))]
    private bool _isConnected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ConnectButtonText), nameof(CanEditConnection))]
    private bool _isConnecting;

    [ObservableProperty]
    private string _connectionTitle = Loc.T("연결 안 됨");

    [ObservableProperty]
    private string _statusText = Loc.T("연결 방식을 고르고 [연결]을 누르세요.");

    [ObservableProperty]
    private bool _statusIsError;

    public string ConnectButtonText => IsConnecting ? Loc.T("취소") : IsConnected ? Loc.T("연결 끊기") : Loc.T("연결");

    public bool CanEditConnection => !IsConnected && !IsConnecting;

    public string PortListHint => Ports.Count(p => p.IsPresent) == 0
        ? Loc.T("지금 PC 에 연결된 COM 포트가 없습니다. USB-시리얼 변환기를 꽂으면 자동으로 나타납니다.")
        : Loc.F("연결된 COM 포트 {0}개 · 꽂거나 뽑으면 자동으로 새로 고칩니다.", Ports.Count(p => p.IsPresent));

    // ===================================================================== 보내기

    [ObservableProperty]
    private string _sendText = string.Empty;

    public ObservableCollection<string> History { get; } = [];

    [ObservableProperty]
    private InputMode _inputMode = InputMode.Text;

    [ObservableProperty]
    private TextEncodingKind _encoding = TextEncodingKind.Ascii;

    [ObservableProperty]
    private bool _escapes = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasCheck))]
    private CheckKind _check = CheckKind.None;

    [ObservableProperty]
    private CheckByteOrder _checkOrder = CheckByteOrder.HighFirst;

    [ObservableProperty]
    private bool _appendCr;

    [ObservableProperty]
    private bool _appendLf;

    [ObservableProperty]
    private string _sendPreview = string.Empty;

    [ObservableProperty]
    private bool _sendPreviewIsError;

    public bool HasCheck => Check != CheckKind.None;

    public bool IsTwoByteCheck => Checksum.Length(Check) == 2;

    // ===================================================================== 로그

    public ObservableCollection<CommLogEntry> Entries { get; } = [];

    public ICollectionView EntriesView { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SelectedDump), nameof(SelectedTitle), nameof(HasSelection))]
    private CommLogEntry? _selectedEntry;

    [ObservableProperty]
    private LogViewMode _viewMode = LogViewMode.Both;

    [ObservableProperty]
    private bool _showSent = true;

    [ObservableProperty]
    private bool _showReceived = true;

    [ObservableProperty]
    private bool _showNotices = true;

    [ObservableProperty]
    private bool _showDelta = true;

    [ObservableProperty]
    private bool _autoScroll = true;

    [ObservableProperty]
    private bool _verifyReceived = true;

    [ObservableProperty]
    private int _gapMs = 20;

    [ObservableProperty]
    private string _searchText = string.Empty;

    /// <summary>true: 검색어와 일치하는 줄만 표시. false: 모든 줄을 두고 일치 부분만 하이라이트 (▲▼ 로 이동).</summary>
    [ObservableProperty]
    private bool _onlyMatches = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MatchText), nameof(HasSearch))]
    private int _matchCount;

    public bool HasSearch => !string.IsNullOrWhiteSpace(SearchText);

    /// <summary>"3건 일치" 또는 선택한 줄이 일치 줄이면 "2 / 3".</summary>
    public string MatchText
    {
        get
        {
            if (!HasSearch)
            {
                return string.Empty;
            }

            if (MatchCount == 0)
            {
                return Loc.T("일치 없음");
            }

            var position = SelectedEntry is { } selected && IsMatch(selected)
                ? MatchingEntries().TakeWhile(e => !ReferenceEquals(e, selected)).Count() + 1
                : 0;
            return position > 0 ? Loc.F("{0} / {1}", position, MatchCount) : Loc.F("{0}건 일치", MatchCount);
        }
    }

    /// <summary>검색 이동으로 선택한 줄을 화면에 보이게 해 달라는 요청 (UI 스레드).</summary>
    public event Action<CommLogEntry>? ScrollToRequested;

    [ObservableProperty]
    private long _txBytes;

    [ObservableProperty]
    private long _rxBytes;

    [ObservableProperty]
    private int _txCount;

    [ObservableProperty]
    private int _rxCount;

    /// <summary>새 줄이 추가되면 발생 (자동 스크롤용, UI 스레드).</summary>
    public event Action<CommLogEntry>? EntryAdded;

    public bool HasSelection => SelectedEntry?.IsData == true;

    public string SelectedTitle => SelectedEntry is { IsData: true } e
        ? Loc.F("#{0} · {1} · {2} · {3}바이트", e.IndexText, e.DirectionText, e.Peer, e.Data.Length)
        : Loc.T("줄을 선택하면 16바이트 단위 HEX 덤프가 여기에 나옵니다.");

    public string SelectedDump => SelectedEntry is { IsData: true } e ? ByteCodec.ToHexDump(e.Data) : string.Empty;

    public string CounterText => IsRelayKind
        ? Loc.F("1→2 {0:N0}건 · {1:N0} B   2→1 {2:N0}건 · {3:N0} B", TxCount, TxBytes, RxCount, RxBytes)
        : Loc.F("송신 {0:N0}건 · {1:N0} B   수신 {2:N0}건 · {3:N0} B", TxCount, TxBytes, RxCount, RxBytes);

    public string VisibleCountText => Loc.F("표시 {0:N0} / 전체 {1:N0}줄", EntriesView.Cast<object>().Count(), Entries.Count);

    // ===================================================================== 변경 처리

    partial void OnKindChanged(ConnectionKind value)
    {
        OnPropertyChanged(nameof(IsRelayKind));
        OnPropertyChanged(nameof(CounterText));
        SaveSettings();
    }

    partial void OnPortNameChanged(string? value)
    {
        OnPropertyChanged(nameof(InjectSides));
        SaveSettings();
    }

    partial void OnBaudRateChanged(int value) => SaveSettings();

    partial void OnDataBitsChanged(int value) => SaveSettings();

    partial void OnParityChanged(Parity value) => SaveSettings();

    partial void OnStopBitsChanged(StopBits value) => SaveSettings();

    partial void OnHandshakeChanged(Handshake value) => SaveSettings();

    partial void OnSendTextChanged(string value) => UpdatePreview();

    partial void OnInputModeChanged(InputMode value) => OnSendOptionChanged();

    partial void OnEncodingChanged(TextEncodingKind value)
    {
        OnSendOptionChanged();
        RecountMatches();
        EntriesView.Refresh(); // 표시 글자 다시 그리기
    }

    partial void OnEscapesChanged(bool value) => OnSendOptionChanged();

    partial void OnCheckChanged(CheckKind value)
    {
        OnPropertyChanged(nameof(IsTwoByteCheck));
        OnSendOptionChanged();
    }

    partial void OnCheckOrderChanged(CheckByteOrder value) => OnSendOptionChanged();

    partial void OnAppendCrChanged(bool value) => OnSendOptionChanged();

    partial void OnAppendLfChanged(bool value) => OnSendOptionChanged();

    partial void OnViewModeChanged(LogViewMode value) => SaveSettings();

    partial void OnShowDeltaChanged(bool value) => SaveSettings();

    partial void OnAutoScrollChanged(bool value) => SaveSettings();

    partial void OnVerifyReceivedChanged(bool value) => SaveSettings();

    partial void OnGapMsChanged(int value)
    {
        _assembler.GapMs = Math.Clamp(value, 0, 5000);
        SaveSettings();
    }

    partial void OnShowSentChanged(bool value) => RefreshFilterAndCount();

    partial void OnShowReceivedChanged(bool value) => RefreshFilterAndCount();

    partial void OnShowNoticesChanged(bool value) => RefreshFilterAndCount();

    private void RefreshFilterAndCount()
    {
        RecountMatches();
        RefreshFilter();
    }

    partial void OnSearchTextChanged(string value)
    {
        RecountMatches();
        RefreshFilter();
        OnPropertyChanged(nameof(HasSearch));
    }

    partial void OnOnlyMatchesChanged(bool value)
    {
        RefreshFilter();
        SaveSettings();
    }

    partial void OnSelectedEntryChanged(CommLogEntry? value) => OnPropertyChanged(nameof(MatchText));

    partial void OnTxBytesChanged(long value) => OnPropertyChanged(nameof(CounterText));

    partial void OnRxBytesChanged(long value) => OnPropertyChanged(nameof(CounterText));

    partial void OnTxCountChanged(int value) => OnPropertyChanged(nameof(CounterText));

    partial void OnRxCountChanged(int value) => OnPropertyChanged(nameof(CounterText));

    private void OnSendOptionChanged()
    {
        UpdatePreview();
        SaveSettings();
    }

    private void RefreshFilter()
    {
        EntriesView.Refresh();
        OnPropertyChanged(nameof(VisibleCountText));
    }

    /// <summary>송신/수신/알림 체크 (검색과 무관).</summary>
    private bool PassesKind(CommLogEntry e) => e.Direction switch
    {
        CommDirection.Sent => ShowSent,
        CommDirection.Received => ShowReceived,
        CommDirection.Relay1To2 => ShowRelay1To2,
        CommDirection.Relay2To1 => ShowRelay2To1,
        _ => ShowNotices,
    };

    private bool PassesFilter(CommLogEntry e) => PassesKind(e) && (!OnlyMatches || e.Matches(SearchText, Encoding));

    private bool IsMatch(CommLogEntry e) => HasSearch && PassesKind(e) && e.Matches(SearchText, Encoding);

    /// <summary>화면 순서대로 일치하는 줄.</summary>
    private IEnumerable<CommLogEntry> MatchingEntries() => Entries.Where(IsMatch);

    private void RecountMatches() => MatchCount = HasSearch ? MatchingEntries().Count() : 0;

    [RelayCommand]
    private void NextMatch() => MoveMatch(forward: true);

    [RelayCommand]
    private void PreviousMatch() => MoveMatch(forward: false);

    /// <summary>선택한 줄 다음(이전) 일치 줄로 이동. 끝에 닿으면 처음(끝)으로 돌아갑니다.</summary>
    private void MoveMatch(bool forward)
    {
        if (!HasSearch || MatchCount == 0)
        {
            return;
        }

        var current = SelectedEntry is null ? -1 : Entries.IndexOf(SelectedEntry);
        CommLogEntry? target = null;
        if (forward)
        {
            target = Entries.Skip(current + 1).FirstOrDefault(IsMatch) ?? Entries.FirstOrDefault(IsMatch);
        }
        else
        {
            var before = current < 0 ? Entries.Count : current;
            target = Entries.Take(before).LastOrDefault(IsMatch) ?? Entries.LastOrDefault(IsMatch);
        }

        if (target is null)
        {
            return;
        }

        // 일치 줄로 이동하는 동안 자동 스크롤이 맨 아래로 끌고 가지 않도록 끔
        AutoScroll = false;
        SelectedEntry = target;
        ScrollToRequested?.Invoke(target);
    }

    private void UpdatePreview()
    {
        if (string.IsNullOrEmpty(SendText))
        {
            SendPreview = InputMode == InputMode.Hex
                ? Loc.T("예: 02 52 45 41 44 03 (공백·쉼표 구분, 0x 접두어 허용)")
                : Escapes ? Loc.T(@"예: READ? (\r \n \t \xHH 로 제어 문자 입력)") : Loc.T("보낼 텍스트를 입력하세요.");
            SendPreviewIsError = false;
            return;
        }

        try
        {
            var bytes = BuildBytes(SendText);
            SendPreview = Loc.F("보낼 바이트 {0}개: {1}", bytes.Length, bytes.Length > 64 ? ByteCodec.ToHex(bytes.AsSpan(0, 64)) + " …" : ByteCodec.ToHex(bytes));
            SendPreviewIsError = false;
        }
        catch (FormatException ex)
        {
            SendPreview = ex.Message;
            SendPreviewIsError = true;
        }
    }

    private byte[] BuildBytes(string text) =>
        SendBuilder.Build(text, InputMode, Encoding, Escapes, Check, CheckOrder, AppendCr, AppendLf);

    // ===================================================================== 포트 목록

    [RelayCommand]
    private void RefreshPortList() => RefreshPorts(force: true);

    private void RefreshPorts(bool force)
    {
        var names = SerialPortCatalog.ReadPortNames();
        var changed = !names.SequenceEqual(_portNames, StringComparer.OrdinalIgnoreCase);
        if ((!changed && !force) || _readingPorts)
        {
            return;
        }

        var removed = _portNames.Except(names, StringComparer.OrdinalIgnoreCase).ToList();
        var added = names.Except(_portNames, StringComparer.OrdinalIgnoreCase).ToList();
        var firstRead = _portNames.Length == 0 && force && Ports.Count == 0;
        _portNames = names;

        // 연결 중인 포트가 사라짐 → 장치 분리
        if (_channel is SerialChannel && Kind == ConnectionKind.Serial && PortName is { } current
            && removed.Contains(current, StringComparer.OrdinalIgnoreCase))
        {
            Disconnect(Loc.F("{0} 장치가 분리되었습니다.", current), isError: true);
        }
        else if (_channel is RelayChannel relay
                 && removed.FirstOrDefault(r => r.Equals(relay.Name1, StringComparison.OrdinalIgnoreCase) || r.Equals(relay.Name2, StringComparison.OrdinalIgnoreCase)) is { } gone)
        {
            Disconnect(Loc.F("{0} 장치가 분리되었습니다.", gone), isError: true);
        }

        if (!firstRead)
        {
            foreach (var name in added)
            {
                AddNotice(Loc.F("COM 포트 연결됨: {0}", name), false);
            }

            foreach (var name in removed)
            {
                AddNotice(Loc.F("COM 포트 분리됨: {0}", name), false);
            }
        }

        _readingPorts = true;
        var dispatcher = Dispatcher.CurrentDispatcher;
        Task.Run(() => _catalog.Read(names)).ContinueWith(t =>
        {
            dispatcher.BeginInvoke(() =>
            {
                _readingPorts = false;
                ApplyPorts(t.IsCompletedSuccessfully ? t.Result : names.Select(n => new SerialPortEntry(n, string.Empty)).ToList());
            });
        }, TaskScheduler.Default);
    }

    private void ApplyPorts(IReadOnlyList<SerialPortEntry> present)
    {
        var selected = PortName;
        var selected2 = PortName2;
        var list = present.ToList();
        foreach (var name in new[] { selected, selected2 })
        {
            if (!string.IsNullOrEmpty(name) && !list.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            {
                // 마지막으로 쓴 포트가 지금 없으면 회색으로 남겨 둠 (열지 않음)
                list.Add(new SerialPortEntry(name, Loc.T("지금 연결 안 됨"), IsPresent: false));
            }
        }

        _loading = true;
        Ports.Clear();
        foreach (var port in list)
        {
            Ports.Add(port);
        }

        PortName = selected ?? list.FirstOrDefault(p => p.IsPresent)?.Name;
        PortName2 = selected2 ?? list.FirstOrDefault(p => p.IsPresent && !string.Equals(p.Name, PortName, StringComparison.OrdinalIgnoreCase))?.Name;
        _loading = false;
        OnPropertyChanged(nameof(PortListHint));
    }

    // ===================================================================== 연결

    [RelayCommand]
    private async Task ToggleConnectionAsync()
    {
        if (IsConnecting)
        {
            _connectCts?.Cancel();
            return;
        }

        if (IsConnected)
        {
            Disconnect(Loc.T("연결을 끊었습니다."), isError: false);
            return;
        }

        ICommChannel channel;
        try
        {
            channel = CreateChannel();
        }
        catch (Exception ex) when (ex is ArgumentException or FormatException or InvalidOperationException)
        {
            SetStatus(ex.Message, isError: true);
            return;
        }

        IsConnecting = true;
        SetStatus(Loc.F("연결 중… {0}", channel.Title), isError: false);
        _connectCts = new CancellationTokenSource();
        AttachChannel(channel);
        try
        {
            await channel.OpenAsync(_connectCts.Token);
            _channel = channel;
            IsConnected = true;
            ConnectionTitle = channel.Title;
            SetStatus(Loc.F("연결됨: {0}", channel.Title), isError: false);
            AddNotice(Loc.F("연결: {0}", channel.Title), false);
            _log.Info($"통신 연결: {channel.Title}");
        }
        catch (Exception ex)
        {
            DetachChannel(channel);
            channel.Dispose();
            var message = ex is OperationCanceledException ? Loc.T("연결을 취소했습니다.") : DescribeOpenError(ex);
            SetStatus(message, isError: ex is not OperationCanceledException);
            AddNotice(message, ex is not OperationCanceledException);
            _log.Warn($"통신 연결 실패: {channel.Title}", ex);
        }
        finally
        {
            IsConnecting = false;
            _connectCts?.Dispose();
            _connectCts = null;
        }
    }

    private ICommChannel CreateChannel()
    {
        switch (Kind)
        {
            case ConnectionKind.Serial:
                if (string.IsNullOrWhiteSpace(PortName))
                {
                    throw new InvalidOperationException(Loc.T("COM 포트를 고르세요."));
                }

                if (!_portNames.Contains(PortName, StringComparer.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(Loc.F("{0} 은(는) 지금 PC 에 연결되어 있지 않습니다.", PortName));
                }

                return new SerialChannel(new SerialSettings(PortName, BaudRate, DataBits, Parity, StopBits, Handshake));
            case ConnectionKind.Relay:
                return CreateRelayChannel();
            case ConnectionKind.TcpClient:
                if (string.IsNullOrWhiteSpace(TcpHost))
                {
                    throw new InvalidOperationException(Loc.T("서버 주소를 입력하세요."));
                }

                return new TcpClientChannel(TcpHost.Trim(), CheckPort(TcpPort), TimeSpan.FromSeconds(5));
            case ConnectionKind.TcpServer:
                if (!IPAddress.TryParse(string.IsNullOrWhiteSpace(ServerAddress) ? "0.0.0.0" : ServerAddress.Trim(), out var address))
                {
                    throw new InvalidOperationException(Loc.F("IP 주소 형식이 아닙니다: {0}", ServerAddress));
                }

                return new TcpServerChannel(address, CheckPort(ServerPort));
            default:
                return new UdpChannel(
                    UdpLocalPort == 0 ? 0 : CheckPort(UdpLocalPort),
                    string.IsNullOrWhiteSpace(UdpRemoteHost) ? null : UdpRemoteHost.Trim(),
                    string.IsNullOrWhiteSpace(UdpRemoteHost) ? 0 : CheckPort(UdpRemotePort));
        }
    }

    private static int CheckPort(int port) => port is >= 1 and <= 65535
        ? port
        : throw new InvalidOperationException(Loc.F("포트 번호는 1~65535 사이여야 합니다: {0}", port));

    private static string DescribeOpenError(Exception ex) => ex switch
    {
        UnauthorizedAccessException => Loc.T("포트를 열 수 없습니다. 다른 프로그램이 이미 사용 중일 수 있습니다."),
        System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.AddressAlreadyInUse } =>
            Loc.T("이 포트 번호는 이미 다른 프로그램이 쓰고 있습니다."),
        System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.ConnectionRefused } =>
            Loc.T("상대가 연결을 거부했습니다 (서버가 실행 중인지, 포트 번호가 맞는지 확인)."),
        _ => Loc.F("연결 실패: {0}", ex.Message),
    };

    private void Disconnect(string message, bool isError)
    {
        var channel = _channel;
        _channel = null;
        StopAutoSending();
        if (channel is not null)
        {
            DetachChannel(channel);
            channel.Dispose();
            _assembler.FlushAll();
            _log.Info($"통신 끊김: {channel.Title} ({message})");
        }

        IsConnected = false;
        ConnectionTitle = Loc.T("연결 안 됨");
        SetStatus(message, isError);
        AddNotice(message, isError);
    }

    private void OnChannelReceived(string peer, byte[] data) => _assembler.Add(peer, data, DateTime.Now);

    private void AttachChannel(ICommChannel channel)
    {
        if (channel is RelayChannel relay)
        {
            relay.Relayed += OnRelayed;
        }
        else
        {
            channel.Received += OnChannelReceived;
        }

        channel.Notice += OnChannelNotice;
        channel.Closed += OnChannelClosed;
    }

    private void DetachChannel(ICommChannel channel)
    {
        if (channel is RelayChannel relay)
        {
            relay.Relayed -= OnRelayed;
        }

        channel.Received -= OnChannelReceived;
        channel.Notice -= OnChannelNotice;
        channel.Closed -= OnChannelClosed;
    }

    private void OnChannelNotice(string message, bool isError) =>
        _incoming.Enqueue(new Incoming(isError ? CommDirection.Error : CommDirection.Info, string.Empty, [], DateTime.Now, message));

    private void OnChannelClosed(string reason)
    {
        var dispatcher = _drainTimer.Dispatcher;
        dispatcher.BeginInvoke(() =>
        {
            if (_channel is not null)
            {
                Disconnect(reason, isError: true);
            }
        });
    }

    // ===================================================================== 보내기

    /// <summary>[보내기]: 반복 보내기가 켜져 있으면 반복 시작/중지, 아니면 한 번.</summary>
    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsRepeating)
        {
            StopAutoSending();
            return;
        }

        if (RepeatEnabled)
        {
            await StartRepeatAsync();
            return;
        }

        if (await SendTextAsync(SendText, InputMode))
        {
            AddHistory(SendText);
        }
    }

    /// <summary>입력 문자열을 지금 보내기 옵션으로 바이트로 만들어 보냄. 성공하면 true.</summary>
    private async Task<bool> SendTextAsync(string text, InputMode mode)
    {
        byte[] bytes;
        try
        {
            bytes = SendBuilder.Build(text, mode, Encoding, Escapes, Check, CheckOrder, AppendCr, AppendLf);
        }
        catch (FormatException ex)
        {
            SetStatus(ex.Message, isError: true);
            return false;
        }

        if (bytes.Length == 0)
        {
            SetStatus(Loc.T("보낼 내용을 입력하세요."), isError: true);
            return false;
        }

        return await SendBytesAsync(bytes);
    }

    private async Task<bool> SendBytesAsync(byte[] bytes)
    {
        if (_channel is not { } channel)
        {
            SetStatus(Loc.T("먼저 [연결]을 누르세요."), isError: true);
            return false;
        }

        try
        {
            // Steal 모드: 고른 쪽(장비 1 / 장비 2)으로 직접 끼워 넣기
            var peer = channel is RelayChannel relay
                ? "→ " + await relay.SendToAsync(InjectSide, bytes, CancellationToken.None)
                : await channel.SendAsync(bytes, CancellationToken.None);
            _incoming.Enqueue(new Incoming(CommDirection.Sent, peer, bytes, DateTime.Now, null));
            Drain();
            return true;
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("보내기 실패: {0}", ex.Message), isError: true);
            AddNotice(Loc.F("보내기 실패: {0}", ex.Message), true);
            return false;
        }
    }

    private void AddHistory(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        var existing = History.IndexOf(text);
        if (existing == 0)
        {
            return;
        }

        if (existing > 0)
        {
            History.RemoveAt(existing);
        }

        History.Insert(0, text);
        while (History.Count > MaxHistory)
        {
            History.RemoveAt(History.Count - 1);
        }

        SaveSettings();
    }

    [RelayCommand]
    private void ClearHistory()
    {
        History.Clear();
        SaveSettings();
    }

    // ===================================================================== 로그 처리

    private void AddNotice(string message, bool isError) =>
        _incoming.Enqueue(new Incoming(isError ? CommDirection.Error : CommDirection.Info, string.Empty, [], DateTime.Now, message));

    /// <summary>쌓인 송수신을 로그 목록에 붙입니다 (UI 스레드, 50ms 마다).</summary>
    private void Drain()
    {
        if (_incoming.IsEmpty)
        {
            return;
        }

        CommLogEntry? last = null;
        var verify = VerifyReceived && Check != CheckKind.None;
        while (_incoming.TryDequeue(out var item))
        {
            double? delta = null;
            bool? check = null;
            if (item.Direction is CommDirection.Sent or CommDirection.Received or CommDirection.Relay1To2 or CommDirection.Relay2To1)
            {
                if (_lastDataTime is { } previous)
                {
                    delta = Math.Max(0, (item.Time - previous).TotalMilliseconds);
                }

                _lastDataTime = item.Time;
                if (item.Direction is CommDirection.Sent or CommDirection.Relay1To2)
                {
                    TxCount++;
                    TxBytes += item.Data.Length;
                }
                else
                {
                    RxCount++;
                    RxBytes += item.Data.Length;
                }

                if (item.Direction != CommDirection.Sent)
                {
                    check = verify ? Checksum.Verify(Check, item.Data, CheckOrder) : null;
                }
            }

            last = new CommLogEntry(_nextIndex++, item.Time, delta, item.Direction, item.Peer, item.Data, check, item.Message);
            Entries.Add(last);
            if (IsMatch(last))
            {
                MatchCount++;
            }
        }

        if (Entries.Count > MaxEntries)
        {
            // 오래된 줄부터 정리 (화면이 느려지지 않도록)
            for (var i = 0; i < TrimCount; i++)
            {
                Entries.RemoveAt(0);
            }

            RecountMatches();
        }

        OnPropertyChanged(nameof(VisibleCountText));
        if (last is not null)
        {
            EntryAdded?.Invoke(last);
        }
    }

    [RelayCommand]
    private void ClearLog()
    {
        Entries.Clear();
        _nextIndex = 1;
        _lastDataTime = null;
        TxBytes = RxBytes = 0;
        TxCount = RxCount = 0;
        MatchCount = 0;
        OnPropertyChanged(nameof(VisibleCountText));
    }

    [RelayCommand]
    private void SaveLog()
    {
        if (Entries.Count == 0)
        {
            SetStatus(Loc.T("저장할 로그가 없습니다."), isError: true);
            return;
        }

        var path = _dialogs.SaveFile(Loc.T("통신 로그 저장"), Loc.T("텍스트 파일 (*.txt)|*.txt"), $"CommLog_{DateTime.Now:yyyyMMdd_HHmmss}.txt");
        if (path is null)
        {
            return;
        }

        try
        {
            // 화면에 보이는 줄(필터 적용)만 저장
            CommLogWriter.Save(path, ConnectionTitle, EntriesView.Cast<CommLogEntry>(), Encoding);
            SetStatus(Loc.F("로그를 저장했습니다: {0}", path), isError: false);
        }
        catch (Exception ex)
        {
            _dialogs.ShowError(Loc.F("로그를 저장하지 못했습니다.\n{0}", ex.Message));
        }
    }

    [RelayCommand]
    private void CopyHex()
    {
        if (SelectedEntry is { IsData: true } e && _clipboard.SetText(e.HexText))
        {
            SetStatus(Loc.T("HEX 를 복사했습니다."), isError: false);
        }
    }

    [RelayCommand]
    private void CopyText()
    {
        if (SelectedEntry is { IsData: true } e && _clipboard.SetText(ByteCodec.ToDisplayText(e.Data, Encoding)))
        {
            SetStatus(Loc.T("텍스트를 복사했습니다."), isError: false);
        }
    }

    /// <summary>선택한 줄의 데이터를 보내기 칸에 넣기 (받은 내용을 그대로 다시 보내 볼 때).</summary>
    [RelayCommand]
    private void UseAsSend()
    {
        if (SelectedEntry is { IsData: true } e)
        {
            InputMode = InputMode.Hex;
            SendText = e.HexText;
        }
    }

    private void SetStatus(string message, bool isError)
    {
        StatusText = message;
        StatusIsError = isError;
    }

    // ===================================================================== 설정

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var s = _settings.Get(SettingsKey, new CommToolSettings());
            Kind = s.Kind;
            PortName = s.PortName;
            BaudRate = s.BaudRate;
            DataBits = s.DataBits;
            Parity = s.Parity;
            StopBits = s.StopBits;
            Handshake = s.Handshake;
            TcpHost = s.TcpHost;
            TcpPort = s.TcpPort;
            ServerAddress = s.ServerAddress;
            ServerPort = s.ServerPort;
            UdpLocalPort = s.UdpLocalPort;
            UdpRemoteHost = s.UdpRemoteHost;
            UdpRemotePort = s.UdpRemotePort;
            InputMode = s.InputMode;
            Encoding = s.Encoding;
            Escapes = s.Escapes;
            Check = s.Check;
            CheckOrder = s.CheckOrder;
            AppendCr = s.AppendCr;
            AppendLf = s.AppendLf;
            ViewMode = s.ViewMode;
            ShowDelta = s.ShowDelta;
            AutoScroll = s.AutoScroll;
            VerifyReceived = s.VerifyReceived;
            GapMs = s.GapMs;
            OnlyMatches = s.OnlyMatches;
            LoadFeatureSettings(s);
            _assembler.GapMs = Math.Clamp(s.GapMs, 0, 5000);
            foreach (var item in s.History.Take(MaxHistory))
            {
                History.Add(item);
            }
        }
        catch (Exception ex)
        {
            _log.Warn("통신 테스트 설정을 읽지 못해 기본값을 씁니다.", ex);
        }
        finally
        {
            _loading = false;
        }
    }

    /// <summary>입력칸(주소·포트 번호)은 연결할 때 저장합니다.</summary>
    partial void OnIsConnectedChanged(bool value)
    {
        if (value)
        {
            SaveSettings();
        }
    }

    private void SaveSettings()
    {
        if (_loading)
        {
            return;
        }

        var settings = new CommToolSettings
        {
            Kind = Kind,
            PortName = PortName,
            BaudRate = BaudRate,
            DataBits = DataBits,
            Parity = Parity,
            StopBits = StopBits,
            Handshake = Handshake,
            TcpHost = TcpHost,
            TcpPort = TcpPort,
            ServerAddress = ServerAddress,
            ServerPort = ServerPort,
            UdpLocalPort = UdpLocalPort,
            UdpRemoteHost = UdpRemoteHost,
            UdpRemotePort = UdpRemotePort,
            InputMode = InputMode,
            Encoding = Encoding,
            Escapes = Escapes,
            Check = Check,
            CheckOrder = CheckOrder,
            AppendCr = AppendCr,
            AppendLf = AppendLf,
            ViewMode = ViewMode,
            ShowDelta = ShowDelta,
            AutoScroll = AutoScroll,
            VerifyReceived = VerifyReceived,
            GapMs = GapMs,
            OnlyMatches = OnlyMatches,
            History = [.. History],
        };
        SaveFeatureSettings(settings);
        _settings.Set(SettingsKey, settings);
    }

    public void Dispose()
    {
        StopAutoSending();
        _drainTimer.Stop();
        _portTimer.Stop();
        if (_channel is { } channel)
        {
            channel.Dispose();
            _channel = null;
        }

        _assembler.Dispose();
        SaveSettings();
    }

    private sealed record Incoming(CommDirection Direction, string Peer, byte[] Data, DateTime Time, string? Message);
}
