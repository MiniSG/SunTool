using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Drawing;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Windows;
using System.Windows.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.Core;
using SunTool.Core.Logging;
using SunTool.Core.Settings;
using SunTool.Modules.ScreenCapture.Logic;
using SunTool.Modules.ScreenCapture.Views;
using SunTool.Wpf.Services;

namespace SunTool.Modules.ScreenCapture.ViewModels;

/// <summary>전체 화면 캡처 대상 선택지. <see cref="Index"/> 가 -1 이면 모든 모니터.</summary>
public sealed record CaptureMonitorTarget(int Index, string Text)
{
    public override string ToString() => Text;
}

/// <summary>설정 파일의 "ScreenCapture" 항목.</summary>
public sealed class ScreenCaptureSettings
{
    public string? SaveFolder { get; set; }

    public string FileNamePattern { get; set; } = CaptureFileName.DefaultPattern;

    public bool SaveToFile { get; set; } = true;

    public bool CopyToClipboard { get; set; } = true;

    public bool OpenFolderAfter { get; set; }

    public int DelaySeconds { get; set; }
}

/// <summary>
/// 화면 캡처: 전체 화면·활성 창·목록에서 고른 창·드래그한 영역을 떠서 PNG 로 저장하거나 클립보드에 복사합니다.
/// 화면 내용을 읽기만 하며 다른 프로그램을 건드리지 않습니다.
/// </summary>
public sealed partial class ScreenCaptureViewModel : ObservableObject
{
    private const string SettingsKey = "ScreenCapture";

    private readonly IAppLogger _log;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly int _selfPid;
    private bool _loading;
    private Rectangle? _lastRegion;

    public ScreenCaptureViewModel(ILogService logs, IDialogService dialogs, ISettingsService settings)
    {
        _log = logs.For("ScreenCapture");
        _dialogs = dialogs;
        _settings = settings;
        _selfPid = Environment.ProcessId;

        LoadSettings();
        RefreshTargets();
    }

    // ===================================================================== 목록·선택지

    public ObservableCollection<CaptureMonitorTarget> MonitorTargets { get; } = [];

    public ObservableCollection<WindowInfo> Windows { get; } = [];

    public IReadOnlyList<int> DelayChoices { get; } = [0, 3, 5];

    [ObservableProperty]
    private CaptureMonitorTarget? _selectedMonitor;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CapturePickedWindowCommand))]
    private WindowInfo? _selectedWindow;

    [ObservableProperty]
    private int _delaySeconds;

    // ===================================================================== 처리 설정

    [ObservableProperty]
    private string? _saveFolder;

    [ObservableProperty]
    private string _fileNamePattern = CaptureFileName.DefaultPattern;

    [ObservableProperty]
    private bool _saveToFile = true;

    [ObservableProperty]
    private bool _copyToClipboard = true;

    [ObservableProperty]
    private bool _openFolderAfter;

    // ===================================================================== 상태·결과

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CaptureFullScreenCommand), nameof(CaptureActiveWindowCommand),
        nameof(CapturePickedWindowCommand), nameof(CaptureRegionCommand), nameof(CaptureLastRegionCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private BitmapSource? _lastImage;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenLastFileCommand))]
    private string? _lastSavePath;

    [ObservableProperty]
    private string _lastSizeText = string.Empty;

    [ObservableProperty]
    private string _statusText = Loc.T("캡처 방식을 고르세요. 결과는 설정에 따라 PNG 로 저장하거나 클립보드에 복사합니다.");

    [ObservableProperty]
    private bool _statusIsError;

    private bool CanCapture() => !IsBusy;

    // ===================================================================== 캡처 명령

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private Task CaptureFullScreenAsync()
    {
        var monitors = MonitorEnumerator.List();
        var target = SelectedMonitor?.Index ?? -1;
        var region = target >= 0 && target < monitors.Count
            ? monitors[target].Bounds
            : MonitorEnumerator.VirtualScreen(monitors);
        // 전체 화면·영역 캡처는 SunTool 창을 잠시 숨겨 화면에서 빠지게 합니다.
        return CaptureAsync(() => ScreenCapturer.CaptureRegion(region), hideMainWindow: true);
    }

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private Task CaptureActiveWindowAsync()
    {
        return CaptureAsync(() =>
        {
            var self = SelfWindowHandle();
            var window = WindowEnumerator.ForegroundOrPrevious(self, _selfPid)
                         ?? throw new InvalidOperationException(Loc.T("캡처할 활성 창을 찾지 못했습니다."));
            return ScreenCapturer.CaptureWindow(window);
        }, hideMainWindow: false);
    }

    private bool CanCapturePicked() => !IsBusy && SelectedWindow is not null;

    [RelayCommand(CanExecute = nameof(CanCapturePicked))]
    private Task CapturePickedWindowAsync()
    {
        var window = SelectedWindow!;
        return CaptureAsync(() => ScreenCapturer.CaptureWindow(window), hideMainWindow: false);
    }

    [RelayCommand(CanExecute = nameof(CanCapture))]
    private Task CaptureRegionAsync()
    {
        // 가상 화면을 먼저 떠서 그 위에서 영역을 고르고 잘라냅니다 (DPI·멀티모니터에 강함).
        return CaptureAsync(() =>
        {
            var (image, region) = RegionSelectWindow.CaptureRegionInteractive();
            if (image is not null)
            {
                SetLastRegion(region);
            }

            return image;
        }, hideMainWindow: true, isRegion: true);
    }

    private bool CanCaptureLastRegion() => !IsBusy && _lastRegion is not null;

    /// <summary>직전에 고른 영역을 그대로 다시 캡처합니다. (지정된 영역이 없으면 비활성화)</summary>
    [RelayCommand(CanExecute = nameof(CanCaptureLastRegion))]
    private Task CaptureLastRegionAsync()
    {
        var last = _lastRegion!.Value;
        return CaptureAsync(() => ScreenCapturer.CaptureRegion(last), hideMainWindow: true);
    }

    private void SetLastRegion(Rectangle region)
    {
        _lastRegion = region;
        CaptureLastRegionCommand.NotifyCanExecuteChanged();
    }

    // ===================================================================== 캡처 공통 흐름

    private async Task CaptureAsync(Func<Bitmap?> capture, bool hideMainWindow, bool isRegion = false)
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        var main = Application.Current?.MainWindow;
        try
        {
            if (DelaySeconds > 0)
            {
                SetStatus(Loc.F("{0}초 뒤에 캡처합니다…", DelaySeconds));
                await Task.Delay(TimeSpan.FromSeconds(DelaySeconds)).ConfigureAwait(true);
            }

            if (hideMainWindow && main is not null)
            {
                main.Hide();
                // 창이 실제로 화면에서 사라질 시간을 잠깐 줍니다.
                await Task.Delay(180).ConfigureAwait(true);
            }

            Bitmap? bitmap;
            try
            {
                bitmap = capture();
            }
            finally
            {
                if (hideMainWindow && main is not null && !isRegion)
                {
                    main.Show();
                }
            }

            // 영역 캡처는 선택 오버레이가 닫힌 뒤 떠야 하므로 캡처까지 창을 숨겨 두었다가 여기서 되돌립니다.
            if (hideMainWindow && main is not null && isRegion)
            {
                main.Show();
            }

            if (bitmap is null)
            {
                SetStatus(Loc.T("캡처를 취소했습니다."));
                return;
            }

            using (bitmap)
            {
                Deliver(bitmap);
            }
        }
        catch (Exception ex)
        {
            if (main is not null && !main.IsVisible)
            {
                main.Show();
            }

            SetStatus(Loc.F("캡처하지 못했습니다.\n\n{0}", ex.Message), isError: true);
            _log.Error("화면 캡처 실패", ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Deliver(Bitmap bitmap)
    {
        LastImage = BitmapConvert.ToBitmapSource(bitmap);
        LastSizeText = Loc.F("{0} × {1} 픽셀", bitmap.Width, bitmap.Height);
        LastSavePath = null;

        var parts = new List<string>();

        if (SaveToFile)
        {
            try
            {
                var folder = EnsureSaveFolder();
                var name = CaptureFileName.Unique(
                    CaptureFileName.Format(FileNamePattern, DateTime.Now, BuildTokens()),
                    candidate => File.Exists(Path.Combine(folder, candidate)));
                var path = Path.Combine(folder, name);
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
                LastSavePath = path;
                parts.Add(Loc.F("저장: {0}", path));

                if (OpenFolderAfter)
                {
                    Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _log.Error("캡처 PNG 저장 실패", ex);
                SetStatus(Loc.F("이미지를 저장하지 못했습니다.\n\n{0}", ex.Message), isError: true);
                return;
            }
        }

        if (CopyToClipboard)
        {
            try
            {
                Clipboard.SetImage(LastImage);
                parts.Add(Loc.T("클립보드에 복사"));
            }
            catch (Exception ex)
            {
                _log.Warn("클립보드 복사 실패", ex);
                parts.Add(Loc.T("클립보드 복사 실패"));
            }
        }

        if (parts.Count == 0)
        {
            parts.Add(Loc.T("미리보기만 함 (저장·클립보드 모두 꺼짐)"));
        }

        OpenLastFileCommand.NotifyCanExecuteChanged();
        SetStatus(Loc.F("캡처 완료 · {0} · {1}", LastSizeText, string.Join(" · ", parts)));
        _log.Info($"화면 캡처: {LastSizeText}, 저장={SaveToFile}, 클립보드={CopyToClipboard}");
    }

    // ===================================================================== 보조 명령

    [RelayCommand]
    private void RefreshTargets()
    {
        var keepMonitor = SelectedMonitor?.Index ?? -1;
        MonitorTargets.Clear();
        MonitorTargets.Add(new CaptureMonitorTarget(-1, Loc.T("전체 화면 (모든 모니터)")));
        foreach (var m in MonitorEnumerator.List())
        {
            MonitorTargets.Add(new CaptureMonitorTarget(m.Index, m.DisplayText));
        }

        SelectedMonitor = MonitorTargets.FirstOrDefault(t => t.Index == keepMonitor) ?? MonitorTargets[0];
        RefreshWindows();
    }

    [RelayCommand]
    private void RefreshWindows()
    {
        var keep = SelectedWindow?.Handle;
        Windows.Clear();
        foreach (var w in WindowEnumerator.List(_selfPid))
        {
            Windows.Add(w);
        }

        SelectedWindow = Windows.FirstOrDefault(w => w.Handle == keep) ?? Windows.FirstOrDefault();
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var picked = _dialogs.PickFolder(Loc.T("캡처 저장 폴더 고르기"), SaveFolder);
        if (picked is not null)
        {
            SaveFolder = picked;
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        try
        {
            var folder = EnsureSaveFolder();
            Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            _log.Warn("저장 폴더 열기 실패", ex);
        }
    }

    private bool CanOpenLastFile() => LastSavePath is not null && File.Exists(LastSavePath);

    [RelayCommand(CanExecute = nameof(CanOpenLastFile))]
    private void OpenLastFile()
    {
        if (LastSavePath is not null && File.Exists(LastSavePath))
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{LastSavePath}\"") { UseShellExecute = true });
        }
    }

    // ===================================================================== 주석본 (미리보기에 그린 박스·텍스트 포함)

    /// <summary>미리보기에 주석을 올려 합친 이미지를 PNG 로 저장합니다. (View 가 렌더한 이미지를 받음)</summary>
    public void SaveComposed(BitmapSource image)
    {
        try
        {
            var folder = EnsureSaveFolder();
            var name = CaptureFileName.Unique(
                CaptureFileName.Format(FileNamePattern, DateTime.Now, BuildTokens()),
                candidate => File.Exists(Path.Combine(folder, candidate)));
            var path = Path.Combine(folder, name);

            using (var fs = File.Create(path))
            {
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(image));
                encoder.Save(fs);
            }

            LastSavePath = path;
            OpenLastFileCommand.NotifyCanExecuteChanged();
            if (OpenFolderAfter)
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
            }

            SetStatus(Loc.F("주석본을 저장했습니다: {0}", path));
            _log.Info("주석본 저장");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.Error("주석본 저장 실패", ex);
            SetStatus(Loc.F("주석본을 저장하지 못했습니다.\n\n{0}", ex.Message), isError: true);
        }
    }

    /// <summary>미리보기에 주석을 올려 합친 이미지를 클립보드에 복사합니다.</summary>
    public void CopyComposed(BitmapSource image)
    {
        try
        {
            Clipboard.SetImage(image);
            SetStatus(Loc.T("주석본을 클립보드에 복사했습니다."));
        }
        catch (Exception ex)
        {
            _log.Warn("주석본 클립보드 복사 실패", ex);
            SetStatus(Loc.T("주석본을 클립보드에 복사하지 못했습니다."), isError: true);
        }
    }

    // ===================================================================== 보조

    private static IntPtr SelfWindowHandle()
    {
        var window = Application.Current?.MainWindow;
        return window is null ? IntPtr.Zero : new System.Windows.Interop.WindowInteropHelper(window).Handle;
    }

    private string EnsureSaveFolder()
    {
        var configured = string.IsNullOrWhiteSpace(SaveFolder) ? DefaultSaveFolder() : SaveFolder!;
        if (TryCreateFolder(configured))
        {
            return configured;
        }

        // 설정/기본 폴더를 만들 수 없는 환경(예: Pictures 가 OneDrive 로 옮겨져 실제로는 없음)에서는
        // 확실히 쓸 수 있는 포터블 데이터 폴더로 대체하고, 설정도 그 폴더로 바꿉니다.
        var fallback = FallbackSaveFolder();
        Directory.CreateDirectory(fallback);
        _log.Warn($"저장 폴더를 만들 수 없어 대체 위치로 바꿉니다: {configured} → {fallback}");
        SaveFolder = fallback;
        SetStatus(Loc.F("저장 폴더를 만들 수 없어 '{0}' 로 바꿨습니다.", fallback), isError: true);
        return fallback;
    }

    private static bool TryCreateFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
        {
            return false;
        }

        try
        {
            Directory.CreateDirectory(folder);
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }

    private static string DefaultSaveFolder()
    {
        // Create 옵션은 알려진 폴더(OneDrive 로 옮겨진 경우 포함)를 실제로 만들어 현재 경로를 돌려줍니다.
        var pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures, Environment.SpecialFolderOption.Create);
        if (string.IsNullOrWhiteSpace(pictures))
        {
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        }

        return string.IsNullOrWhiteSpace(pictures) ? FallbackSaveFolder() : Path.Combine(pictures, "SunTool");
    }

    /// <summary>항상 쓸 수 있는 대체 저장 폴더 (포터블 데이터 폴더 아래).</summary>
    private static string FallbackSaveFolder() => Path.Combine(AppPaths.DataDirectory, "Captures");

    // ===================================================================== 파일명 토큰

    /// <summary>파일명 규칙에 토큰을 덧붙입니다 (예: {host}, {ip}, {user}).</summary>
    [RelayCommand]
    private void InsertToken(string? token)
    {
        if (!string.IsNullOrEmpty(token))
        {
            FileNamePattern = (FileNamePattern ?? string.Empty) + token;
        }
    }

    /// <summary>{host}=컴퓨터명, {ip}=이 PC 의 IPv4, {user}=사용자명.</summary>
    private static Dictionary<string, string> BuildTokens() => new()
    {
        ["host"] = Environment.MachineName,
        ["ip"] = LocalIpv4(),
        ["user"] = Environment.UserName,
    };

    /// <summary>이 PC 의 대표 IPv4 주소 (없으면 0.0.0.0). 바깥으로 연결하지 않고 어댑터만 읽습니다.</summary>
    private static string LocalIpv4()
    {
        try
        {
            var address = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(n => n.GetIPProperties().UnicastAddresses)
                .Select(a => a.Address)
                .FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a));
            return address?.ToString() ?? "0.0.0.0";
        }
        catch (NetworkInformationException)
        {
            return "0.0.0.0";
        }
    }

    // ===================================================================== 설정

    partial void OnDelaySecondsChanged(int value) => SaveSettings();

    partial void OnSaveFolderChanged(string? value) => SaveSettings();

    partial void OnFileNamePatternChanged(string value) => SaveSettings();

    partial void OnSaveToFileChanged(bool value) => SaveSettings();

    partial void OnCopyToClipboardChanged(bool value) => SaveSettings();

    partial void OnOpenFolderAfterChanged(bool value) => SaveSettings();

    private void LoadSettings()
    {
        _loading = true;
        try
        {
            var s = _settings.Get(SettingsKey, new ScreenCaptureSettings());
            SaveFolder = s.SaveFolder;
            FileNamePattern = string.IsNullOrWhiteSpace(s.FileNamePattern) ? CaptureFileName.DefaultPattern : s.FileNamePattern;
            SaveToFile = s.SaveToFile;
            CopyToClipboard = s.CopyToClipboard;
            OpenFolderAfter = s.OpenFolderAfter;
            DelaySeconds = DelayChoices.Contains(s.DelaySeconds) ? s.DelaySeconds : 0;
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

        _settings.Set(SettingsKey, new ScreenCaptureSettings
        {
            SaveFolder = SaveFolder,
            FileNamePattern = FileNamePattern,
            SaveToFile = SaveToFile,
            CopyToClipboard = CopyToClipboard,
            OpenFolderAfter = OpenFolderAfter,
            DelaySeconds = DelaySeconds,
        });
        _settings.Save();
    }

    private void SetStatus(string text, bool isError = false)
    {
        StatusText = text;
        StatusIsError = isError;
    }
}
