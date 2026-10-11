using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cleaner.App.Services;
using Cleaner.Core.Data;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Wpf.Ui.Controls;

namespace Cleaner.App.ViewModels;

public partial class OptimizeViewModel : ObservableObject
{
    private readonly IOptimizeService _optimize;
    private readonly TweakRepository _tweaks;
    private readonly PresetsRepository _presets;
    private readonly LocalizationRepository _loc;
    private readonly IAppNotifications _notify;

    public ObservableCollection<OptimizePresetViewModel> Presets { get; } = new();
    public ObservableCollection<OptimizeCategoryViewModel> Categories { get; } = new();

    [ObservableProperty]
    private OptimizePresetViewModel? _selectedPreset;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = "Готово";

    [ObservableProperty]
    private string _presetNoteText = "";

    [ObservableProperty]
    private string _conflictText = "";

    [ObservableProperty]
    private int _pendingRebootCount;

    public bool HasPresetNote => !string.IsNullOrWhiteSpace(PresetNoteText);
    public bool HasConflict => !string.IsNullOrWhiteSpace(ConflictText);
    public bool HasPendingReboot => PendingRebootCount > 0;
    public bool HasSelectedPreset => SelectedPreset is not null;

    public string PendingRebootText => PendingRebootCount == 0
        ? ""
        : $"⚠ Требуется перезагрузка: {PendingRebootCount}";

    public OptimizeViewModel(
        IOptimizeService optimize,
        TweakRepository tweaks,
        PresetsRepository presets,
        LocalizationRepository loc,
        IAppNotifications notify)
    {
        _optimize = optimize ?? throw new ArgumentNullException(nameof(optimize));
        _tweaks = tweaks ?? throw new ArgumentNullException(nameof(tweaks));
        _presets = presets ?? throw new ArgumentNullException(nameof(presets));
        _loc = loc ?? throw new ArgumentNullException(nameof(loc));
        _notify = notify ?? throw new ArgumentNullException(nameof(notify));

        LoadPresets();
        LoadCategories();
        RefreshAll();
    }

    // ------------------------------------------------------------------
    //  Загрузка данных
    // ------------------------------------------------------------------

    private void LoadPresets()
    {
        foreach (var (key, preset) in _presets.OptimizePresets)
        {
            Presets.Add(new OptimizePresetViewModel(
                key,
                _loc[preset.NameKey],
                preset.Keys));
        }
    }

    private void LoadCategories()
    {
        var byKey = new Dictionary<string, OptimizeCategoryViewModel>(StringComparer.OrdinalIgnoreCase);
        var order = new[] { "perf", "privacy", "win11", "network", "maint", "games" };

        foreach (var cat in order)
        {
            var vm = new OptimizeCategoryViewModel(cat, CategoryName(cat), IconForCategory(cat));
            byKey[cat] = vm;
            Categories.Add(vm);
        }

        foreach (var cat in _tweaks.Tweaks.Select(t => t.Category).Distinct())
        {
            if (string.IsNullOrEmpty(cat)) continue;
            if (byKey.ContainsKey(cat)) continue;

            var vm = new OptimizeCategoryViewModel(cat, cat, SymbolRegular.Settings24);
            byKey[cat] = vm;
            Categories.Add(vm);
        }

        foreach (var def in _tweaks.Tweaks)
        {
            if (string.IsNullOrEmpty(def.Category)) continue;
            if (!byKey.TryGetValue(def.Category, out var catVm)) continue;

            var name = _loc[def.NameKey];
            var shortText = _loc[def.ShortKey];
            var desc = _loc[def.DescKey].Replace("`n", "\n");
            var warn = _loc.Has(def.WarnKey) ? _loc[def.WarnKey] : "";

            var tw = new OptimizeTweakViewModel(def, name, shortText, desc, warn, OnToggleTweakAsync);
            tw.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(OptimizeTweakViewModel.IsApplied) ||
                    e.PropertyName == nameof(OptimizeTweakViewModel.IsPendingReboot))
                {
                    catVm.RefreshCounters();
                    UpdatePendingRebootCount();
                }
            };
            catVm.Tweaks.Add(tw);
        }
    }

    private static string CategoryName(string key) => key switch
    {
        "perf" => "Производительность",
        "privacy" => "Приватность",
        "win11" => "Windows 11",
        "network" => "Сеть",
        "maint" => "Обслуживание",
        "games" => "Игры",
        _ => key
    };

    private static SymbolRegular IconForCategory(string key) => key switch
    {
        "perf" => SymbolRegular.Flash24,
        "privacy" => SymbolRegular.LockClosed24,
        "win11" => SymbolRegular.Desktop24,
        "network" => SymbolRegular.Globe24,
        "maint" => SymbolRegular.Wrench24,
        "games" => SymbolRegular.Games24,
        _ => SymbolRegular.Settings24
    };

    // ------------------------------------------------------------------
    //  Toggle callback
    // ------------------------------------------------------------------

    private async Task OnToggleTweakAsync(OptimizeTweakViewModel tweak, bool apply)
    {
        IsBusy = true;
        try
        {
            if (apply && !string.Equals(tweak.Level, "safe", StringComparison.OrdinalIgnoreCase))
            {
                if (!ConfirmTweak(tweak))
                {
                    tweak.SetAppliedSilently(false, false);
                    StatusText = $"Отменено: {tweak.Name}";
                    return;
                }

                StatusText = "Создаём точку восстановления Windows...";
                await SessionRestorePoint.EnsureAsync($"перед применением «{tweak.Name}»");
            }

            var result = await Task.Run(() => apply ? _optimize.Apply(tweak.Key) : _optimize.Unapply(tweak.Key));

            if (result.Success)
            {
                tweak.SetAppliedSilently(
                    _optimize.IsAppliedByCleaner(tweak.Key),
                    _optimize.IsPendingReboot(tweak.Key));
                StatusText = apply ? $"Применено: {tweak.Name}" : $"Отключено: {tweak.Name}";
                _notify.Success(tweak.Name, apply ? "Твик применён" : "Твик отключён");
            }
            else
            {
                tweak.SetAppliedSilently(
                    _optimize.IsAppliedByCleaner(tweak.Key),
                    _optimize.IsPendingReboot(tweak.Key));

                StatusText = "Ошибка: " + result.Error;
                _notify.Error(tweak.Name, result.Error ?? "Не удалось выполнить");
            }
        }
        catch (Exception ex)
        {
            tweak.SetAppliedSilently(false, false);
            StatusText = "Ошибка: " + ex.Message;
            _notify.Error(tweak.Name, ex.Message);
        }
        finally
        {
            IsBusy = false;
            UpdatePendingRebootCount();
            await Task.CompletedTask;
        }
    }

    // ------------------------------------------------------------------
    //  Пресеты
    // ------------------------------------------------------------------

    partial void OnSelectedPresetChanged(OptimizePresetViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedPreset));
        ClearPresetCommand.NotifyCanExecuteChanged();
        ApplyPresetCommand.NotifyCanExecuteChanged();

        if (value is null)
        {
            PresetNoteText = "";
            ConflictText = "";
            OnPropertyChanged(nameof(HasPresetNote));
            OnPropertyChanged(nameof(HasConflict));
            return;
        }

        PresetNoteText = value.Key switch
        {
            "safe" => "Включит все твики уровня «безопасно». Безопасный пресет для любого ПК.",
            _ => $"Пресет «{value.Name}». Нажмите «Применить пресет», чтобы включить все его твики."
        };

        var conflictKeys = ResolvePresetKeys(value);
        var conflicts = _presets.FindConflicts(conflictKeys).ToList();
        ConflictText = conflicts.Count == 0
            ? ""
            : string.Join("  ",
                conflicts.Select(c => _loc.Has(c.MessageKey) ? _loc[c.MessageKey] : $"Конфликт: {c.A} ↔ {c.B}"));

        OnPropertyChanged(nameof(HasPresetNote));
        OnPropertyChanged(nameof(HasConflict));
    }

    private IReadOnlyList<string> ResolvePresetKeys(OptimizePresetViewModel preset)
    {
        if (preset.Key == "safe" && preset.Keys.Count == 0)
        {
            return _tweaks.Tweaks
                .Where(t => string.Equals(t.Level, "safe", StringComparison.OrdinalIgnoreCase))
                .Select(t => t.Key)
                .ToList();
        }
        return preset.Keys;
    }

    // ------------------------------------------------------------------
    //  Команды
    // ------------------------------------------------------------------

    private static bool ConfirmTweak(OptimizeTweakViewModel tweak)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"«{tweak.Name}»");
        sb.AppendLine($"Уровень риска: {tweak.LevelLabel}.");

        if (tweak.HasWarn)
        {
            sb.AppendLine();
            sb.AppendLine(tweak.Warn);
        }

        if (tweak.RequiresReboot)
        {
            sb.AppendLine();
            sb.AppendLine("Для применения потребуется перезагрузка.");
        }

        sb.AppendLine();
        sb.AppendLine("Перед изменением будет создана точка восстановления Windows (если включена защита системы). " +
                      "Изменение можно отключить этим же переключателем.");
        sb.AppendLine();
        sb.Append("Применить?");

        return System.Windows.MessageBox.Show(
            sb.ToString(),
            "Подтверждение изменения",
            System.Windows.MessageBoxButton.YesNo,
            string.Equals(tweak.Level, "danger", StringComparison.OrdinalIgnoreCase)
                ? System.Windows.MessageBoxImage.Warning
                : System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
    }

    private static bool ConfirmPreset(string presetName, int total, int risky)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"Пресет «{presetName}»: будет применено твиков — {total}.");

        if (risky > 0)
        {
            sb.AppendLine($"Из них с повышенным риском: {risky}.");
            sb.AppendLine();
            sb.AppendLine("Перед применением будет создана точка восстановления Windows (если включена защита системы).");
        }

        sb.AppendLine();
        sb.AppendLine("Любой твик потом можно отключить отдельно, а кнопка отката вернёт исходные значения.");
        sb.AppendLine();
        sb.Append("Применить пресет?");

        return System.Windows.MessageBox.Show(
            sb.ToString(),
            "Подтверждение пресета",
            System.Windows.MessageBoxButton.YesNo,
            risky > 0
                ? System.Windows.MessageBoxImage.Warning
                : System.Windows.MessageBoxImage.Question) == System.Windows.MessageBoxResult.Yes;
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ApplyPresetAsync()
    {
        if (SelectedPreset is null) return;

        var keys = ResolvePresetKeys(SelectedPreset);
        if (keys.Count == 0)
        {
            StatusText = "Пресет пуст";
            return;
        }

        var riskyCount = keys.Count(k =>
        {
            var d = _tweaks.GetByKey(k);
            return d != null && !string.Equals(d.Level, "safe", StringComparison.OrdinalIgnoreCase);
        });

        if (!ConfirmPreset(SelectedPreset.Name, keys.Count, riskyCount))
        {
            StatusText = "Отменено: ничего не изменено";
            return;
        }

        IsBusy = true;
        int ok = 0, failed = 0;
        try
        {
            if (riskyCount > 0)
            {
                StatusText = "Создаём точку восстановления Windows...";
                await SessionRestorePoint.EnsureAsync($"перед применением пресета «{SelectedPreset.Name}»");
            }

            foreach (var key in keys)
            {
                var tw = FindTweak(key);
                if (tw is null) { failed++; continue; }

                tw.IsBusy = true;
                try
                {
                    var result = await Task.Run(() => _optimize.Apply(key));
                    if (result.Success)
                    {
                        tw.SetAppliedSilently(true, _optimize.IsPendingReboot(key));
                        ok++;
                    }
                    else { failed++; }
                }
                finally { tw.IsBusy = false; }
            }

            StatusText = $"Пресет «{SelectedPreset.Name}»: применено {ok}, ошибок {failed}";

            if (failed == 0)
                _notify.Success($"Пресет «{SelectedPreset.Name}»", $"Применено твиков: {ok}");
            else
                _notify.Warning($"Пресет «{SelectedPreset.Name}»", $"Успешно: {ok}, ошибок: {failed}");

            await Task.CompletedTask;
        }
        finally
        {
            IsBusy = false;
            UpdatePendingRebootCount();
            RefreshAllCounters();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RollbackAllAsync()
    {
        IsBusy = true;
        try
        {
            var (restored, failed) = await Task.Run(() => _optimize.RollbackAll());
            StatusText = $"Откат: восстановлено {restored}, ошибок {failed}";

            if (failed == 0)
                _notify.Success("Откат выполнен", $"Восстановлено: {restored}");
            else
                _notify.Error("Откат с ошибками", $"Восстановлено: {restored}, ошибок: {failed}");
        }
        finally
        {
            IsBusy = false;
            RefreshAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void Refresh()
    {
        RefreshAll();
        StatusText = "Состояние обновлено";
    }

    [RelayCommand]
    private void ClearPreset()
    {
        SelectedPreset = null;
        PresetNoteText = "";
        ConflictText = "";
        OnPropertyChanged(nameof(HasPresetNote));
        OnPropertyChanged(nameof(HasConflict));
        StatusText = "Пресет не выбран";
    }

    private bool CanRun() => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        ApplyPresetCommand.NotifyCanExecuteChanged();
        RollbackAllCommand.NotifyCanExecuteChanged();
        RefreshCommand.NotifyCanExecuteChanged();
    }

    // ------------------------------------------------------------------
    //  Helpers
    // ------------------------------------------------------------------

    private void RefreshAll()
    {
        foreach (var cat in Categories)
        {
            foreach (var tw in cat.Tweaks)
            {
                tw.SetAppliedSilently(
                    _optimize.IsAppliedByCleaner(tw.Key),
                    _optimize.IsPendingReboot(tw.Key));
            }
            cat.RefreshCounters();
        }
        UpdatePendingRebootCount();
    }

    private void RefreshAllCounters()
    {
        foreach (var cat in Categories)
            cat.RefreshCounters();
    }

    private void UpdatePendingRebootCount()
    {
        PendingRebootCount = Categories
            .SelectMany(c => c.Tweaks)
            .Count(t => t.IsPendingReboot);

        OnPropertyChanged(nameof(HasPendingReboot));
        OnPropertyChanged(nameof(PendingRebootText));
    }

    private OptimizeTweakViewModel? FindTweak(string key)
        => Categories
            .SelectMany(c => c.Tweaks)
            .FirstOrDefault(t => string.Equals(t.Key, key, StringComparison.OrdinalIgnoreCase));
}

public sealed class OptimizePresetViewModel
{
    public string Key { get; }
    public string Name { get; }
    public IReadOnlyList<string> Keys { get; }

    public OptimizePresetViewModel(string key, string name, IReadOnlyList<string> keys)
    {
        Key = key;
        Name = name;
        Keys = keys;
    }
}