using System.IO;
using Cleaner.App.Services;

namespace Cleaner.App.Tests;

public class AppSettingsStoreTests
{
    private static string NewPath()
    {
        var root = Path.Combine(Path.GetTempPath(), "CleanerPreferences_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return Path.Combine(root, "settings.json");
    }

    [Fact]
    public void LoadMissingFileDoesNotCreateIt()
    {
        var path = NewPath(); var data = new AppSettingsStore(path).Load();
        Assert.False(File.Exists(path)); Assert.Equal("blue", data.Accent);
        Assert.Equal("cleanup", data.StartPage); Assert.True(data.Animations); Assert.True(data.ShowMetrics);
        Assert.False(data.AutoCleanupEnabled); Assert.Equal(1440, data.AutoCleanupIntervalMinutes);
    }

    [Fact]
    public void PreferencesRoundTripPreservesHistoryAndHints()
    {
        var path = NewPath(); var store = new AppSettingsStore(path); var data = store.Load();
        data.Language = "en"; data.Accent = "teal"; data.StartPage = "privacy";
        data.Animations = false; data.ShowMetrics = false; data.DismissedHints.Add("cleanup");
        data.AutoCleanupEnabled = true; data.AutoCleanupIntervalMinutes = 360;
        data.History.Add(new CleanupHistoryEntry { Operations = 3, FilesQuarantined = 9, Canceled = true });
        Assert.True(store.Save());
        var loaded = new AppSettingsStore(path).Load();
        Assert.Equal("en", loaded.Language); Assert.Equal("teal", loaded.Accent); Assert.Equal("privacy", loaded.StartPage);
        Assert.False(loaded.Animations); Assert.False(loaded.ShowMetrics); Assert.Contains("cleanup", loaded.DismissedHints);
        Assert.True(loaded.AutoCleanupEnabled); Assert.Equal(360, loaded.AutoCleanupIntervalMinutes);
        Assert.Equal(9, Assert.Single(loaded.History).FilesQuarantined); Assert.True(loaded.History[0].Canceled);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public void LegacyDocumentGetsDefaultsWithoutLosingData()
    {
        var path = NewPath(); File.WriteAllText(path, "{\"Language\":\"ru\",\"DismissedHints\":[\"disk\"],\"History\":[{\"BytesFreed\":42}]}");
        var store = new AppSettingsStore(path); var data = store.Load();
        Assert.True(data.ShowMetrics); Assert.Equal("blue", data.Accent); Assert.True(store.Save());
        var again = new AppSettingsStore(path).Load(); Assert.Equal(42, Assert.Single(again.History).BytesFreed);
        Assert.Contains("disk", again.DismissedHints);
    }

    [Theory]
    [InlineData("null")][InlineData("")][InlineData("{broken")][InlineData("[]")][InlineData("{\"Animations\":\"invalid\"}")]
    public void DamagedDocumentIsNeverOverwritten(string original)
    {
        var path = NewPath(); File.WriteAllText(path, original); var store = new AppSettingsStore(path);
        store.Load().Accent = "violet"; Assert.False(store.Save()); Assert.Equal("settings.unreadable", store.LastError);
        Assert.Equal(original, File.ReadAllText(path));
    }

    [Fact]
    public void UnknownPreferencesAndNullListsAreNormalized()
    {
        var path = NewPath(); File.WriteAllText(path, "{\"Accent\":\"unknown\",\"StartPage\":\"delete\",\"History\":null,\"DismissedHints\":null}");
        var data = new AppSettingsStore(path).Load(); Assert.Equal("blue", data.Accent); Assert.Equal("cleanup", data.StartPage);
        Assert.Empty(data.History); Assert.Empty(data.DismissedHints);
    }

    [Fact]
    public void WriteFailureIsReportedAndCanBeRetried()
    {
        var path = NewPath(); var store = new AppSettingsStore(path); store.Load().Accent = "violet";
        Directory.CreateDirectory(path); Assert.False(store.Save()); Assert.NotNull(store.LastError);
        Directory.Delete(path); Assert.True(store.Save()); Assert.Null(store.LastError);
        Assert.Equal("violet", new AppSettingsStore(path).Load().Accent);
    }

    [Fact]
    public void InvalidAutomaticIntervalIsNormalized()
    {
        var path = NewPath(); File.WriteAllText(path, "{\"AutoCleanupIntervalMinutes\":-1}");
        var data = new AppSettingsStore(path).Load();
        Assert.False(data.AutoCleanupEnabled); Assert.Equal(1440, data.AutoCleanupIntervalMinutes);
    }
}
