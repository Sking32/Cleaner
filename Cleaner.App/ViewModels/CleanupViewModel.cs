using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Wpf.Ui.Controls;

namespace Cleaner.App.ViewModels;

public partial class CleanupViewModel : ObservableObject
{
    private readonly OperationsRepository _ops;
    private readonly PresetsRepository _presets;
    private readonly LocalizationRepository _loc;
    private readonly ICleanupRunner _runner;
    private readonly ISafetyService _safety;
    private readonly IWhitelistService _whitelist;
    private readonly IQuarantineService _quarantine;

    private CancellationTokenSource? _cts;

    public ObservableCollection<CleanupPresetViewModel> Presets { get; } = new();
    public ObservableCollection<CleanupCategoryViewModel> Categories { get; } = new();

    [ObservableProperty]
    private CleanupPresetViewModel? _selectedPreset;

    [ObservableProperty]
    private bool _isRunning;

    [ObservableProperty]
    private string _statusText = "Готово";

    [ObservableProperty]
    private string _bytesFreedText = "";

    [ObservableProperty]
    private int _checkedCount;

    [ObservableProperty]
    private string _presetNoteText = "";

    public bool HasPresetNote => !string.IsNullOrWhiteSpace(PresetNoteText);

    /// <summary>Текст кнопки «Запустить» с счётчиком (или без, если 0).</summary>
    public string RunButtonText => CheckedCount <= 0
        ? "Запустить"
        : $"Запустить ({CheckedCount})";

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

        var keys = new HashSet<string>(value.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var op in Categories.SelectMany(c => c.Operations))
            op.IsChecked = keys.Contains(op.Key);

        PresetNoteText = value.Note;
        OnPropertyChanged(nameof(HasPresetNote));
        StatusText = $"Пресет: {value.Name}";
        UpdateCheckedCount();
    }

    // ------------------------------------------------------------------
    //  Запуск / отмена
    // ------------------------------------------------------------------

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RunAsync()
    {
        var selected = Categories
            .SelectMany(c => c.Operations)
            .Where(o => o.IsChecked)
            .Select(o => o.Key)
            .ToList();

        if (selected.Count == 0)
        {
            StatusText = "Ничего не выбрано";
            return;
        }

        IsRunning = true;
        BytesFreedText = "";
        StatusText = "Выполняется...";
        _cts = new CancellationTokenSource();

        try
        {
            var progress = new Progress<CleanupProgress>(p =>
            {
                if (p.CurrentPath != null)
                    StatusText = $"[{p.OperationKey}] {p.CurrentPath}";
            });

            var ctx = new CleanupContext(
                _safety, _whitelist, _quarantine,
                progress: progress,
                cancellationToken: _cts.Token);

            var token = _cts.Token;
            var result = await Task.Run(() => _runner.Run(selected, ctx, token), token);

            StatusText = result.Canceled
                ? $"Отменено. Обработано операций: {result.OperationsRun}"
                : $"Готово: удалено {result.FilesDeleted}, пропущено {result.FilesSkipped}, " +
                  $"ошибок: {result.OperationsFailed}";

            BytesFreedText = "Освобождено: " + FormatBytes(result.BytesFreed);
        }
        catch (OperationCanceledException)
        {
            StatusText = "Отменено";
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка: " + ex.Message;
        }
        finally
        {
            IsRunning = false;
            _cts?.Dispose();
            _cts = null;
            RunCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanRun() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        try { _cts?.Cancel(); }
        catch { }
    }

    private bool CanCancel() => IsRunning;

    partial void OnIsRunningChanged(bool value)
    {
        RunCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnCheckedCountChanged(int value)
    {
        OnPropertyChanged(nameof(RunButtonText));
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    public void UpdateCheckedCount()
    {
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