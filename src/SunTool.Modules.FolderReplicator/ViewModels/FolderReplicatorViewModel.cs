using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.Core.Common;
using SunTool.Core.FileSystem;
using SunTool.Core.Logging;
using SunTool.Core.Settings;
using SunTool.Core.Text;
using SunTool.Modules.FolderReplicator.Logic;
using SunTool.Wpf.Services;

namespace SunTool.Modules.FolderReplicator.ViewModels;

public sealed partial class FolderReplicatorViewModel : ObservableObject
{
    private const string SettingsKey = "FolderReplicator";

    private readonly IAppLogger _log;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly List<FolderNodeViewModel> _allNodes = new();
    private CancellationTokenSource? _cts;
    private FolderNode? _rootModel;
    private bool _loadingSettings;
    private int _selectedCount;

    public FolderReplicatorViewModel(ILogService logs, IDialogService dialogs, ISettingsService settings)
    {
        _log = logs.For("FolderReplicator");
        _dialogs = dialogs;
        _settings = settings;
        LoadSettings();
    }

    public ObservableCollection<FolderNodeViewModel> RootNodes { get; } = new();

    public IReadOnlyList<EnumOption<ConflictPolicy>> ConflictPolicies { get; } =
    [
        new(ConflictPolicy.Skip, "건너뛰기 (기존 파일 유지)"),
        new(ConflictPolicy.Overwrite, "덮어쓰기"),
        new(ConflictPolicy.OverwriteIfNewer, "원본이 더 최신일 때만 덮어쓰기"),
        new(ConflictPolicy.KeepBoth, "둘 다 유지 (이름 (2) 로 저장)"),
    ];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetPreview))]
    private string? _sourcePath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetPreview))]
    private string? _destinationPath;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TargetPreview))]
    private bool _createRootFolder = true;

    [ObservableProperty]
    private ConflictPolicy _conflictPolicy = ConflictPolicy.Skip;

    [ObservableProperty]
    private bool _skipHidden = true;

    [ObservableProperty]
    private string _extensionFilter = string.Empty;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private bool _isProgressIndeterminate;

    [ObservableProperty]
    private string _statusText = "원본 폴더를 끌어다 놓으세요.";

    [ObservableProperty]
    private string _selectionSummary = "선택된 폴더 없음";

    [ObservableProperty]
    private string? _planSummary;

    /// <summary>실제로 만들어질 최상위 경로 미리보기.</summary>
    public string TargetPreview
    {
        get
        {
            if (string.IsNullOrWhiteSpace(DestinationPath))
            {
                return "목적 폴더를 지정하세요.";
            }

            if (_rootModel is null || !CreateRootFolder)
            {
                return $"생성 위치: {DestinationPath}";
            }

            return $"생성 위치: {Path.Combine(DestinationPath, _rootModel.Name)}";
        }
    }

    // ===== 속성 변경 =====

    partial void OnIsBusyChanged(bool value) => RefreshCommandStates();

    partial void OnSourcePathChanged(string? value) => RefreshCommandStates();

    partial void OnDestinationPathChanged(string? value) => OnOptionChanged();

    partial void OnCreateRootFolderChanged(bool value) => OnOptionChanged();

    partial void OnConflictPolicyChanged(ConflictPolicy value) => OnOptionChanged();

    partial void OnSkipHiddenChanged(bool value) => OnOptionChanged();

    partial void OnExtensionFilterChanged(string value) => OnOptionChanged();

    private void OnOptionChanged()
    {
        PlanSummary = null;
        if (!_loadingSettings)
        {
            SaveSettings();
        }
    }

    // ===== 명령: 입력 =====

    private bool CanEdit() => !IsBusy;

    private bool CanRun() => !IsBusy && _rootModel is not null;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task DropSourceAsync(string[]? paths)
    {
        var folder = paths?.FirstOrDefault(Directory.Exists);
        if (folder is null)
        {
            _dialogs.ShowWarning("폴더를 끌어다 놓아 주세요. (파일은 원본으로 쓸 수 없습니다)");
            return;
        }

        if (paths!.Length > 1)
        {
            _log.Warn($"여러 항목이 드롭되어 첫 번째 폴더만 사용합니다: {folder}");
        }

        await LoadSourceAsync(folder);
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void DropDestination(string[]? paths)
    {
        var folder = paths?.FirstOrDefault(Directory.Exists);
        if (folder is null)
        {
            _dialogs.ShowWarning("목적지로 쓸 폴더를 끌어다 놓아 주세요.");
            return;
        }

        DestinationPath = folder;
        _log.Info($"목적 폴더: {folder}");
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task BrowseSourceAsync()
    {
        var folder = _dialogs.PickFolder("원본 폴더 선택", SourcePath);
        if (folder is not null)
        {
            await LoadSourceAsync(folder);
        }
    }

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void BrowseDestination()
    {
        var folder = _dialogs.PickFolder("목적 폴더 선택", DestinationPath);
        if (folder is not null)
        {
            DestinationPath = folder;
            _log.Info($"목적 폴더: {folder}");
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RescanAsync() => SourcePath is null ? Task.CompletedTask : LoadSourceAsync(SourcePath);

    // ===== 명령: 선택 =====

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var node in _allNodes)
        {
            node.IsChecked = true;
        }
    }

    [RelayCommand]
    private void ClearSelection()
    {
        foreach (var node in _allNodes)
        {
            node.IsChecked = false;
        }
    }

    [RelayCommand]
    private void ExpandAll()
    {
        foreach (var node in _allNodes)
        {
            node.IsExpanded = true;
        }
    }

    [RelayCommand]
    private void CollapseAll()
    {
        foreach (var node in _allNodes)
        {
            node.IsExpanded = node.Parent is null; // 루트만 펼친 상태로
        }
    }

    // ===== 명령: 실행 =====

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task PreviewAsync()
    {
        var plan = await BuildPlanAsync();
        if (plan is not null)
        {
            ShowPlan(plan);
            StatusText = "계산이 끝났습니다. 내용을 확인한 뒤 실행하세요.";
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ExecuteAsync()
    {
        var plan = await BuildPlanAsync();
        if (plan is null)
        {
            return;
        }

        ShowPlan(plan);
        var message =
            $"다음 위치에 폴더 구조를 만듭니다:\n{plan.DestinationBase}\n\n" +
            $"• 폴더 {plan.Folders.Count:N0}개 생성\n" +
            $"• 파일 {plan.Files.Count:N0}개 복사 ({SizeFormatter.Format(plan.TotalBytes)})\n\n계속할까요?";
        if (!_dialogs.Confirm(message, "폴더 구조 복제"))
        {
            return;
        }

        IsBusy = true;
        IsProgressIndeterminate = false;
        ProgressValue = 0;
        _cts = new CancellationTokenSource();
        _log.Info($"복제 시작: {_rootModel!.FullPath} → {plan.DestinationBase}");

        var progress = new Progress<ReplicationProgress>(p =>
        {
            ProgressValue = p.Percent;
            StatusText = p.FoldersDone < p.FoldersTotal
                ? $"폴더 생성 중... {p.FoldersDone:N0}/{p.FoldersTotal:N0}"
                : $"파일 복사 중... {p.FilesDone:N0}/{p.FilesTotal:N0} ({SizeFormatter.Format(p.BytesDone)} / {SizeFormatter.Format(p.BytesTotal)})";
        });

        try
        {
            var result = await ReplicationExecutor.ExecuteAsync(plan, ConflictPolicy, progress, _log, _cts.Token);
            var summary =
                $"폴더 {result.FoldersCreated:N0}개 생성(기존 {result.FoldersExisting:N0}), " +
                $"파일 {result.FilesCopied:N0}개 복사, {result.FilesSkipped:N0}개 건너뜀, 오류 {result.Errors:N0}건 " +
                $"({result.Elapsed.TotalSeconds:0.0}초)";

            if (result.Cancelled)
            {
                StatusText = "취소됨 — " + summary;
                _log.Warn("복제 취소: " + summary);
            }
            else
            {
                ProgressValue = 100;
                StatusText = "완료 — " + summary;
                _log.Info("복제 완료: " + summary);
                if (result.Errors > 0)
                {
                    _dialogs.ShowWarning($"작업은 끝났지만 오류가 {result.Errors}건 있었습니다.\n자세한 내용은 터미널(F12)에서 확인하세요.");
                }
                else
                {
                    _dialogs.ShowInfo("폴더 구조 복제가 완료되었습니다.\n\n" + summary);
                }
            }
        }
        catch (Exception ex)
        {
            StatusText = "실패: " + ex.Message;
            _log.Error("복제 중 오류", ex);
            _dialogs.ShowError($"복제 중 오류가 발생했습니다.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            DisposeCts();
        }
    }

    [RelayCommand(CanExecute = nameof(IsBusy))]
    private void Cancel()
    {
        _cts?.Cancel();
        StatusText = "취소하는 중...";
    }

    [RelayCommand]
    private void OpenDestination()
    {
        var target = _rootModel is not null && !string.IsNullOrWhiteSpace(DestinationPath)
            ? ReplicationPlanner.GetDestinationBase(_rootModel, CreateOptions())
            : DestinationPath;
        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
        {
            target = DestinationPath;
        }

        if (string.IsNullOrWhiteSpace(target) || !Directory.Exists(target))
        {
            _dialogs.ShowWarning("열 수 있는 목적 폴더가 없습니다.");
            return;
        }

        Process.Start(new ProcessStartInfo("explorer.exe", $"\"{target}\"") { UseShellExecute = true });
    }

    // ===== 내부 =====

    private async Task LoadSourceAsync(string folder)
    {
        // 같은 폴더를 다시 스캔하면 이전 선택을 유지합니다.
        var previousSelection = _rootModel is not null
                                && string.Equals(_rootModel.FullPath, PathUtil.Normalize(folder), StringComparison.OrdinalIgnoreCase)
            ? GetSelectedRelativePaths()
            : null;

        IsBusy = true;
        IsProgressIndeterminate = true;
        StatusText = "폴더 구조를 읽는 중...";
        _cts = new CancellationTokenSource();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            var progress = new Progress<int>(count => StatusText = $"폴더 구조를 읽는 중... ({count:N0}개)");
            var root = await FolderScanner.ScanAsync(folder, progress, _log, _cts.Token);

            _rootModel = root;
            SourcePath = root.FullPath;
            BuildTree(root, previousSelection);
            OnPropertyChanged(nameof(TargetPreview));
            PlanSummary = null;

            var errors = _allNodes.Count(n => n.HasError);
            StatusText = $"폴더 {_allNodes.Count:N0}개를 읽었습니다 ({stopwatch.Elapsed.TotalSeconds:0.0}초)" +
                         (errors > 0 ? $", 읽기 실패 {errors}개" : string.Empty) +
                         ". 파일을 복사할 폴더를 체크하세요.";
            _log.Info($"스캔 완료: {root.FullPath} — 폴더 {_allNodes.Count:N0}개, {stopwatch.ElapsedMilliseconds}ms");
        }
        catch (OperationCanceledException)
        {
            StatusText = "스캔이 취소되었습니다.";
            _log.Warn("스캔 취소");
        }
        catch (Exception ex)
        {
            StatusText = "스캔 실패: " + ex.Message;
            _log.Error($"스캔 실패: {folder}", ex);
            _dialogs.ShowError($"폴더를 읽지 못했습니다.\n\n{ex.Message}");
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
            DisposeCts();
            RefreshCommandStates();
        }
    }

    private void BuildTree(FolderNode root, HashSet<string>? previousSelection)
    {
        RootNodes.Clear();
        _allNodes.Clear();
        _selectedCount = 0;

        var rootViewModel = new FolderNodeViewModel(root, null, OnNodeCheckedChanged) { IsExpanded = true };
        _allNodes.AddRange(rootViewModel.DescendantsAndSelf());
        RootNodes.Add(rootViewModel);

        if (previousSelection is not null)
        {
            foreach (var node in _allNodes.Where(n => previousSelection.Contains(n.RelativePath)))
            {
                node.IsChecked = true;
            }
        }

        UpdateSelectionSummary();
    }

    private void OnNodeCheckedChanged(FolderNodeViewModel node, bool isChecked)
    {
        _selectedCount += isChecked ? 1 : -1;
        UpdateSelectionSummary();
        PlanSummary = null;
    }

    private void UpdateSelectionSummary()
    {
        SelectionSummary = _allNodes.Count == 0
            ? "선택된 폴더 없음"
            : $"전체 폴더 {_allNodes.Count:N0}개 중 {_selectedCount:N0}개 선택 (선택한 폴더의 바로 아래 파일만 복사)";
    }

    private HashSet<string> GetSelectedRelativePaths() =>
        _allNodes.Where(n => n.IsChecked).Select(n => n.RelativePath).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private ReplicationOptions CreateOptions() => new()
    {
        DestinationRoot = DestinationPath ?? string.Empty,
        CreateRootFolder = CreateRootFolder,
        ConflictPolicy = ConflictPolicy,
        SkipHidden = SkipHidden,
        ExtensionFilter = ExtensionFilter,
    };

    private async Task<ReplicationPlan?> BuildPlanAsync()
    {
        if (_rootModel is null)
        {
            return null;
        }

        var options = CreateOptions();
        var error = ReplicationPlanner.Validate(_rootModel.FullPath, options.DestinationRoot);
        if (error is not null)
        {
            _dialogs.ShowWarning(error);
            return null;
        }

        var root = _rootModel;
        var selected = GetSelectedRelativePaths();
        IsBusy = true;
        IsProgressIndeterminate = true;
        StatusText = "복사 계획을 계산하는 중...";
        try
        {
            var plan = await Task.Run(() => ReplicationPlanner.Build(root, selected, options, _log));
            foreach (var warning in plan.Warnings)
            {
                _log.Warn(warning);
            }

            return plan;
        }
        catch (Exception ex)
        {
            _log.Error("계획 계산 실패", ex);
            _dialogs.ShowError($"계획을 계산하지 못했습니다.\n\n{ex.Message}");
            return null;
        }
        finally
        {
            IsBusy = false;
            IsProgressIndeterminate = false;
        }
    }

    private void ShowPlan(ReplicationPlan plan)
    {
        PlanSummary =
            $"폴더 {plan.Folders.Count:N0}개 생성 · 파일 {plan.Files.Count:N0}개 복사 ({SizeFormatter.Format(plan.TotalBytes)})" +
            (plan.Warnings.Count > 0 ? $" · 경고 {plan.Warnings.Count}건" : string.Empty);
    }

    private void RefreshCommandStates()
    {
        DropSourceCommand.NotifyCanExecuteChanged();
        DropDestinationCommand.NotifyCanExecuteChanged();
        BrowseSourceCommand.NotifyCanExecuteChanged();
        BrowseDestinationCommand.NotifyCanExecuteChanged();
        RescanCommand.NotifyCanExecuteChanged();
        PreviewCommand.NotifyCanExecuteChanged();
        ExecuteCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    private void DisposeCts()
    {
        _cts?.Dispose();
        _cts = null;
    }

    private void LoadSettings()
    {
        _loadingSettings = true;
        try
        {
            var saved = _settings.Get(SettingsKey, new FolderReplicatorSettings());
            DestinationPath = saved.DestinationPath;
            CreateRootFolder = saved.CreateRootFolder;
            ConflictPolicy = saved.ConflictPolicy;
            SkipHidden = saved.SkipHidden;
            ExtensionFilter = saved.ExtensionFilter ?? string.Empty;
        }
        finally
        {
            _loadingSettings = false;
        }
    }

    private void SaveSettings() =>
        _settings.Set(SettingsKey, new FolderReplicatorSettings
        {
            DestinationPath = DestinationPath,
            CreateRootFolder = CreateRootFolder,
            ConflictPolicy = ConflictPolicy,
            SkipHidden = SkipHidden,
            ExtensionFilter = ExtensionFilter,
        });

    private sealed class FolderReplicatorSettings
    {
        public string? DestinationPath { get; set; }

        public bool CreateRootFolder { get; set; } = true;

        public ConflictPolicy ConflictPolicy { get; set; } = ConflictPolicy.Skip;

        public bool SkipHidden { get; set; } = true;

        public string? ExtensionFilter { get; set; }
    }
}
