using Cleaner.App.Controls;
using Cleaner.App.Services;
using Cleaner.App.ViewModels;
using Cleaner.Core.Services.Cleanup;

namespace Cleaner.App.Tests;

[Collection("Preferences")]
public sealed class AnalyticsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CleanerAnalytics_" + Guid.NewGuid().ToString("N"));
    public AnalyticsTests()
    {
        Directory.CreateDirectory(_root);
        UserSettings.UsePreviewStore(new AppSettingsStore(Path.Combine(_root, "settings.json")));
    }
    public void Dispose() => Directory.Delete(_root, true);
    [Theory]
    [InlineData(0)] [InlineData(25)] [InlineData(50)] [InlineData(75)] [InlineData(100)]
    public void GaugeUsesRealPercentage(double value) => Assert.Equal(value, DiskGauge.ClampPercent(value));
    [Fact] public void GaugeHandlesMissingInvalidAndOutOfRangeData()
    {
        Assert.True(double.IsNaN(DiskGauge.ClampPercent(double.NaN)));
        Assert.True(double.IsNaN(DiskGauge.ClampPercent(double.PositiveInfinity)));
        Assert.Equal(0, DiskGauge.ClampPercent(-10)); Assert.Equal(100, DiskGauge.ClampPercent(110));
        Assert.True(double.IsNaN(new DriveItem { TotalBytes = 0 }.UsedPercent));
        Assert.True(double.IsNaN(new DriveItem { TotalBytes = 100, FreeBytes = 200 }.UsedPercent));
        Assert.Equal(75, new DriveItem { TotalBytes = 100, FreeBytes = 25 }.UsedPercent);
    }
    [Fact] public void OccupancyColorMovesFromGreenToAmberToRed()
    {
        var low = DiskGauge.OccupancyColor(0); var middle = DiskGauge.OccupancyColor(70); var high = DiskGauge.OccupancyColor(90);
        Assert.True(low.G > low.R); Assert.True(middle.R > middle.B); Assert.True(high.R > high.G);
        Assert.Equal(high, DiskGauge.OccupancyColor(100));
    }
    [Fact] public void HistorySeparatesMethodsAndExcludesPreviewsAndUnknownLegacyEntries()
    {
        var entries = new List<CleanupHistoryEntry> { new() { FilesDeleted = 1, BytesFreed = 9999 },
            CleanupHistoryEntry.FromReport(new CleanupReport { DryRun = true, AppVersion = "1.1.0-beta.6", Result = new() { PreviewBytes = 9999 } }, "preview") };
        for (var i = 0; i < 12; i++) entries.Add(CleanupHistoryEntry.FromReport(new CleanupReport { AppVersion = "1.1.0-beta.6",
            CreatedUtc = DateTime.UtcNow.AddMinutes(-i), Result = new() { FilesDeleted = 1, BytesFreed = 100, FilesQuarantined = 1, BytesQuarantined = 200 } }, "applied"));
        var history = new HistoryViewModel(() => entries);
        Assert.Equal(10, history.ChartItems.Count); Assert.All(history.ChartItems, item => { Assert.Equal(50, item.DeletedPercent); Assert.Equal(100, item.QuarantinedPercent); });
        Assert.False(history.HasNoChart);
        Assert.True(new HistoryViewModel(() => Array.Empty<CleanupHistoryEntry>()).HasNoChart);
    }
    [Fact] public void HistoryChartDisplaysOneByteScaleForBothMethodsAndResetsItWhenEmpty()
    {
        const long mb = 1048576;
        var entries = new List<CleanupHistoryEntry> {
            CleanupHistoryEntry.FromReport(new CleanupReport { AppVersion = "1.1.0-rc.1", Result = new() {
                FilesDeleted = 1, BytesFreed = mb, FilesQuarantined = 1, BytesQuarantined = 4 * mb } }, "applied"),
            CleanupHistoryEntry.FromReport(new CleanupReport { AppVersion = "1.1.0-rc.1", Result = new() {
                FilesDeleted = 1, BytesFreed = 2 * mb, FilesQuarantined = 1, BytesQuarantined = mb } }, "applied")
        };
        var history = new HistoryViewModel(() => entries);
        Assert.Equal("4 MB", history.ChartMaximumText);
        Assert.Equal("2 MB", history.ChartMidpointText);
        Assert.Equal("0 B", history.ChartZeroText);
        Assert.All(history.ChartItems, item => Assert.Equal(4 * mb, item.Maximum));
        var first = history.ChartItems.Single(item => item.Entry.BytesFreed == mb);
        Assert.Equal(25, first.DeletedPercent);
        Assert.Equal(100, first.QuarantinedPercent);
        Assert.EndsWith(": 1 MB", first.DeletedText);
        Assert.EndsWith(": 4 MB", first.QuarantinedText);
        entries.Clear();
        history.RefreshCommand.Execute(null);
        Assert.Equal("0 B", history.ChartMaximumText);
        Assert.Equal("0 B", history.ChartMidpointText);
        Assert.True(history.HasNoChart);
    }
    [Fact] public void SimpleModeIsDefaultAndPersistsWithoutChangingAutomation()
    {
        Assert.False(UserSettings.GetExpertMode()); Assert.False(UserSettings.GetAutoCleanupEnabled());
        Assert.True(UserSettings.SetExpertMode(true)); Assert.True(UserSettings.GetExpertMode()); Assert.False(UserSettings.GetAutoCleanupEnabled());
        Assert.True(UserSettings.SetExpertMode(false)); Assert.False(UserSettings.GetExpertMode());
    }
    [Fact] public void ManyAnalysesDoNotEvictTheLastTenCleanupsFromSavedHistory()
    {
        for (var i = 0; i < 15; i++) UserSettings.AddCleanup(CleanupHistoryEntry.FromReport(new CleanupReport {
            AppVersion = "1.1.0-beta.6", CreatedUtc = DateTime.UtcNow.AddDays(-i - 1), Result = new() { FilesQuarantined = 1, BytesQuarantined = 100 } }, "applied"));
        for (var i = 0; i < 80; i++) UserSettings.AddCleanup(CleanupHistoryEntry.FromReport(new CleanupReport { DryRun = true }, "preview"));
        var reloaded = new AppSettingsStore(Path.Combine(_root, "settings.json")).Load().History;
        Assert.Equal(60, reloaded.Count);
        Assert.Equal(10, reloaded.Count(e => e.IsChartEligible));
        Assert.Equal(10, new HistoryViewModel(() => reloaded).ChartItems.Count);
    }
    [Theory]
    [InlineData("1.1.0-beta.6", "v1.1.0-beta.10")]
    [InlineData("1.1.0-beta.10", "1.1.0-rc.1")]
    [InlineData("1.1.0-rc.9", "1.1.0")]
    [InlineData("1.1.0", "1.2.0-beta.1")]
    [InlineData("1.1.0-beta.7", "v1.1.0-beta.7.1")]
    [InlineData("1.1.0-beta.7", "1.1.0-beta.7.0")]
    [InlineData("1.1.0-beta.7.1", "1.1.0-beta.7.2")]
    [InlineData("1.1.0-beta.7.2", "1.1.0-beta.7.10")]
    [InlineData("1.1.0-beta.7.10", "1.1.0-beta.8")]
    [InlineData("1.1.0-beta.7.1", "1.1.0-rc.1")]
    public void ReleaseVersionsCompareNumericPrereleaseAndStableOrder(string oldVersion, string newVersion)
    {
        Assert.True(ReleaseVersion.TryParse(oldVersion, out var old)); Assert.True(ReleaseVersion.TryParse(newVersion, out var next));
        Assert.True(next.CompareTo(old) > 0);
        Assert.False(ReleaseVersion.TryParse("https://untrusted.invalid", out _));
    }
    [Theory]
    [InlineData("1.1.0-beta.7.")]
    [InlineData("1.1.0-beta.7.1.2")]
    [InlineData("1.1.0-beta.7.2147483648")]
    public void MalformedOrOverflowingReleaseRevisionIsRejected(string value) => Assert.False(ReleaseVersion.TryParse(value, out _));
    [Fact] public void RecoveryFileSearchMatchesNamesPathsCategoriesAndPendingFilter()
    {
        var file = new QuarantineFileItem(new Cleaner.Core.Services.QuarantineEntry { OriginalPath = @"C:\Fixture\old-cache.tmp", Category = "temp_user" });
        Assert.True(file.Matches("CACHE", false)); Assert.True(file.Matches("temp_user", false)); Assert.False(file.Matches("other", false));
        Assert.False(file.Matches("cache", true));
        var pending = new QuarantineFileItem(new Cleaner.Core.Services.QuarantineEntry { OriginalPath = @"C:\Fixture\one.tmp", Pending = true });
        Assert.True(pending.Matches("one", true));
    }
}
