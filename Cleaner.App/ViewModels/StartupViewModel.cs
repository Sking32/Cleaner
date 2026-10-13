using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Cleaner.App.Localization;
using Cleaner.App.Services;
using Cleaner.Core.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.ComponentModel;
using System.Windows.Data;

namespace Cleaner.App.ViewModels;

public partial class StartupViewModel : HelpViewModelBase
{
    protected override string HelpKey => "startup";

    public ObservableCollection<StartupItem> Entries { get; } = new();
    public ICollectionView FilteredEntries { get; }
    [ObservableProperty] private string _searchText = "";
    partial void OnSearchTextChanged(string value)
    {
        FilteredEntries.Refresh();
        OnPropertyChanged(nameof(HasNoEntries));
        OnPropertyChanged(nameof(EmptyText));
    }
    public ObservableCollection<PowerPlanItem> Plans { get; } = new();

    [ObservableProperty] private string _statusText = Loc.T("t.gotovo");
    [ObservableProperty] private bool _isBusy;

    public bool HasEntries => Entries.Count > 0;
    public bool HasNoEntries => FilteredEntries.IsEmpty;
    public string EmptyText => Loc.T(string.IsNullOrWhiteSpace(SearchText) ? "ui.startup.empty" : "design.no.results");
    public bool HasPlans => Plans.Count > 0;
    public bool HasNoPlans => Plans.Count == 0;

    public StartupViewModel()
    {
        FilteredEntries = CollectionViewSource.GetDefaultView(Entries);
        FilteredEntries.Filter = item => item is StartupItem entry &&
            (SearchText.Trim().Length == 0 || (entry.Name + " " + entry.Command + " " + entry.SourceText)
                .Contains(SearchText.Trim(), StringComparison.OrdinalIgnoreCase));
        _ = RefreshAllAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private Task RefreshAsync() => RefreshAllAsync();
    private bool CanRun() => !IsBusy;
    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        SetPlanCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshAllAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            var entries = await Task.Run(() => StartupService.GetEntries());
            var plans = await Task.Run(() => PowerPlanService.GetPlans());

            Entries.Clear();
            foreach (var e in entries) Entries.Add(new StartupItem(e, this));

            Plans.Clear();
            foreach (var p in plans) Plans.Add(new PowerPlanItem(p));

            StatusText = Loc.T("t.sostoyanie_obnovleno");
        }
        catch (Exception ex)
        {
            CoreLog.Warn("Автозагрузка: ошибка обновления", ex);
            StatusText = Loc.T("t.oshibka") + ex.Message;
        }
        finally
        {
            IsBusy = false;
            OnPropertyChanged(nameof(HasEntries));
            OnPropertyChanged(nameof(HasNoEntries));
            OnPropertyChanged(nameof(HasPlans));
            OnPropertyChanged(nameof(HasNoPlans));
        }
    }

    internal async Task ToggleAsync(StartupItem item, bool enable, bool previous)
    {
        if (IsBusy) { item.SetSilently(previous); return; }
        IsBusy = true;
        try
        {
        var ok = await Task.Run(() => StartupService.SetEnabled(item.Entry, enable));
        if (ok)
        {
            StatusText = Loc.F(enable ? "ui.startup.enabled" : "ui.startup.disabled", item.Name);
        }
        else
        {
            item.SetSilently(previous);
            StatusText = Loc.F("ui.startup.failed", item.Name);
        }
        }
        catch (Exception ex) { item.SetSilently(previous); StatusText = Loc.T("t.oshibka") + ex.Message; }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task SetPlanAsync(PowerPlanItem? item)
    {
        if (item is null || item.IsActive || IsBusy) return;
        IsBusy = true;
        try
        {

        var ok = await Task.Run(() => PowerPlanService.SetActive(item.Plan.Id));
        if (!ok)
        {
            StatusText = Loc.F("ui.power.failed", item.DisplayName);
            return;
        }

        var plans = await Task.Run(() => PowerPlanService.GetPlans());
        Plans.Clear();
        foreach (var p in plans) Plans.Add(new PowerPlanItem(p));
        OnPropertyChanged(nameof(HasPlans));
        OnPropertyChanged(nameof(HasNoPlans));

        StatusText = Loc.F("ui.power.activated", item.DisplayName);
        }
        catch (Exception ex) { StatusText = Loc.T("t.oshibka") + ex.Message; }
        finally { IsBusy = false; }
    }
}

public sealed partial class StartupItem : ObservableObject
{
    private readonly StartupViewModel _owner;
    private bool _isEnabled;
    private bool _suppress;

    public StartupEntry Entry { get; }

    public StartupItem(StartupEntry entry, StartupViewModel owner)
    {
        Entry = entry;
        _owner = owner;
        _isEnabled = entry.IsEnabled;
    }

    public string Name => Entry.Name;
    public string Command => Entry.Command;

    public string SourceText => Entry.Source switch
    {
        StartupSource.CurrentUserRun => Loc.T("ui.startup.source.user"),
        StartupSource.LocalMachineRun or StartupSource.LocalMachineRun32 => Loc.T("ui.startup.source.machine"),
        _ => Loc.T("ui.startup.source.folder")
    };

    public string ImpactLevel => Entry.Impact switch
    {
        StartupImpact.Low => "low",
        StartupImpact.Medium => "medium",
        StartupImpact.High => "high",
        _ => "unknown"
    };

    public string ImpactText => Entry.Impact switch
    {
        StartupImpact.Low => Loc.T("ui.startup.impact.low"),
        StartupImpact.Medium => Loc.T("ui.startup.impact.medium"),
        StartupImpact.High => Loc.T("ui.startup.impact.high"),
        _ => Loc.T("ui.startup.impact.unknown")
    };

    public string ImpactHint => Loc.T("ui.startup.impact.hint");

    public bool IsEnabled
    {
        get => _isEnabled;
        set
        {
            if (_isEnabled == value) return;

            var previous = _isEnabled;
            _isEnabled = value;
            OnPropertyChanged();

            if (!_suppress)
                _ = _owner.ToggleAsync(this, value, previous);
        }
    }

    internal void SetSilently(bool value)
    {
        _suppress = true;
        _isEnabled = value;
        OnPropertyChanged(nameof(IsEnabled));
        _suppress = false;
    }
}

public sealed class PowerPlanItem
{
    public PowerPlan Plan { get; }

    public PowerPlanItem(PowerPlan plan)
    {
        Plan = plan;
    }

    public bool IsActive => Plan.IsActive;

    public string DisplayName => Plan.Kind switch
    {
        PowerPlanKind.Balanced => Loc.T("ui.power.balanced"),
        PowerPlanKind.HighPerformance => Loc.T("ui.power.high"),
        PowerPlanKind.PowerSaver => Loc.T("ui.power.saver"),
        _ => Plan.Name
    };

    public string Description => Plan.Kind switch
    {
        PowerPlanKind.Balanced => Loc.T("ui.power.balanced.desc"),
        PowerPlanKind.HighPerformance => Loc.T("ui.power.high.desc"),
        PowerPlanKind.PowerSaver => Loc.T("ui.power.saver.desc"),
        _ => Loc.T("ui.power.custom.desc")
    };
}
