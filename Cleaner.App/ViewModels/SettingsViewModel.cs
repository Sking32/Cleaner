using Cleaner.App.Localization;
using Cleaner.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.IO;

namespace Cleaner.App.ViewModels;

public sealed record SettingsChoice(string Key, string Label);

public partial class SettingsViewModel : ObservableObject
{
    public IReadOnlyList<SettingsChoice> Languages { get; } = new[] { new SettingsChoice("ru", "Русский"), new SettingsChoice("en", "English") };
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
        new SettingsChoice("privacy", Loc.T("design.recovery")), new SettingsChoice("disk", Loc.T("t.disk"))
    };
    [ObservableProperty] private string _language = UserSettings.GetLanguage();
    [ObservableProperty] private string _accent = UserSettings.GetAccent();
    [ObservableProperty] private string _startPage = UserSettings.GetStartPage();
    [ObservableProperty] private bool _animations = UserSettings.GetAnimations();
    [ObservableProperty] private bool _showMetrics = UserSettings.GetShowMetrics();
    [ObservableProperty] private string _statusText = Loc.T("settings.saved.auto");
    public bool RestartRequired => Language != Loc.Language;
    public string VersionText => "Cleaner · " + (typeof(App).Assembly.GetName().Version?.ToString(3) ?? "1.0.0");
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
