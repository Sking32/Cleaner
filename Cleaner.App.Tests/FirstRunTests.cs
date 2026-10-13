using System.Globalization;
using Cleaner.App.Services;
using Cleaner.App.ViewModels;
using Cleaner.Core.Services;

namespace Cleaner.App.Tests;

[Collection("Preferences")]
public sealed class FirstRunTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CleanerFirstRun_" + Guid.NewGuid().ToString("N"));
    public FirstRunTests()
    {
        Directory.CreateDirectory(_root);
        AppLog.UsePreviewDirectory(Path.Combine(_root, "logs"));
        UserSettings.UsePreviewStore(new AppSettingsStore(Path.Combine(_root, "settings.json")));
    }

    [Theory]
    [InlineData(null, "ru-RU", "ru")]
    [InlineData(null, "en-US", "en")]
    [InlineData(null, "en-GB", "en")]
    [InlineData(null, "fr-FR", "en")]
    [InlineData("auto", "ru-RU", "ru")]
    [InlineData("en", "ru-RU", "en")]
    [InlineData("ru", "en-US", "ru")]
    [InlineData("invalid", "ru-RU", "ru")]
    public void LanguageFollowsWindowsDisplayCultureUnlessOverridden(string? choice, string culture, string expected) =>
        Assert.Equal(expected, AppLanguage.Resolve(choice, CultureInfo.GetCultureInfo(culture)));

    [Fact]
    public void AutomaticLanguageIsNotTurnedIntoAManualChoice()
    {
        Assert.Equal("auto", UserSettings.GetLanguagePreference());
        UserSettings.SetLanguage("ru"); Assert.Equal("ru", UserSettings.GetLanguagePreference());
        UserSettings.SetLanguage("auto");
        Assert.Null(new AppSettingsStore(Path.Combine(_root, "settings.json")).Load().Language);
        Assert.Equal(AppLanguage.Resolve(null, CultureInfo.CurrentUICulture), UserSettings.GetLanguage());
    }

    [Fact]
    public void SafetyAcknowledgementPersistsOnlyAfterExplicitContinue()
    {
        Assert.True(UserSettings.NeedsSafetyNotice);
        Assert.True(UserSettings.AcknowledgeSafetyNotice());
        UserSettings.UsePreviewStore(new AppSettingsStore(Path.Combine(_root, "settings.json")));
        Assert.False(UserSettings.NeedsSafetyNotice);
        UserSettings.ResetHints(); Assert.False(UserSettings.NeedsSafetyNotice);
    }

    [Fact]
    public void LegacySettingsShowTheNoticeWithoutLosingLanguageOrHistory()
    {
        var path = Path.Combine(_root, "legacy.json");
        File.WriteAllText(path, "{\"Language\":\"en\",\"History\":[{\"Kind\":\"preview\"}]}");
        UserSettings.UsePreviewStore(new AppSettingsStore(path));
        Assert.True(UserSettings.NeedsSafetyNotice);
        Assert.True(UserSettings.AcknowledgeSafetyNotice());
        var saved = new AppSettingsStore(path).Load();
        Assert.Equal("en", saved.Language); Assert.Single(saved.History); Assert.Equal(1, saved.SafetyNoticeVersion);
    }

    [Fact]
    public void CorruptSettingsDoNotBecomeAcknowledgedOrGetOverwritten()
    {
        var path = Path.Combine(_root, "corrupt.json"); File.WriteAllText(path, "broken");
        UserSettings.UsePreviewStore(new AppSettingsStore(path));
        Assert.False(UserSettings.AcknowledgeSafetyNotice());
        Assert.True(UserSettings.NeedsSafetyNotice); Assert.Equal("broken", File.ReadAllText(path));
    }

    [Fact]
    public void OpeningAndContinuingNeverCreatesAPointOrRequestsElevation()
    {
        var points = 0; var restarts = 0; var completed = 0;
        var vm = new FirstRunSafetyViewModel(() => false, () => { restarts++; return Task.FromResult(true); },
            () => { points++; return Task.FromResult(new RestorePointResult(true, "test")); }, UserSettings.AcknowledgeSafetyNotice);
        vm.Completed += () => completed++;
        Assert.True(UserSettings.NeedsSafetyNotice);
        vm.ContinueCommand.Execute(null);
        Assert.Equal(0, points); Assert.Equal(0, restarts); Assert.Equal(1, completed); Assert.False(UserSettings.NeedsSafetyNotice);
    }

    [Fact]
    public async Task SuccessfulPointDoesNotSilentlyDismissTheNoticeOrCreateTwice()
    {
        var points = 0; var acknowledged = 0;
        var vm = new FirstRunSafetyViewModel(() => true, () => throw new Exception("No elevation should run"),
            () => { points++; return Task.FromResult(new RestorePointResult(true, "test")); }, () => { acknowledged++; return true; });
        await vm.CreateRestorePointCommand.ExecuteAsync(null);
        Assert.True(vm.HasRestorePoint); Assert.False(vm.CreateRestorePointCommand.CanExecute(null));
        await vm.CreateRestorePointCommand.ExecuteAsync(null);
        Assert.Equal(1, points); Assert.Equal(0, acknowledged);
    }

    [Theory]
    [InlineData(true)] [InlineData(false)]
    public async Task ElevationOrItsCancellationNeverCreatesAPointInTheCurrentProcess(bool restarted)
    {
        var restarts = 0; var acknowledged = 0;
        var vm = new FirstRunSafetyViewModel(() => false, () => { restarts++; return Task.FromResult(restarted); },
            () => throw new Exception("Creation must wait for elevated relaunch"), () => { acknowledged++; return true; });
        await vm.CreateRestorePointCommand.ExecuteAsync(null);
        Assert.Equal(1, restarts); Assert.Equal(0, acknowledged); Assert.False(vm.HasRestorePoint); Assert.False(vm.IsBusy);
        Assert.NotEmpty(vm.StatusText);
    }

    [Fact]
    public async Task FailedPointCanBeRetriedWithoutAcknowledgingTheNotice()
    {
        var points = 0;
        var vm = new FirstRunSafetyViewModel(() => true, () => throw new Exception(),
            () => Task.FromResult(new RestorePointResult(++points > 1, "test")), () => throw new Exception("No acknowledgement expected"));
        await vm.CreateRestorePointCommand.ExecuteAsync(null);
        Assert.False(vm.HasRestorePoint); Assert.True(vm.CreateRestorePointCommand.CanExecute(null));
        await vm.CreateRestorePointCommand.ExecuteAsync(null); Assert.True(vm.HasRestorePoint); Assert.Equal(2, points);
    }

    [Fact]
    public async Task BusyNoticeCannotContinueOrStartAnotherPoint()
    {
        var pending = new TaskCompletionSource<RestorePointResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var points = 0; var acknowledgements = 0;
        var vm = new FirstRunSafetyViewModel(() => true, () => throw new Exception(),
            () => { points++; return pending.Task; }, () => { acknowledgements++; return true; });
        var task = vm.CreateRestorePointCommand.ExecuteAsync(null);
        Assert.True(vm.IsBusy); Assert.False(vm.ContinueCommand.CanExecute(null));
        vm.ContinueCommand.Execute(null);
        await vm.CreateRestorePointCommand.ExecuteAsync(null);
        Assert.Equal(0, acknowledgements); Assert.Equal(1, points);
        pending.SetResult(new RestorePointResult(true, "test")); await task;
        Assert.False(vm.IsBusy); Assert.True(vm.ContinueCommand.CanExecute(null));
    }

    [Fact]
    public async Task ExceptionsAndSaveFailuresKeepTheNoticeOpen()
    {
        var completed = 0;
        var vm = new FirstRunSafetyViewModel(() => true, () => throw new Exception(),
            () => throw new IOException("fixture failure"), () => false);
        vm.Completed += () => completed++;
        await vm.CreateRestorePointCommand.ExecuteAsync(null); vm.ContinueCommand.Execute(null);
        Assert.False(vm.IsBusy); Assert.False(vm.HasRestorePoint); Assert.Equal(0, completed); Assert.NotEmpty(vm.StatusText);
    }
}
