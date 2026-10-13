using Cleaner.App.Localization;
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

public partial class OptimizeViewModel : HelpViewModelBase
{
    protected override string HelpKey => "optimize";

    public string SubtitleText => Loc.F("ui.optimize.subtitle", _tweaks.Tweaks.Count);

    private readonly IOptimizeService _optimize;
    private readonly TweakRepository _tweaks;
    private readonly PresetsRepository _presets;
    private readonly LocalizationRepository _loc;
    private readonly IAppNotifications _notify;

    public ObservableCollection<OptimizePresetViewModel> Presets { get; } = new();
    public ObservableCollection<OptimizeCategoryViewModel> Categories { get; } = new();
    [ObservableProperty] private string _searchText = "";
    [ObservableProperty] private bool _appliedOnly;
    public bool HasNoSearchResults => Categories.All(c => !c.HasVisibleTweaks);
    partial void OnSearchTextChanged(string value) => ApplySearch();
    partial void OnAppliedOnlyChanged(bool value) => ApplySearch();
    private void ApplySearch()
    {
        var query = SearchText.Trim();
        foreach (var category in Categories)
        {
            foreach (var tweak in category.Tweaks)
                tweak.IsVisible = (!AppliedOnly || tweak.IsApplied) &&
                    (query.Length == 0 || (tweak.Name + " " + tweak.Short + " " + tweak.Description + " " + tweak.Key)
                        .Contains(query, StringComparison.OrdinalIgnoreCase));
            category.RefreshCounters();
        }
        OnPropertyChanged(nameof(HasNoSearchResults));
    }

    [ObservableProperty]
    private OptimizePresetViewModel? _selectedPreset;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _statusText = Loc.T("t.gotovo");

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
        : Loc.F("t.trebuetsya_perezagruzka", PendingRebootCount);

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
                    ApplySearch();
                }
            };
            catVm.Tweaks.Add(tw);
        }
    }

    private static string CategoryName(string key) => key switch
    {
        "perf" => Loc.T("t.proizvoditelnost"),
        "privacy" => Loc.T("t.privatnost"),
        "win11" => "Windows 11",
        "network" => Loc.T("t.set"),
        "maint" => Loc.T("t.obsluzhivanie"),
        "games" => Loc.T("t.igry"),
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
        if (IsBusy) { RefreshAll(); return; }
        IsBusy = true;
        try
        {
            if (apply && !string.Equals(tweak.Level, "safe", StringComparison.OrdinalIgnoreCase))
            {
                if (!await ConfirmTweakAsync(tweak))
                {
                    tweak.SetAppliedSilently(false, false);
                    StatusText = Loc.F("t.otmeneno_2", tweak.Name);
                    return;
                }

                StatusText = Loc.T("t.sozdaem_tochku_vosstanovleniya_windows");
                if (!await SessionRestorePoint.EnsureAsync(Loc.F("t.pered_primeneniem", tweak.Name)))
                { RefreshAll(); return; }
            }

            var result = await Task.Run(() => apply ? _optimize.Apply(tweak.Key) : _optimize.Unapply(tweak.Key));

            if (result.Success)
            {
                tweak.LastError = null;
                tweak.SetAppliedSilently(
                    _optimize.IsAppliedByCleaner(tweak.Key),
                    _optimize.IsPendingReboot(tweak.Key));
                StatusText = apply ? Loc.F("t.primeneno", tweak.Name) : Loc.F("t.otklyucheno", tweak.Name);
                _notify.Success(tweak.Name, apply ? Loc.T("t.tvik_primenen") : Loc.T("t.tvik_otklyuchen"));
            }
            else
            {
                tweak.LastError = result.Error == null ? null : Loc.T(result.Error);
                tweak.SetAppliedSilently(
                    _optimize.IsAppliedByCleaner(tweak.Key),
                    _optimize.IsPendingReboot(tweak.Key));

                StatusText = Loc.T("t.oshibka") + tweak.LastError;
                _notify.Error(tweak.Name, tweak.LastError ?? Loc.T("t.ne_udalos_vypolnit"));
            }
        }
        catch (Exception ex)
        {
            RefreshAll();
            StatusText = Loc.T("t.oshibka") + ex.Message;
            _notify.Error(tweak.Name, ex.Message);
        }
        finally
        {
            RefreshAll();
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
            "safe" => Loc.T("t.vklyuchit_vse_tviki_urovnya_bezopasno_bezopa"),
            _ => Loc.F("t.preset_nazhmite_primenit_preset_chtoby_vklyu", value.Name)
        };

        var conflictKeys = ResolvePresetKeys(value);
        var conflicts = _presets.FindConflicts(conflictKeys).ToList();
        ConflictText = conflicts.Count == 0
            ? ""
            : string.Join("  ",
                conflicts.Select(c => _loc.Has(c.MessageKey) ? _loc[c.MessageKey] : Loc.F("t.konflikt", c.A, c.B)));

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

    private static async Task<bool> ConfirmTweakAsync(OptimizeTweakViewModel tweak)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"«{tweak.Name}»");
        sb.AppendLine(Loc.F("t.uroven_riska", tweak.LevelLabel));

        if (tweak.HasWarn)
        {
            sb.AppendLine();
            sb.AppendLine(tweak.Warn);
        }

        if (tweak.RequiresReboot)
        {
            sb.AppendLine();
            sb.AppendLine(Loc.T("t.dlya_primeneniya_potrebuetsya_perezagruzka"));
        }

        sb.AppendLine();
        sb.AppendLine(Loc.T("t.pered_izmeneniem_budet_sozdana_tochka_vossta") +
                      Loc.T("t.izmenenie_mozhno_otklyuchit_etim_zhe_perekly"));
        sb.AppendLine();
        sb.Append(Loc.T("t.primenit"));

        return await Dialogs.ConfirmAsync(
            Loc.T("t.podtverzhdenie_izmeneniya"),
            sb.ToString(),
            string.Equals(tweak.Level, "danger", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<bool> ConfirmPresetAsync(string presetName, int total, int risky)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine(Loc.F("t.preset_budet_primeneno_tvikov", presetName, total));

        if (risky > 0)
        {
            sb.AppendLine(Loc.F("t.iz_nih_s_povyshennym_riskom", risky));
            sb.AppendLine();
            sb.AppendLine(Loc.T("t.pered_primeneniem_budet_sozdana_tochka_vosst"));
        }

        sb.AppendLine();
        sb.AppendLine(Loc.T("t.lyuboy_tvik_potom_mozhno_otklyuchit_otdelno"));
        sb.AppendLine();
        sb.Append(Loc.T("t.primenit_preset"));

        return await Dialogs.ConfirmAsync(Loc.T("t.podtverzhdenie_preseta"), sb.ToString(), risky > 0);
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task ApplyPresetAsync()
    {
        if (SelectedPreset is null || IsBusy) return;
        var preset = SelectedPreset;

        var keys = ResolvePresetKeys(preset);
        if (keys.Count == 0)
        {
            StatusText = Loc.T("t.preset_pust");
            return;
        }

        var riskyCount = keys.Count(k =>
        {
            var d = _tweaks.GetByKey(k);
            return d != null && !string.Equals(d.Level, "safe", StringComparison.OrdinalIgnoreCase);
        });

        IsBusy = true;
        int ok = 0, failed = 0;
        try
        {
            if (!await ConfirmPresetAsync(preset.Name, keys.Count, riskyCount))
            {
                StatusText = Loc.T("t.otmeneno_nichego_ne_izmeneno");
                return;
            }
            if (riskyCount > 0)
            {
                StatusText = Loc.T("t.sozdaem_tochku_vosstanovleniya_windows");
                if (!await SessionRestorePoint.EnsureAsync(Loc.F("t.pered_primeneniem_preseta", preset.Name))) return;
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
                    else { failed++; tw.LastError = result.Error == null ? null : Loc.T(result.Error); }
                }
                finally { tw.IsBusy = false; }
            }

            StatusText = Loc.F("t.preset_primeneno_oshibok", preset.Name, ok, failed);

            if (failed == 0)
                _notify.Success(Loc.F("t.preset_2", preset.Name), Loc.F("t.primeneno_tvikov", ok));
            else
                _notify.Warning(Loc.F("t.preset_2", preset.Name), Loc.F("t.uspeshno_oshibok", ok, failed));

            await Task.CompletedTask;
        }
        finally
        {
            RefreshAll();
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
            StatusText = Loc.F("t.otkat_vosstanovleno_oshibok", restored, failed);

            if (failed == 0)
                _notify.Success(Loc.T("t.otkat_vypolnen"), Loc.F("t.vosstanovleno", restored));
            else
                _notify.Error(Loc.T("t.otkat_s_oshibkami"), Loc.F("t.vosstanovleno_oshibok", restored, failed));
        }
        finally
        {
            IsBusy = false;
            RefreshAll();
        }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            RefreshAll();
            var owned = Categories.SelectMany(c => c.Tweaks).Where(t => t.IsApplied && !t.IsPendingReboot).ToList();
            var matches = await Task.Run(() => owned.ToDictionary(t => t.Key, t => _optimize.GetState(t.Key)));
            foreach (var tweak in owned)
                tweak.LastError = matches[tweak.Key] ? null : Loc.T("optimize.state.drift");
            StatusText = Loc.T("t.sostoyanie_obnovleno");
        }
        catch (Exception ex) { StatusText = Loc.T("t.oshibka") + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private void ClearPreset()
    {
        SelectedPreset = null;
        PresetNoteText = "";
        ConflictText = "";
        OnPropertyChanged(nameof(HasPresetNote));
        OnPropertyChanged(nameof(HasConflict));
        StatusText = Loc.T("t.preset_ne_vybran");
    }

    private bool CanRun() => !IsBusy;

    partial void OnIsBusyChanged(bool value)
    {
        foreach (var tw in Categories.SelectMany(c => c.Tweaks)) tw.IsOperationBusy = value;
        ClearPresetCommand.NotifyCanExecuteChanged();
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
