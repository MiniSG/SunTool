using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.Modules.Comm.Logic;

namespace SunTool.Modules.Comm.ViewModels;

/// <summary>2단계(수신 묶기 방식, 반복 보내기, 파일 보내기, 빠른 명령) + 3단계(Steal 중계).</summary>
public sealed partial class CommToolViewModel
{
    private readonly Dictionary<string, CommDirection> _relayPeers = new(StringComparer.Ordinal);
    private CancellationTokenSource? _repeatCts;
    private CancellationTokenSource? _fileCts;

    // ===================================================================== 수신 묶기 방식

    public IReadOnlyList<Option<FrameMode>> FrameModes { get; } =
    [
        new(FrameMode.Gap, Loc.T("시간 간격")),
        new(FrameMode.EndMarker, Loc.T("끝 문자")),
        new(FrameMode.StartEnd, Loc.T("시작·끝 문자")),
        new(FrameMode.FixedLength, Loc.T("고정 길이")),
    ];

    [ObservableProperty]
    private FrameMode _frameMode = FrameMode.Gap;

    /// <summary>시작 바이트 (HEX, 예: "02").</summary>
    [ObservableProperty]
    private string _frameStart = "02";

    /// <summary>끝 바이트 (HEX, 예: "0D 0A", "03").</summary>
    [ObservableProperty]
    private string _frameEnd = "0D 0A";

    [ObservableProperty]
    private int _frameLength = 8;

    [ObservableProperty]
    private string _frameError = string.Empty;

    partial void OnFrameModeChanged(FrameMode value) => ApplyFraming();

    partial void OnFrameStartChanged(string value) => ApplyFraming();

    partial void OnFrameEndChanged(string value) => ApplyFraming();

    partial void OnFrameLengthChanged(int value) => ApplyFraming();

    /// <summary>묶기 설정을 받기 처리에 적용. 입력이 잘못되면 시간 간격으로 동작하고 이유를 보여 줍니다.</summary>
    private void ApplyFraming()
    {
        var start = TryHex(FrameStart, out var s) ? s : null;
        var end = TryHex(FrameEnd, out var e) ? e : null;
        FrameError = FrameMode switch
        {
            FrameMode.EndMarker when end is not { Length: > 0 } => Loc.T("끝 바이트를 HEX 로 입력하세요 (예: 0D 0A)"),
            FrameMode.StartEnd when start is not { Length: > 0 } || end is not { Length: > 0 } => Loc.T("시작·끝 바이트를 HEX 로 입력하세요 (예: 02 / 03)"),
            FrameMode.FixedLength when FrameLength <= 0 => Loc.T("길이는 1 이상이어야 합니다"),
            _ => string.Empty,
        };

        _assembler.Settings = new FrameSettings(FrameMode, Math.Clamp(GapMs, 0, 5000), start ?? [], end ?? [], FrameLength);
        SaveSettings();

        static bool TryHex(string text, out byte[] bytes)
        {
            try
            {
                bytes = ByteCodec.ParseHex(text ?? string.Empty);
                return true;
            }
            catch (FormatException)
            {
                bytes = [];
                return false;
            }
        }
    }

    // ===================================================================== 반복 보내기

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SendButtonText))]
    private bool _repeatEnabled;

    [ObservableProperty]
    private int _repeatIntervalMs = 1000;

    /// <summary>반복 횟수 (0 = 멈출 때까지).</summary>
    [ObservableProperty]
    private int _repeatCount;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SendButtonText), nameof(IsAutoSending))]
    private bool _isRepeating;

    [ObservableProperty]
    private string _autoSendStatus = string.Empty;

    public string SendButtonText => IsRepeating ? Loc.T("반복 중지") : RepeatEnabled ? Loc.T("반복 시작") : Loc.T("보내기");

    public bool IsAutoSending => IsRepeating || IsFileSending;

    partial void OnRepeatEnabledChanged(bool value) => SaveSettings();

    partial void OnRepeatIntervalMsChanged(int value) => SaveSettings();

    partial void OnRepeatCountChanged(int value) => SaveSettings();

    private async Task StartRepeatAsync()
    {
        if (_channel is null)
        {
            SetStatus(Loc.T("먼저 [연결]을 누르세요."), isError: true);
            return;
        }

        var text = SendText;
        var mode = InputMode;
        var interval = Math.Max(10, RepeatIntervalMs);
        var total = Math.Max(0, RepeatCount);
        _repeatCts = new CancellationTokenSource();
        var token = _repeatCts.Token;
        IsRepeating = true;
        AddHistory(text);
        var sent = 0;
        try
        {
            while (!token.IsCancellationRequested && (total == 0 || sent < total))
            {
                if (!await SendTextAsync(text, mode))
                {
                    break;
                }

                sent++;
                AutoSendStatus = total == 0
                    ? Loc.F("반복 보내기 {0}회 (간격 {1} ms)", sent, interval)
                    : Loc.F("반복 보내기 {0} / {1}회 (간격 {2} ms)", sent, total, interval);
                if (total != 0 && sent >= total)
                {
                    break;
                }

                await Task.Delay(interval, token);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsRepeating = false;
            _repeatCts?.Dispose();
            _repeatCts = null;
            AutoSendStatus = Loc.F("반복 보내기 끝: {0}회 보냄", sent);
        }
    }

    /// <summary>반복 보내기·파일 보내기를 멈춤 (연결 끊을 때도).</summary>
    private void StopAutoSending()
    {
        _repeatCts?.Cancel();
        _fileCts?.Cancel();
    }

    // ===================================================================== 파일 보내기

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSendFile), nameof(SendFileName))]
    private string? _sendFilePath;

    private List<string> _fileLines = [];

    [ObservableProperty]
    private int _fileLineCount;

    [ObservableProperty]
    private int _fileStartLine = 1;

    [ObservableProperty]
    private int _fileEndLine = 1;

    [ObservableProperty]
    private int _fileIntervalMs = 500;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(FileButtonText), nameof(IsAutoSending))]
    private bool _isFileSending;

    public bool HasSendFile => !string.IsNullOrEmpty(SendFilePath);

    public string SendFileName => HasSendFile ? Path.GetFileName(SendFilePath!) : Loc.T("(파일을 고르세요)");

    public string FileButtonText => IsFileSending ? Loc.T("파일 보내기 중지") : Loc.T("파일 보내기 시작");

    partial void OnFileIntervalMsChanged(int value) => SaveSettings();

    [RelayCommand]
    private void OpenSendFile()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = Loc.T("보낼 파일 고르기 (한 줄 = 한 번 보내기)"),
            Filter = Loc.T("텍스트 파일 (*.txt)|*.txt|모든 파일 (*.*)|*.*"),
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            _fileLines = [.. File.ReadAllLines(dialog.FileName)];
            SendFilePath = dialog.FileName;
            FileLineCount = _fileLines.Count;
            FileStartLine = 1;
            FileEndLine = Math.Max(1, _fileLines.Count);
            AutoSendStatus = Loc.F("파일 {0}: {1}줄 (빈 줄은 건너뜀)", SendFileName, FileLineCount);
        }
        catch (Exception ex)
        {
            SetStatus(Loc.F("파일을 읽지 못했습니다: {0}", ex.Message), isError: true);
        }
    }

    // 실행 중(반복·파일 보내기)에도 같은 버튼으로 멈출 수 있어야 함
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task ToggleFileSendAsync()
    {
        if (IsFileSending)
        {
            _fileCts?.Cancel();
            return;
        }

        if (_fileLines.Count == 0)
        {
            SetStatus(Loc.T("보낼 파일이 없습니다. [파일 열기]로 고르세요."), isError: true);
            return;
        }

        if (FileStartLine < 1 || FileEndLine < FileStartLine || FileEndLine > _fileLines.Count)
        {
            SetStatus(Loc.F("줄 범위를 확인하세요 (1 ~ {0}).", _fileLines.Count), isError: true);
            return;
        }

        if (_channel is null)
        {
            SetStatus(Loc.T("먼저 [연결]을 누르세요."), isError: true);
            return;
        }

        var lines = Enumerable.Range(FileStartLine, FileEndLine - FileStartLine + 1)
            .Select(n => (Number: n, Text: _fileLines[n - 1]))
            .Where(l => !string.IsNullOrWhiteSpace(l.Text))
            .ToList();
        var mode = InputMode;
        var interval = Math.Max(10, FileIntervalMs);
        _fileCts = new CancellationTokenSource();
        var token = _fileCts.Token;
        IsFileSending = true;
        var sent = 0;
        try
        {
            foreach (var (number, text) in lines)
            {
                token.ThrowIfCancellationRequested();
                if (!await SendTextAsync(text, mode))
                {
                    break;
                }

                sent++;
                AutoSendStatus = Loc.F("파일 보내기 {0} / {1} (줄 {2})", sent, lines.Count, number);
                if (sent < lines.Count)
                {
                    await Task.Delay(interval, token);
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            IsFileSending = false;
            _fileCts?.Dispose();
            _fileCts = null;
            AutoSendStatus = Loc.F("파일 보내기 끝: {0} / {1}줄 보냄", sent, lines.Count);
        }
    }

    // ===================================================================== 빠른 명령

    public ObservableCollection<QuickCommand> QuickCommands { get; } = [];

    /// <summary>지금 입력한 내용을 빠른 명령 버튼으로 저장.</summary>
    [RelayCommand]
    private void AddQuickCommand()
    {
        if (string.IsNullOrEmpty(SendText))
        {
            SetStatus(Loc.T("저장할 내용을 보내기 칸에 먼저 입력하세요."), isError: true);
            return;
        }

        var suggested = SendText.Length > 16 ? SendText[..16] + "…" : SendText;
        var name = _dialogs.Prompt(Loc.T("버튼 이름을 입력하세요."), Loc.T("빠른 명령 저장"), suggested);
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        QuickCommands.Add(new QuickCommand { Name = name.Trim(), Text = SendText, Mode = InputMode });
        SaveSettings();
    }

    [RelayCommand]
    private async Task SendQuickCommandAsync(QuickCommand? command)
    {
        if (command is not null)
        {
            await SendTextAsync(command.Text, command.Mode);
        }
    }

    /// <summary>빠른 명령 내용을 보내기 칸으로 가져오기 (고쳐서 보낼 때).</summary>
    [RelayCommand]
    private void LoadQuickCommand(QuickCommand? command)
    {
        if (command is not null)
        {
            InputMode = command.Mode;
            SendText = command.Text;
        }
    }

    [RelayCommand]
    private void RemoveQuickCommand(QuickCommand? command)
    {
        if (command is not null && _dialogs.Confirm(Loc.F("빠른 명령 '{0}' 을(를) 지울까요?", command.Name)))
        {
            QuickCommands.Remove(command);
            SaveSettings();
        }
    }

    // ===================================================================== Steal (중계)

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(InjectSides))]
    private string? _portName2;

    /// <summary>전달하지 않고 기록만 (Y 케이블로 엿볼 때).</summary>
    [ObservableProperty]
    private bool _listenOnly;

    /// <summary>중계 중 직접 보내기 대상: 1 = 장비 1 쪽, 2 = 장비 2 쪽.</summary>
    [ObservableProperty]
    private int _injectSide = 1;

    [ObservableProperty]
    private bool _showRelay1To2 = true;

    [ObservableProperty]
    private bool _showRelay2To1 = true;

    public bool IsRelayKind => Kind == ConnectionKind.Relay;

    public IReadOnlyList<Option<int>> InjectSides =>
    [
        new(1, Loc.F("장비 1 ({0}) 로", PortName ?? "?")),
        new(2, Loc.F("장비 2 ({0}) 로", PortName2 ?? "?")),
    ];

    partial void OnPortName2Changed(string? value) => SaveSettings();

    partial void OnListenOnlyChanged(bool value) => SaveSettings();

    partial void OnInjectSideChanged(int value) => SaveSettings();

    partial void OnShowRelay1To2Changed(bool value) => RefreshFilterAndCount();

    partial void OnShowRelay2To1Changed(bool value) => RefreshFilterAndCount();

    private ICommChannel CreateRelayChannel()
    {
        if (string.IsNullOrWhiteSpace(PortName) || string.IsNullOrWhiteSpace(PortName2))
        {
            throw new InvalidOperationException(Loc.T("장비 1 · 장비 2 쪽 COM 포트를 모두 고르세요."));
        }

        if (string.Equals(PortName, PortName2, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(Loc.T("장비 1 과 장비 2 는 서로 다른 COM 포트여야 합니다."));
        }

        foreach (var port in new[] { PortName, PortName2 })
        {
            if (!_portNames.Contains(port, StringComparer.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(Loc.F("{0} 은(는) 지금 PC 에 연결되어 있지 않습니다.", port));
            }
        }

        var side1 = new SerialChannel(new SerialSettings(PortName, BaudRate, DataBits, Parity, StopBits, Handshake));
        var side2 = new SerialChannel(new SerialSettings(PortName2, BaudRate, DataBits, Parity, StopBits, Handshake));
        _relayPeers.Clear();
        _relayPeers[$"{PortName} → {PortName2}"] = CommDirection.Relay1To2;
        _relayPeers[$"{PortName2} → {PortName}"] = CommDirection.Relay2To1;
        return new RelayChannel(side1, PortName, side2, PortName2, ListenOnly);
    }

    private void OnRelayed(int side, byte[] data)
    {
        var (from, to) = side == 1 ? (PortName, PortName2) : (PortName2, PortName);
        _assembler.Add($"{from} → {to}", data, DateTime.Now);
    }

    /// <summary>묶어서 나온 줄의 방향 (Steal 이면 1→2 / 2→1, 그 밖에는 수신).</summary>
    private CommDirection DirectionOfPeer(string peer) =>
        _relayPeers.TryGetValue(peer, out var direction) ? direction : CommDirection.Received;

    // ===================================================================== 설정

    private void LoadFeatureSettings(CommToolSettings s)
    {
        FrameMode = s.FrameMode;
        FrameStart = s.FrameStart;
        FrameEnd = s.FrameEnd;
        FrameLength = s.FrameLength;
        RepeatEnabled = s.RepeatEnabled;
        RepeatIntervalMs = s.RepeatIntervalMs;
        RepeatCount = s.RepeatCount;
        FileIntervalMs = s.FileIntervalMs;
        PortName2 = s.PortName2;
        ListenOnly = s.ListenOnly;
        InjectSide = s.InjectSide is 1 or 2 ? s.InjectSide : 1;
        foreach (var command in s.QuickCommands)
        {
            QuickCommands.Add(command);
        }
    }

    private void SaveFeatureSettings(CommToolSettings s)
    {
        s.FrameMode = FrameMode;
        s.FrameStart = FrameStart;
        s.FrameEnd = FrameEnd;
        s.FrameLength = FrameLength;
        s.RepeatEnabled = RepeatEnabled;
        s.RepeatIntervalMs = RepeatIntervalMs;
        s.RepeatCount = RepeatCount;
        s.FileIntervalMs = FileIntervalMs;
        s.PortName2 = PortName2;
        s.ListenOnly = ListenOnly;
        s.InjectSide = InjectSide;
        s.QuickCommands = [.. QuickCommands];
    }
}
