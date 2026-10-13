using Cleaner.App.Localization;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cleaner.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Wpf.Ui.Controls;

namespace Cleaner.App.ViewModels;

public partial class CleanupViewModel : HelpViewModelBase
{
    protected override string HelpKey => "cleanup";

    private readonly OperationsRepository _ops;
    private readonly PresetsRepository _presets;
    private readonly LocalizationRepository _loc;
    private readonly ICleanupRunner _runner;
    private readonly ISafetyService _safety;
    private readonly IWhitelistService _whitelist;
    private readonly IQuarantineService _quarantine;

    private CancellationTokenSource? _cts;
    private CleanupReport? _lastReport;
    [ObservableProperty] private int _currentStage;
    [ObservableProperty] private string _previewSizeText = "—";
    [ObservableProperty] private string _resultSummary = "";
    public bool CanEditSelection => !IsRunning;
    public ObservableCollection<CleanupDetailRow> ResultRows { get; } = new();
    public ObservableCollection<UserCleanupPreset> CustomPresets { get; } = new();
    [ObservableProperty] private UserCleanupPreset? _selectedCustomPreset;
    [ObservableProperty] private bool _presetEditorOpen;
    [ObservableProperty] private string _presetName = "";
    [ObservableProperty] private int _resultTabIndex;
    [ObservableProperty] private string _previewNote = "";
    [ObservableProperty] private string _categorySummary = "";
    public string CompactSummary => Loc.F("preview.compact", CheckedCount, PreviewSizeText);
    public bool HasResults => ResultRows.Count > 0;

    private void LoadCustomPresets()
    {
        CustomPresets.Clear();
        foreach (var preset in UserSettings.GetCleanupPresets()) CustomPresets.Add(preset);
    }
    partial void OnSelectedCustomPresetChanged(UserCleanupPreset? value)
    {
        DeletePresetCommand.NotifyCanExecuteChanged();
        if (value == null || IsRunning) return;
        SelectedPreset = null;
        var known = Categories.SelectMany(c => c.Operations).Select(o => o.Key).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var op in Categories.SelectMany(c => c.Operations)) op.IsChecked = value.Keys.Contains(op.Key, StringComparer.OrdinalIgnoreCase);
        PresetName = value.Name;
        PresetNoteText = value.Keys.Any(k => !known.Contains(k)) ? Loc.T("presets.unknown") : Loc.T("presets.custom.note");
        OnPropertyChanged(nameof(HasPresetNote));
    }
    [RelayCommand] private void EditPreset() { PresetEditorOpen = !PresetEditorOpen; }
    [RelayCommand(CanExecute = nameof(CanRun))] private void SavePreset()
    {
        try
        {
            var preset = UserSettings.SaveCleanupPreset(PresetName, Categories.SelectMany(c => c.Operations).Where(o => o.IsChecked).Select(o => o.Key), _ops.Operations.Select(o => o.Key));
            LoadCustomPresets(); SelectedCustomPreset = CustomPresets.First(p => p.Id == preset.Id);
            PresetEditorOpen = false; StatusText = Loc.T("presets.saved");
        }
        catch (Exception ex) { StatusText = Loc.T(ex.Message); }
    }
    private bool CanDeletePreset() => !IsRunning && SelectedCustomPreset != null;
    [RelayCommand(CanExecute = nameof(CanDeletePreset))] private void DeletePreset()
    {
        try { if (SelectedCustomPreset == null) return; UserSettings.DeleteCleanupPreset(SelectedCustomPreset.Id); SelectedCustomPreset = null; LoadCustomPresets(); StatusText = Loc.T("presets.deleted"); }
        catch (Exception ex) { StatusText = Loc.T(ex.Message); }
    }
    private void ShowResults(CleanupSessionResult result, bool preview)
    {
        ResultRows.Clear();
        foreach (var outcome in result.Outcomes) ResultRows.Add(new CleanupDetailRow(outcome, _ops, preview, result));
        CategorySummary = string.Join(" · ", result.Outcomes.GroupBy(o => _ops.GetByKey(o.Key)?.Category ?? "other").Select(g =>
            Loc.T("section.sec." + g.Key + ".name") + ": " + CleanupDetailRow.FormatBytes(g.Sum(o => preview ? o.PreviewBytes : o.BytesQuarantined + o.BytesFreed))));
        PreviewNote = Loc.F("preview.sample.note", result.OmittedPreviewFiles, result.OmittedSkipDetails);
        OnPropertyChanged(nameof(HasResults));
        ResultTabIndex = 1;
    }
    private CleanupReport MakeReport(CleanupSessionResult result, bool preview)
    {
        // Persist handler identity so history remains understandable if the catalog later changes.
        return new CleanupReport { DryRun = preview, Result = result, QuarantineDirectory = preview ? null : _quarantine.SessionDirectory };
    }

    private bool CanExportReport() => !IsRunning && _lastReport != null;

    [RelayCommand(CanExecute = nameof(CanExportReport))]
    private void ExportReport()
    {
        try
        {
            var path = ReportExport.ChoosePath("Cleaner-cleanup");
            if (path == null || _lastReport == null) return;
            _lastReport.Save(path);
            StatusText = Loc.F("report.saved", path);
        }
        catch (Exception ex) { StatusText = Loc.T("t.oshibka") + ex.Message; }
    }

    public ObservableCollection<CleanupPresetViewModel> Presets { get; } = new();
    public ObservableCollection<CleanupCategoryViewModel> Categories { get; } = new();

    [ObservableProperty]
    private CleanupPresetViewModel? _selectedPreset;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = Loc.T("t.gotovo");

    [ObservableProperty]
    private string _bytesFreedText = "";

    [ObservableProperty]
    private int _checkedCount;

    [ObservableProperty]
    private string _presetNoteText = "";

    public bool HasPresetNote => !string.IsNullOrWhiteSpace(PresetNoteText);

    /// <summary>Текст кнопки «Запустить» с счётчиком (или без, если 0).</summary>
    public string RunButtonText => CheckedCount <= 0
        ? Loc.T("t.zapustit")
        : Loc.F("t.zapustit_2", CheckedCount);

    public CleanupViewModel(
        OperationsRepository ops,
        PresetsRepository presets,
        LocalizationRepository loc,
        ICleanupRunner runner,
        ISafetyService safety,
        IWhitelistService whitelist,
        IQuarantineService quarantine)
    {
        _ops = ops ?? throw new ArgumentNullException(nameof(ops));
        _presets = presets ?? throw new ArgumentNullException(nameof(presets));
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _safety = safety;
        _whitelist = whitelist;
        _quarantine = quarantine;

        LoadPresets();
        LoadCategories();
        LoadCustomPresets();
        UpdateCheckedCount();
    }

    // ------------------------------------------------------------------
    //  Загрузка данных
    // ------------------------------------------------------------------

    private void LoadPresets()
    {
        foreach (var (key, preset) in _presets.CleanupPresets)
        {
            Presets.Add(new CleanupPresetViewModel(
                key,
                _loc[preset.NameKey],
                _loc[preset.ShortKey],
                _loc[preset.NoteKey],
                preset.Keys));
        }
    }

    private void LoadCategories()
    {
        var byCategory = new Dictionary<string, CleanupCategoryViewModel>(StringComparer.OrdinalIgnoreCase);

        foreach (var section in _ops.Sections)
        {
            var categoryKey = section.Key.StartsWith("sec.")
                ? section.Key.Substring(4)
                : section.Key;

            var name = _loc[section.NameKey];
            var icon = IconForCategory(categoryKey);
            var vm = new CleanupCategoryViewModel(categoryKey, name, icon);
            byCategory[categoryKey] = vm;
            Categories.Add(vm);
        }

        foreach (var op in _ops.Operations)
        {
            if (string.IsNullOrEmpty(op.Category)) continue;
            if (!byCategory.TryGetValue(op.Category!, out var cat)) continue;

            var name = _loc[op.NameKey];
            var desc = _loc[op.DescKey].Replace("`n", "\n");
            var opVm = new CleanupOperationViewModel(op, name, desc);
            opVm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(CleanupOperationViewModel.IsChecked))
                    UpdateCheckedCount();
            };
            cat.Operations.Add(opVm);
        }

        UpdateCheckedCount();
    }

    private static SymbolRegular IconForCategory(string key) => key switch
    {
        "quick" => SymbolRegular.Flash24,
        "reports" => SymbolRegular.DocumentText24,
        "syscache" => SymbolRegular.ShieldCheckmark24,
        "gfx" => SymbolRegular.Desktop24,
        "browsers" => SymbolRegular.Globe24,
        "apps" => SymbolRegular.Apps24,
        "dev" => SymbolRegular.Code24,
        "privacy" => SymbolRegular.LockClosed24,
        "heavy" => SymbolRegular.Warning24,
        _ => SymbolRegular.Folder24
    };

    // ------------------------------------------------------------------
    //  Пресеты
    // ------------------------------------------------------------------

    partial void OnSelectedPresetChanged(CleanupPresetViewModel? value)
    {
        if (value is null)
        {
            PresetNoteText = "";
            OnPropertyChanged(nameof(HasPresetNote));
            return;
        }

        SelectedCustomPreset = null;
        var keys = new HashSet<string>(value.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var op in Categories.SelectMany(c => c.Operations))
            op.IsChecked = keys.Contains(op.Key);

        PresetNoteText = value.Note;
        OnPropertyChanged(nameof(HasPresetNote));
        StatusText = Loc.F("t.preset", value.Name);
        UpdateCheckedCount();
    }

    // ------------------------------------------------------------------
    //  Запуск / отмена
    // ------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RunAsync() => RunCoreAsync(false);

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task AnalyzeAsync() => RunCoreAsync(true);

    private async Task RunCoreAsync(bool previewOnly)
    {
        var selectedOps = Categories
            .SelectMany(c => c.Operations)
            .Where(o => o.IsChecked)
            .ToList();
        var selected = selectedOps.Select(o => o.Key).ToList();

        if (selected.Count == 0)
        {
            StatusText = Loc.T("t.nichego_ne_vybrano");
            return;
        }

        _lastReport = null;
        var historyKind = "preview";
        string? historyError = null;
        IsRunning = true;
        CurrentStage = 1;
        PreviewSizeText = "—";
        ResultSummary = "";
        BytesFreedText = "";
        StatusText = Loc.T("t.analiz_proveryaem_chto_budet_ochischeno");
        _cts = new CancellationTokenSource();

        try
        {
            var token = _cts.Token;

            var progress = new Progress<CleanupProgress>(p =>
            {
                if (p.CurrentPath != null)
                    StatusText = $"[{p.OperationKey}] {p.CurrentPath}";
            });

            // Шаг 1. Пробный прогон: ничего не удаляем, только считаем.
            var scanCtx = new CleanupContext(
                _safety, _whitelist, _quarantine,
                progress: progress,
                cancellationToken: token,
                dryRun: true);

            var scan = await Task.Run(() => _runner.Run(selected, scanCtx, token), token);
            _lastReport = MakeReport(scan, true);
            ShowResults(scan, true);
            PreviewSizeText = FormatBytes(scan.PreviewBytes);

            if (previewOnly && !scan.Canceled)
            {
                StatusText = Loc.F("preview.completed", scan.OperationsRun, scan.OperationsFailed);
                return;
            }
            if (scan.Canceled)
            {
                historyKind = "canceled";
                StatusText = Loc.T("t.otmeneno_nichego_ne_udaleno");
                return;
            }

            var hasNonFileOps = selectedOps.Any(o =>
                !CleanupDetailRow.IsQuarantined(o.Handler));

            if (scan.PreviewBytes <= 0 && !hasNonFileOps)
            {
                StatusText = Loc.T("t.nechego_ochischat_vybrannye_mesta_uzhe_chist");
                return;
            }

            var dangerNames = selectedOps
                .Where(o => o.Level == "danger")
                .Select(o => o.Name)
                .ToList();

            // Шаг 2. Подтверждение пользователя.
            CurrentStage = 2;
            if (!await ConfirmRunAsync(selected.Count, scan.PreviewBytes, dangerNames, hasNonFileOps))
            {
                historyKind = "declined";
                StatusText = Loc.T("t.otmeneno_nichego_ne_udaleno");
                return;
            }

            // Шаг 3. Настоящая очистка.
            CurrentStage = 3;
            if (dangerNames.Count > 0)
            {
                StatusText = Loc.T("t.sozdaem_tochku_vosstanovleniya_windows");
                if (!await SessionRestorePoint.EnsureAsync(Loc.T("t.pered_ochistkoy"))) { historyKind = "declined"; return; }
            }

            StatusText = Loc.T("t.vypolnyaetsya_ochistka");
            AppLog.Info($"Очистка: операций {selected.Count} ({string.Join(", ", selected)})");

            var ctx = new CleanupContext(
                _safety, _whitelist, _quarantine,
                progress: progress,
                cancellationToken: token,
                dryRun: false);

            historyKind = "applied";
            _lastReport = MakeReport(new CleanupSessionResult(), false);
            var result = await Task.Run(() => _runner.Run(selected, ctx, token), token);
            _lastReport = MakeReport(result, false);
            ShowResults(result, false);
            ResultSummary = Loc.F("design.cleanup.result", result.FilesQuarantined, result.FilesSkipped, result.OperationsFailed);

            foreach (var outcome in result.Outcomes)
            {
                foreach (var err in outcome.Errors)
                    AppLog.Warn($"Операция {outcome.Key}: {err}");
            }

            AppLog.Info($"Очистка завершена: удалено {result.FilesDeleted}, пропущено {result.FilesSkipped}, " +
                        $"ошибок {result.OperationsFailed}, освобождено {FormatBytes(result.BytesFreed)}");

            StatusText = result.Canceled
                ? Loc.F("t.otmeneno_obrabotano_operaciy", result.OperationsRun)
                : Loc.F("t.gotovo_udaleno_propuscheno", result.FilesQuarantined, result.FilesSkipped) +
                  Loc.F("t.oshibok", result.OperationsFailed);

            await AnimateFreedAsync(result.BytesQuarantined);
        }
        catch (OperationCanceledException)
        {
            historyKind = "canceled";
            StatusText = Loc.T("t.otmeneno");
        }
        catch (Exception ex)
        {
            historyKind = "failed"; historyError = ex.Message;
            AppLog.Error("Ошибка очистки", ex);
            StatusText = Loc.T("t.oshibka") + ex.Message;
        }
        finally
        {
            if (_lastReport == null && historyKind is "failed" or "canceled") _lastReport = new CleanupReport { DryRun = true, Result = new CleanupSessionResult { Canceled = historyKind == "canceled" } };
            if (_lastReport != null) UserSettings.AddCleanup(CleanupHistoryEntry.FromReport(_lastReport, historyKind, historyError));
            if (UserSettings.LastSaveError != null) StatusText += " · " + Loc.T("history.save.failed");
            CurrentStage = 4;
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            RunCommand.NotifyCanExecuteChanged();
            AnalyzeCommand.NotifyCanExecuteChanged();
            SavePresetCommand.NotifyCanExecuteChanged();
            DeletePresetCommand.NotifyCanExecuteChanged();
        }
    }

    /// <summary>Окно подтверждения перед настоящей очисткой.</summary>
    private static async Task<bool> ConfirmRunAsync(int operationsCount, long bytes, IReadOnlyList<string> dangerNames, bool hasNonFileOps)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Loc.F("t.vybrano_operaciy", operationsCount));
        sb.AppendLine(Loc.F("t.orientirovochno_budet_osvobozhdeno", FormatBytes(bytes)));
        sb.AppendLine();

        if (dangerNames.Count > 0)
        {
            sb.AppendLine(Loc.T("t.sredi_vybrannogo_est_operacii_povyshennogo_r"));
            foreach (var name in dangerNames)
                sb.AppendLine("  • " + name);
            sb.AppendLine();
            sb.AppendLine(Loc.T("t.pered_ochistkoy_budet_sozdana_tochka_vosstan"));
            sb.AppendLine();
        }

        sb.AppendLine(Loc.T("t.udalyaemye_fayly_snachala_popadayut_v_karant"));

        if (hasNonFileOps)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.T("t.chast_operaciy_reestr_sistemnye_komandy_wind"));
        }

        sb.AppendLine();
        sb.Append(Loc.T("t.prodolzhit"));

        return await Dialogs.ConfirmAsync(Loc.T("t.podtverzhdenie_ochistki"), sb.ToString(), dangerNames.Count > 0);
    }

    /// <summary>Анимированный счётчик освобождённого места (ease-out, ~0.8 с).</summary>
    private async Task AnimateFreedAsync(long bytes)
    {
        var label = Loc.T("cleanup.quarantined");

        if (bytes <= 0 || !Motion.Enabled)
        {
            BytesFreedText = label + FormatBytes(bytes);
            return;
        }

        const int frames = 20;
        for (var i = 1; i <= frames; i++)
        {
            var t = i / (double)frames;
            var eased = 1 - Math.Pow(1 - t, 3);
            BytesFreedText = label + FormatBytes((long)(bytes * eased));
            await Task.Delay(40);
        }

        BytesFreedText = label + FormatBytes(bytes);
    }

    private bool CanRun() => !IsRunning && CheckedCount > 0;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        try { _cts?.Cancel(); }
        catch { }
    }

    private bool CanCancel() => IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanEditSelection));
        RunCommand.NotifyCanExecuteChanged();
        AnalyzeCommand.NotifyCanExecuteChanged();
        SavePresetCommand.NotifyCanExecuteChanged();
        DeletePresetCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
        ExportReportCommand.NotifyCanExecuteChanged();
    }

    partial void OnCheckedCountChanged(int value)
    {
        OnPropertyChanged(nameof(RunButtonText));
        OnPropertyChanged(nameof(CompactSummary));
        RunCommand.NotifyCanExecuteChanged(); AnalyzeCommand.NotifyCanExecuteChanged(); SavePresetCommand.NotifyCanExecuteChanged();
    }

    partial void OnPreviewSizeTextChanged(string value) => OnPropertyChanged(nameof(CompactSummary));

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    public void UpdateCheckedCount()
    {
        if (!IsRunning)
        {
            _lastReport = null;
            ExportReportCommand.NotifyCanExecuteChanged();
            CurrentStage = 0;
            PreviewSizeText = "—";
            ResultSummary = "";
            ResultRows.Clear(); OnPropertyChanged(nameof(HasResults)); ResultTabIndex = 0;
        }
        CheckedCount = Categories
            .SelectMany(c => c.Operations)
            .Count(o => o.IsChecked);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v.ToString("F2", CultureInfo.InvariantCulture)} {units[u]}";
    }
}

public sealed class CleanupPresetViewModel
{
    public string Key { get; }
    public string Name { get; }
    public string Short { get; }
    public string Note { get; }
    public IReadOnlyList<string> Keys { get; }

    public CleanupPresetViewModel(string key, string name, string @short, string note, IReadOnlyList<string> keys)
    {
        Key = key;
        Name = name;
        Short = @short;
        Note = note;
        Keys = keys;
    }
}
