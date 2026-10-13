using Cleaner.App.Localization;
using Cleaner.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;
using Cleaner.Core.Services;

namespace Cleaner.App.ViewModels;

public sealed record SettingsChoice(string Key, string Label);

public partial class SettingsViewModel : ObservableObject
{
    [ObservableProperty] private bool _maintenanceBusy;
    [ObservableProperty] private string _maintenanceStatus = "";
    private bool CanMaintainLogs() => !MaintenanceBusy;
    partial void OnMaintenanceBusyChanged(bool value) => ClearOldLogsCommand.NotifyCanExecuteChanged();
    [RelayCommand(CanExecute = nameof(CanMaintainLogs))]
    private async Task ClearOldLogsAsync()
    {
        MaintenanceBusy = true;
        try
        {
            var maintenance = new ApplicationLogMaintenance(AppLog.LogDirectory);
            var plan = await Task.Run(maintenance.Analyze);
            if (plan.Count == 0) { MaintenanceStatus = Loc.T("settings.storage.empty"); return; }
            if (!await Dialogs.ConfirmAsync(Loc.T("settings.storage.title"), Loc.F("settings.storage.confirm", plan.Count, CleanupDetailRow.FormatBytes(plan.Sum(f => f.Identity.Length))))) return;
            var result = await Task.Run(() => maintenance.Apply(plan));
            MaintenanceStatus = Loc.F("settings.storage.result", result.Deleted, CleanupDetailRow.FormatBytes(result.BytesDeleted), result.Skipped);
        }
        catch (Exception error) { MaintenanceStatus = Loc.T("t.oshibka") + error.Message; }
        finally { MaintenanceBusy = false; }
    }
    [ObservableProperty] private string _updateStatus = "";
    [RelayCommand] private async Task CheckUpdatesAsync()
    {
        UpdateStatus = Loc.T("updates.checking");
        try { var newer = await ReleaseUpdates.FindNewerAsync(AppVersion.Current); UpdateStatus = newer == null ? Loc.T("updates.current") : Loc.F("updates.available", newer); }
        catch { UpdateStatus = Loc.T("updates.failed"); }
    }
    [RelayCommand] private void OpenReleases()
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(ReleaseUpdates.OfficialPage) { UseShellExecute = true }); }
        catch { UpdateStatus = Loc.T("updates.failed"); }
    }
    [ObservableProperty] private bool _expertMode = UserSettings.GetExpertMode();
    private bool _restoringMode;
    partial void OnExpertModeChanged(bool value)
    {
        if (_restoringMode) return;
        if (!UserSettings.SetExpertMode(value)) { _restoringMode = true; ExpertMode = !value; _restoringMode = false; }
        Saved();
    }
    private readonly IStartupRegistration? _startup;
    private bool _restoringAutomation;
    public IReadOnlyList<SettingsChoice> CleanupIntervals { get; } = new[]
    { new SettingsChoice("60", Loc.T("automation.hour")), new SettingsChoice("360", Loc.T("automation.six.hours")),
      new SettingsChoice("1440", Loc.T("automation.day")), new SettingsChoice("10080", Loc.T("automation.week")) };
    [ObservableProperty] private bool _autoCleanupEnabled = UserSettings.GetAutoCleanupEnabled();
    [ObservableProperty] private string _autoCleanupInterval = UserSettings.GetAutoCleanupInterval().ToString(System.Globalization.CultureInfo.InvariantCulture);
    [ObservableProperty] private bool _startWithWindows;
    [ObservableProperty] private string _automationStatus = Loc.T("automation.off");
    partial void OnAutoCleanupEnabledChanged(bool value) => SaveAutomation();
    partial void OnAutoCleanupIntervalChanged(string value) => SaveAutomation();
    private void SaveAutomation()
    {
        if (_restoringAutomation) return;
        if (!int.TryParse(AutoCleanupInterval, out var minutes) || !UserSettings.SetAutoCleanup(AutoCleanupEnabled, minutes))
        {
            _restoringAutomation = true;
            AutoCleanupEnabled = UserSettings.GetAutoCleanupEnabled();
            AutoCleanupInterval = UserSettings.GetAutoCleanupInterval().ToString(System.Globalization.CultureInfo.InvariantCulture);
            _restoringAutomation = false;
        }
        Saved();
    }
    partial void OnStartWithWindowsChanged(bool value)
    {
        if (_restoringAutomation || _startup == null) return;
        try { _startup.SetEnabled(value); Saved(); }
        catch (Exception ex)
        {
            _restoringAutomation = true; StartWithWindows = !value; _restoringAutomation = false;
            StatusText = Loc.T("startup.registration.failed") + " " + Loc.T(ex.Message);
        }
    }
    public IReadOnlyList<SettingsChoice> Languages { get; } = new[] { new SettingsChoice("auto", Loc.T("settings.language.auto")), new SettingsChoice("ru", "Русский"), new SettingsChoice("en", "English") };
    public IReadOnlyList<SettingsChoice> Accents { get; } = new[]
    {
        new SettingsChoice("blue", Loc.T("settings.accent.blue")),
        new SettingsChoice("teal", Loc.T("settings.accent.teal")),
        new SettingsChoice("violet", Loc.T("settings.accent.violet"))
    };
    public IReadOnlyList<SettingsChoice> StartPages { get; } = new[]
    {
        new SettingsChoice("cleanup", Loc.T("t.ochistka")), new SettingsChoice("optimize", Loc.T("t.optimizaciya")),
        new SettingsChoice("startup", Loc.T("ui.startup.nav")), new SettingsChoice("system", Loc.T("t.sistema")),
        new SettingsChoice("privacy", Loc.T("design.recovery")), new SettingsChoice("disk", Loc.T("t.disk")), new SettingsChoice("history", Loc.T("history.title")), new SettingsChoice("whatsnew", Loc.T("news.title"))
    };
    public IReadOnlyList<SettingsChoice> Densities { get; } = new[] { new SettingsChoice("compact", Loc.T("settings.density.compact")), new SettingsChoice("comfortable", Loc.T("settings.density.comfortable")) };
    [ObservableProperty] private string _density = UserSettings.GetDensity();
    partial void OnDensityChanged(string value) { UserSettings.SetDensity(value); Appearance.Apply(); Saved(); }
    [ObservableProperty] private string _language = UserSettings.GetLanguagePreference();
    [ObservableProperty] private string _accent = UserSettings.GetAccent();
    [ObservableProperty] private string _startPage = UserSettings.GetStartPage();
    [ObservableProperty] private bool _animations = UserSettings.GetAnimations();
    [ObservableProperty] private bool _showMetrics = UserSettings.GetShowMetrics();
    [ObservableProperty] private string _statusText = Loc.T("settings.saved.auto");
    public bool RestartRequired => UserSettings.GetLanguage() != Loc.Language;
    public Action? ShowSafety { get; set; }
    [RelayCommand]
    private void ShowSafetyNotice() => ShowSafety?.Invoke();
    public string VersionText => "Cleaner · " + AppVersion.Current;
    public string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Cleaner");
    public string LogDirectory => AppLog.LogDirectory;
    public SettingsViewModel(IStartupRegistration? startup = null)
    {
        _startup = startup;
        try { _startWithWindows = startup?.IsEnabled == true; }
        catch (Exception ex) { StatusText = Loc.T("startup.registration.failed") + " " + ex.Message; return; }
        Saved();
    }

    partial void OnLanguageChanged(string value)
    {
        UserSettings.SetLanguage(value);
        OnPropertyChanged(nameof(RestartRequired));
        Saved();
    }
    partial void OnAccentChanged(string value) => SaveAppearance();
    partial void OnAnimationsChanged(bool value) => SaveAppearance();
    partial void OnShowMetricsChanged(bool value) => SaveAppearance();
    partial void OnStartPageChanged(string value) { UserSettings.SetStartPage(value); Saved(); }
    private void SaveAppearance()
    {
        UserSettings.SetAppearance(Accent, Animations, ShowMetrics);
        Appearance.Apply();
        Saved();
    }
    private void Saved() => StatusText = UserSettings.LastSaveError is null
        ? Loc.T("settings.saved.auto") : Loc.T("settings.save.failed") + " " + Loc.T(UserSettings.LastSaveError);
    [RelayCommand]
    private void ResetHints() { UserSettings.ResetHints(); Saved(); if (UserSettings.LastSaveError is null) StatusText = Loc.T("settings.hints.reset.done"); }
}
