using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SunTool.Core;
using SunTool.Core.Common;
using SunTool.Core.FileSystem;
using SunTool.Core.Logging;
using SunTool.Core.Settings;
using SunTool.Modules.BatchRename.Logic;
using SunTool.Wpf.Services;

namespace SunTool.Modules.BatchRename.ViewModels;

public sealed partial class BatchRenameViewModel : ObservableObject
{
    private const string SettingsKey = "BatchRename";
    private const string PresetsKey = "BatchRename.Presets";

    private readonly IAppLogger _log;
    private readonly IDialogService _dialogs;
    private readonly ISettingsService _settings;
    private readonly string _journalPath = Path.Combine(AppPaths.UndoDirectory, "last-rename.json");

    private readonly List<string> _sources = new();
    private readonly HashSet<string> _excluded = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _directoryCache = new(StringComparer.OrdinalIgnoreCase);
    private List<RenameItem> _items = new();
    private bool _suspendRefresh;

    public BatchRenameViewModel(ILogService logs, IDialogService dialogs, ISettingsService settings)
    {
        _log = logs.For("BatchRename");
        _dialogs = dialogs;
        _settings = settings;

        Rules.CollectionChanged += OnRulesCollectionChanged;
        LoadSettings();
        LoadPresetNames();
        CanUndo = File.Exists(_journalPath);
        RefreshPreview();
    }

    public ObservableCollection<RenameRowViewModel> Rows { get; } = new();

    public ObservableCollection<RenameRuleBase> Rules { get; } = new();

    public ObservableCollection<string> PresetNames { get; } = new();

    public IReadOnlyList<RuleDescriptor> AvailableRules => RuleCatalog.All;

    public IReadOnlyList<EnumOption<SortKey>> SortKeys => RenameChoices.SortKeys;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(RemoveRuleCommand), nameof(MoveRuleUpCommand), nameof(MoveRuleDownCommand), nameof(DuplicateRuleCommand))]
    private RenameRuleBase? _selectedRule;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(MoveRowUpCommand), nameof(MoveRowDownCommand), nameof(RemoveRowCommand))]
    private RenameRowViewModel? _selectedRow;

    [ObservableProperty]
    private string _sourceDisplay = Loc.T("(지정 안 됨)");

    [ObservableProperty]
    private bool _includeSubfolders;

    [ObservableProperty]
    private bool _skipHidden = true;

    [ObservableProperty]
    private string _extensionFilter = string.Empty;

    [ObservableProperty]
    private SortKey _sortKey = SortKey.NaturalName;

    [ObservableProperty]
    private bool _sortDescending;

    [ObservableProperty]
    private bool _includeExtension;

    [ObservableProperty]
    private string _summary = string.Empty;

    [ObservableProperty]
    private int _changeCount;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(UndoCommand))]
    private bool _canUndo;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadPresetCommand), nameof(DeletePresetCommand))]
    private string? _selectedPreset;

    // ===== 옵션 변경 =====

    partial void OnIncludeSubfoldersChanged(bool value) => OnCollectOptionChanged();

    partial void OnSkipHiddenChanged(bool value) => OnCollectOptionChanged();

    partial void OnExtensionFilterChanged(string value) => OnCollectOptionChanged();

    partial void OnSortKeyChanged(SortKey value) => OnSortOptionChanged();

    partial void OnSortDescendingChanged(bool value) => OnSortOptionChanged();

    partial void OnIncludeExtensionChanged(bool value)
    {
        SaveSettings();
        RefreshPreview();
    }

    partial void OnIsBusyChanged(bool value)
    {
        ExecuteCommand.NotifyCanExecuteChanged();
        UndoCommand.NotifyCanExecuteChanged();
        DropCommand.NotifyCanExecuteChanged();
    }

    private void OnCollectOptionChanged()
    {
        SaveSettings();
        Reload();
    }

    private void OnSortOptionChanged()
    {
        SaveSettings();
        if (_suspendRefresh)
        {
            return;
        }

        _items = FileCollector.Sort(_items, SortKey, SortDescending, _items);
        RefreshPreview(forceRebuild: true);
    }

    // ===== 파일 목록 =====

    private bool CanEdit() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private void Drop(string[]? paths)
    {
        if (paths is null || paths.Length == 0)
        {
            return;
        }

        var added = 0;
        foreach (var path in paths)
        {
            if (!Directory.Exists(path) && !File.Exists(path))
            {
                continue;
            }

            if (!_sources.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                _sources.Add(path);
                added++;
            }
        }

        _log.Info($"대상 추가: {added}개 ({string.Join(", ", paths.Take(3))}{(paths.Length > 3 ? " …" : string.Empty)})");
        UpdateSourceDisplay();
        Reload();
    }

    [RelayCommand]
    private void BrowseFolder()
    {
        var folder = _dialogs.PickFolder(Loc.T("이름을 바꿀 파일이 있는 폴더 선택"), _sources.FirstOrDefault(Directory.Exists));
        if (folder is not null)
        {
            Drop([folder]);
        }
    }

    [RelayCommand]
    private void ClearFiles()
    {
        _sources.Clear();
        _excluded.Clear();
        _items = new List<RenameItem>();
        UpdateSourceDisplay();
        RefreshPreview(forceRebuild: true);
    }

    [RelayCommand]
    private void ReloadFiles() => Reload();

    private bool HasSelectedRow() => SelectedRow is not null;

    [RelayCommand(CanExecute = nameof(HasSelectedRow))]
    private void MoveRowUp() => MoveRow(-1);

    [RelayCommand(CanExecute = nameof(HasSelectedRow))]
    private void MoveRowDown() => MoveRow(1);

    [RelayCommand(CanExecute = nameof(HasSelectedRow))]
    private void RemoveRow()
    {
        if (SelectedRow is null)
        {
            return;
        }

        var index = Rows.IndexOf(SelectedRow);
        _excluded.Add(SelectedRow.Item.FullPath);
        _items.RemoveAt(index);
        Rows.RemoveAt(index);
        SelectedRow = Rows.Count == 0 ? null : Rows[Math.Min(index, Rows.Count - 1)];
        RefreshPreview();
    }

    private void MoveRow(int offset)
    {
        if (SelectedRow is null)
        {
            return;
        }

        var index = Rows.IndexOf(SelectedRow);
        var target = index + offset;
        if (index < 0 || target < 0 || target >= Rows.Count)
        {
            return;
        }

        // 직접 순서를 바꾸면 정렬 기준을 "직접 지정"으로 전환 (재정렬 없이)
        if (SortKey != SortKey.Manual)
        {
            _suspendRefresh = true;
            SortKey = SortKey.Manual;
            _suspendRefresh = false;
        }

        (_items[index], _items[target]) = (_items[target], _items[index]);
        var row = SelectedRow;
        Rows.Move(index, target);
        SelectedRow = row;
        RefreshPreview();
    }

    // ===== 규칙 =====

    private bool HasSelectedRule() => SelectedRule is not null;

    [RelayCommand]
    private void AddRule(RuleDescriptor? descriptor)
    {
        if (descriptor is null)
        {
            return;
        }

        var rule = descriptor.Create();
        Rules.Add(rule);
        SelectedRule = rule;
        _log.Debug($"규칙 추가: {descriptor.Name}");
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRule))]
    private void RemoveRule()
    {
        if (SelectedRule is null)
        {
            return;
        }

        var index = Rules.IndexOf(SelectedRule);
        Rules.RemoveAt(index);
        SelectedRule = Rules.Count == 0 ? null : Rules[Math.Min(index, Rules.Count - 1)];
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRule))]
    private void MoveRuleUp() => MoveRule(-1);

    [RelayCommand(CanExecute = nameof(HasSelectedRule))]
    private void MoveRuleDown() => MoveRule(1);

    [RelayCommand(CanExecute = nameof(HasSelectedRule))]
    private void DuplicateRule()
    {
        if (SelectedRule is null)
        {
            return;
        }

        var copy = SelectedRule.Clone();
        Rules.Insert(Rules.IndexOf(SelectedRule) + 1, copy);
        SelectedRule = copy;
    }

    [RelayCommand]
    private void ClearRules()
    {
        if (Rules.Count == 0 || !_dialogs.Confirm(Loc.T("모든 규칙을 지울까요?")))
        {
            return;
        }

        ReplaceRules(Array.Empty<RenameRuleBase>());
    }

    private void MoveRule(int offset)
    {
        if (SelectedRule is null)
        {
            return;
        }

        var index = Rules.IndexOf(SelectedRule);
        var target = index + offset;
        if (target < 0 || target >= Rules.Count)
        {
            return;
        }

        var rule = SelectedRule;
        Rules.Move(index, target);
        SelectedRule = rule;
    }

    private void ReplaceRules(IEnumerable<RenameRuleBase> rules)
    {
        _suspendRefresh = true;
        try
        {
            // Clear() 는 이벤트 구독 해제가 안 되므로 하나씩 제거
            while (Rules.Count > 0)
            {
                Rules.RemoveAt(Rules.Count - 1);
            }

            foreach (var rule in rules)
            {
                Rules.Add(rule);
            }
        }
        finally
        {
            _suspendRefresh = false;
        }

        SelectedRule = Rules.FirstOrDefault();
        SaveRules();
        RefreshPreview();
    }

    private void OnRulesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (RenameRuleBase rule in e.OldItems)
            {
                rule.PropertyChanged -= OnRulePropertyChanged;
            }
        }

        if (e.NewItems is not null)
        {
            foreach (RenameRuleBase rule in e.NewItems)
            {
                rule.PropertyChanged += OnRulePropertyChanged;
            }
        }

        if (!_suspendRefresh)
        {
            SaveRules();
            RefreshPreview();
        }
    }

    private void OnRulePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RenameRuleBase.Summary) or nameof(RenameRuleBase.RuleName))
        {
            return;
        }

        SaveRules();
        RefreshPreview();
    }

    // ===== 프리셋 =====

    private bool HasSelectedPreset() => !string.IsNullOrEmpty(SelectedPreset);

    [RelayCommand]
    private void SavePreset()
    {
        if (Rules.Count == 0)
        {
            _dialogs.ShowInfo(Loc.T("저장할 규칙이 없습니다."));
            return;
        }

        var name = _dialogs.Prompt(Loc.T("프리셋 이름을 입력하세요."), Loc.T("프리셋 저장"), SelectedPreset ?? string.Empty)?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            return;
        }

        var presets = LoadPresets();
        if (presets.ContainsKey(name) && !_dialogs.Confirm(Loc.F("'{0}' 프리셋을 덮어쓸까요?", name)))
        {
            return;
        }

        presets[name] = Rules.Select(r => r.Clone()).ToList();
        _settings.Set(PresetsKey, presets);
        _settings.Save();
        LoadPresetNames();
        SelectedPreset = name;
        _log.Info($"프리셋 저장: {name} (규칙 {Rules.Count}개)");
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPreset))]
    private void LoadPreset()
    {
        if (SelectedPreset is null || !LoadPresets().TryGetValue(SelectedPreset, out var rules))
        {
            return;
        }

        ReplaceRules(rules.Select(r => r.Clone()));
        _log.Info($"프리셋 불러오기: {SelectedPreset}");
    }

    [RelayCommand(CanExecute = nameof(HasSelectedPreset))]
    private void DeletePreset()
    {
        if (SelectedPreset is null || !_dialogs.Confirm(Loc.F("'{0}' 프리셋을 삭제할까요?", SelectedPreset)))
        {
            return;
        }

        var presets = LoadPresets();
        presets.Remove(SelectedPreset);
        _settings.Set(PresetsKey, presets);
        _settings.Save();
        LoadPresetNames();
        SelectedPreset = null;
    }

    private Dictionary<string, List<RenameRuleBase>> LoadPresets() =>
        _settings.Get(PresetsKey, new Dictionary<string, List<RenameRuleBase>>());

    private void LoadPresetNames()
    {
        PresetNames.Clear();
        foreach (var name in LoadPresets().Keys.OrderBy(k => k, StringComparer.CurrentCultureIgnoreCase))
        {
            PresetNames.Add(name);
        }
    }

    // ===== 실행 / 되돌리기 =====

    private bool CanExecuteRename() => !IsBusy && ChangeCount > 0;

    private bool CanUndoNow() => !IsBusy && CanUndo;

    [RelayCommand(CanExecute = nameof(CanExecuteRename))]
    private async Task ExecuteAsync()
    {
        var targets = Rows.Where(r => r.Status == RenameStatus.Ok).ToList();
        if (targets.Count == 0)
        {
            return;
        }

        var blocked = Rows.Count(r => r.Status is RenameStatus.Conflict or RenameStatus.Invalid or RenameStatus.Error);
        var message = Loc.F("파일 {0:N0}개의 이름을 바꿉니다.", targets.Count) +
                      (blocked > 0 ? Loc.F("\n충돌/오류가 있는 {0:N0}개는 건너뜁니다.", blocked) : string.Empty) +
                      Loc.T("\n\n실행 후 '되돌리기'로 원래 이름으로 돌릴 수 있습니다. 계속할까요?");
        if (!_dialogs.Confirm(message, Loc.T("이름 일괄 변경")))
        {
            return;
        }

        var operations = targets
            .Select(r => new RenameOperation(r.Item.FullPath, Path.Combine(r.Item.Directory, r.NewName)))
            .ToList();

        IsBusy = true;
        _log.Info($"이름 변경 시작: {operations.Count}개");
        try
        {
            var result = await Task.Run(() => RenameExecutor.Execute(operations, _log));
            if (result.Completed.Count > 0)
            {
                RenameJournal.Save(_journalPath, new RenameJournal(DateTime.Now, result.Completed.ToList()));
                CanUndo = true;
            }

            ApplyCompleted(result.Completed);
            ReportResult(Loc.T("이름 변경"), result);
        }
        catch (Exception ex)
        {
            _log.Error("이름 변경 중 오류", ex);
            _dialogs.ShowError(Loc.F("이름 변경 중 오류가 발생했습니다.\n\n{0}", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand(CanExecute = nameof(CanUndoNow))]
    private async Task UndoAsync()
    {
        var journal = RenameJournal.Load(_journalPath);
        if (journal is null || journal.Operations.Count == 0)
        {
            _dialogs.ShowInfo(Loc.T("되돌릴 기록이 없습니다."));
            CanUndo = false;
            return;
        }

        var operations = journal.CreateUndoOperations();
        var missing = operations.Where(o => !File.Exists(o.SourcePath)).ToList();
        var valid = operations.Except(missing).ToList();
        var message = Loc.F("{0:yyyy-MM-dd HH:mm:ss} 에 바꾼 파일 {1:N0}개를 원래 이름으로 되돌립니다.", journal.Timestamp, valid.Count) +
                      (missing.Count > 0 ? Loc.F("\n(이미 옮겨졌거나 삭제된 {0:N0}개는 건너뜁니다)", missing.Count) : string.Empty) +
                      Loc.T("\n\n계속할까요?");
        if (valid.Count == 0)
        {
            _dialogs.ShowWarning(Loc.T("되돌릴 수 있는 파일이 없습니다 (모두 이동/삭제됨)."));
            RenameJournal.Delete(_journalPath);
            CanUndo = false;
            return;
        }

        if (!_dialogs.Confirm(message, Loc.T("되돌리기")))
        {
            return;
        }

        IsBusy = true;
        try
        {
            var result = await Task.Run(() => RenameExecutor.Execute(valid, _log));
            RenameJournal.Delete(_journalPath);
            CanUndo = false;
            ApplyCompleted(result.Completed);
            ReportResult(Loc.T("되돌리기"), result);
        }
        catch (Exception ex)
        {
            _log.Error("되돌리기 중 오류", ex);
            _dialogs.ShowError(Loc.F("되돌리는 중 오류가 발생했습니다.\n\n{0}", ex.Message));
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void ReportResult(string action, RenameExecutionResult result)
    {
        _log.Info($"{action} 완료: 성공 {result.Completed.Count}개, 실패 {result.Failed.Count}개");
        if (result.Failed.Count == 0)
        {
            _dialogs.ShowInfo(Loc.F("{0} 완료: {1:N0}개", action, result.Completed.Count));
            return;
        }

        var details = string.Join("\n", result.Failed.Take(10).Select(f => $"• {Path.GetFileName(f.Operation.SourcePath)}: {f.Error}"));
        _dialogs.ShowWarning(
            Loc.F("{0}: 성공 {1:N0}개, 실패 {2:N0}개\n\n{3}", action, result.Completed.Count, result.Failed.Count, details) +
            (result.Failed.Count > 10 ? "\n…" : string.Empty) + Loc.T("\n\n전체 내용은 터미널(F12)에서 확인하세요."));
    }

    /// <summary>바뀐 파일 경로를 목록에 반영합니다 (순서는 유지).</summary>
    private void ApplyCompleted(IReadOnlyList<RenameOperation> completed)
    {
        var map = completed.ToDictionary(o => o.SourcePath, o => o.TargetPath, StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < _items.Count; i++)
        {
            if (map.TryGetValue(_items[i].FullPath, out var newPath))
            {
                try
                {
                    _items[i] = RenameItem.FromPath(newPath);
                }
                catch (Exception ex)
                {
                    _log.Warn($"변경된 파일 정보를 읽지 못했습니다: {newPath}", ex);
                }
            }
        }

        for (var i = 0; i < _sources.Count; i++)
        {
            if (map.TryGetValue(_sources[i], out var newPath))
            {
                _sources[i] = newPath; // 파일을 직접 드롭한 경우
            }
        }

        _directoryCache.Clear();
        RefreshPreview(forceRebuild: true);
    }

    // ===== 미리보기 =====

    private void Reload()
    {
        if (_suspendRefresh)
        {
            return;
        }

        var filter = FileExtensionFilter.Parse(ExtensionFilter);
        var collected = FileCollector.Collect(_sources, IncludeSubfolders, filter, SkipHidden, _excluded, _log);
        _items = FileCollector.Sort(collected, SortKey, SortDescending, _items);
        _directoryCache.Clear();
        _log.Debug($"파일 목록: {_items.Count}개");
        RefreshPreview(forceRebuild: true);
    }

    private void RefreshPreview(bool forceRebuild = false)
    {
        if (_suspendRefresh)
        {
            return;
        }

        var previews = RenameEngine.Preview(_items, Rules.Cast<IRenameRule>().ToList(), IncludeExtension, ListDirectoryCached);

        var canUpdateInPlace = !forceRebuild && Rows.Count == previews.Count;
        for (var i = 0; canUpdateInPlace && i < previews.Count; i++)
        {
            canUpdateInPlace = ReferenceEquals(Rows[i].Item, previews[i].Item);
        }

        if (canUpdateInPlace)
        {
            for (var i = 0; i < previews.Count; i++)
            {
                Rows[i].Update(previews[i]);
            }
        }
        else
        {
            var selectedPath = SelectedRow?.Item.FullPath;
            Rows.Clear();
            foreach (var preview in previews)
            {
                Rows.Add(new RenameRowViewModel(preview));
            }

            SelectedRow = selectedPath is null
                ? null
                : Rows.FirstOrDefault(r => string.Equals(r.Item.FullPath, selectedPath, StringComparison.OrdinalIgnoreCase));
        }

        var ok = previews.Count(p => p.Status == RenameStatus.Ok);
        var conflicts = previews.Count(p => p.Status == RenameStatus.Conflict);
        var errors = previews.Count(p => p.Status is RenameStatus.Invalid or RenameStatus.Error);
        ChangeCount = ok;
        Summary = previews.Count == 0
            ? Loc.T("파일이 없습니다.")
            : Loc.F("파일 {0:N0}개 · 변경 {1:N0}개 · 충돌 {2:N0}개 · 오류 {3:N0}개", previews.Count, ok, conflicts, errors);
        ExecuteCommand.NotifyCanExecuteChanged();
    }

    private IEnumerable<string> ListDirectoryCached(string directory)
    {
        if (_directoryCache.TryGetValue(directory, out var cached))
        {
            return cached;
        }

        List<string> names;
        try
        {
            names = Directory.EnumerateFileSystemEntries(directory).Select(p => Path.GetFileName(p)).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            names = new List<string>();
        }

        _directoryCache[directory] = names;
        return names;
    }

    private void UpdateSourceDisplay()
    {
        SourceDisplay = _sources.Count switch
        {
            0 => Loc.T("(지정 안 됨)"),
            1 => _sources[0],
            _ => Loc.F("{0} 외 {1}개", _sources[0], _sources.Count - 1),
        };
    }

    // ===== 설정 저장 =====

    private void LoadSettings()
    {
        _suspendRefresh = true;
        try
        {
            var saved = _settings.Get(SettingsKey, new BatchRenameSettings());
            IncludeSubfolders = saved.IncludeSubfolders;
            SkipHidden = saved.SkipHidden;
            ExtensionFilter = saved.ExtensionFilter ?? string.Empty;
            SortKey = saved.SortKey == SortKey.Manual ? SortKey.NaturalName : saved.SortKey;
            SortDescending = saved.SortDescending;
            IncludeExtension = saved.IncludeExtension;

            foreach (var rule in _settings.Get(SettingsKey + ".Rules", new List<RenameRuleBase>()))
            {
                Rules.Add(rule);
            }

            SelectedRule = Rules.FirstOrDefault();
        }
        catch (Exception ex)
        {
            _log.Warn("이름 변경 설정을 불러오지 못했습니다.", ex);
        }
        finally
        {
            _suspendRefresh = false;
        }
    }

    private void SaveSettings()
    {
        if (_suspendRefresh)
        {
            return;
        }

        _settings.Set(SettingsKey, new BatchRenameSettings
        {
            IncludeSubfolders = IncludeSubfolders,
            SkipHidden = SkipHidden,
            ExtensionFilter = ExtensionFilter,
            SortKey = SortKey,
            SortDescending = SortDescending,
            IncludeExtension = IncludeExtension,
        });
    }

    private void SaveRules() => _settings.Set(SettingsKey + ".Rules", Rules.ToList());

    private sealed class BatchRenameSettings
    {
        public bool IncludeSubfolders { get; set; }

        public bool SkipHidden { get; set; } = true;

        public string? ExtensionFilter { get; set; }

        public SortKey SortKey { get; set; } = SortKey.NaturalName;

        public bool SortDescending { get; set; }

        public bool IncludeExtension { get; set; }
    }
}
