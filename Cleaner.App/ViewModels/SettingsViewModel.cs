using Cleaner.App.Localization;
using Cleaner.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;

namespace Cleaner.App.ViewModels;

public sealed record SettingsChoice(string Key, string Label);

public partial class SettingsViewModel : ObservableObject
{
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
    public SettingsViewModel() => Saved();

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
