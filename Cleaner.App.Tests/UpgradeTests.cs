using System.Text.Json;
using System.Text.Json.Nodes;
using System.Windows;
using Cleaner.App.Services;
using Cleaner.App.ViewModels;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;

namespace Cleaner.App.Tests;

[CollectionDefinition("Preferences", DisableParallelization = true)]
public sealed class PreferencesCollection { }

[Collection("Preferences")]
public sealed class UpgradeTests
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CleanerUpgrade_" + Guid.NewGuid().ToString("N"));
    public UpgradeTests()
    {
        Directory.CreateDirectory(_root);
        AppLog.UsePreviewDirectory(Path.Combine(_root, "logs"));
        UserSettings.UsePreviewStore(new AppSettingsStore(Path.Combine(_root, "preferences.json")));
        Dialogs.Host = _ => Task.FromResult(false);
    }

    [Fact]
    public void PresetsRoundTripAndUpdateByNamePreservesIdentity()
    {
        var path = Path.Combine(_root, "presets.json"); var store = new AppSettingsStore(path);
        var first = store.UpsertCleanupPreset(" Mine ", new[] { "temp_user", "TEMP_USER" }, new[] { "temp_user", "wer_user" });
        Assert.Single(first.Keys);
        var updated = store.UpsertCleanupPreset("MINE", new[] { "wer_user" }, new[] { "wer_user" });
        Assert.Equal(first.Id, updated.Id);
        var loaded = Assert.Single(new AppSettingsStore(path).Load().CleanupPresets);
        Assert.Equal("wer_user", Assert.Single(loaded.Keys));
        Assert.True(store.DeleteCleanupPreset(first.Id));
        Assert.Empty(new AppSettingsStore(path).Load().CleanupPresets);
    }

    [Fact]
    public void InvalidPresetCannotWriteASelection()
    {
        var path = Path.Combine(_root, "invalid.json"); var store = new AppSettingsStore(path);
        Assert.Throws<ArgumentException>(() => store.UpsertCleanupPreset(" ", new[] { "ok" }, new[] { "ok" }));
        Assert.Throws<ArgumentException>(() => store.UpsertCleanupPreset("Name", new[] { "unknown" }, new[] { "ok" }));
        Assert.Throws<ArgumentException>(() => store.UpsertCleanupPreset("Name", Array.Empty<string>(), new[] { "ok" }));
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void FailedPresetWriteRollsBackMemoryAndPreservesOriginalDocument()
    {
        var path = Path.Combine(_root, "damaged.json"); File.WriteAllText(path, "{broken");
        var store = new AppSettingsStore(path);
        Assert.Throws<IOException>(() => store.UpsertCleanupPreset("Name", new[] { "ok" }, new[] { "ok" }));
        Assert.Empty(store.Load().CleanupPresets); Assert.Equal("{broken", File.ReadAllText(path));
    }

    [Fact]
    public void LimitDoesNotPreventUpdatingAnExistingPreset()
    {
        var store = new AppSettingsStore(Path.Combine(_root, "limit.json"));
        for (var i = 0; i < 30; i++) store.UpsertCleanupPreset("Name" + i, new[] { "ok" }, new[] { "ok" });
        Assert.Throws<InvalidOperationException>(() => store.UpsertCleanupPreset("extra", new[] { "ok" }, new[] { "ok" }));
        store.UpsertCleanupPreset("Name0", new[] { "other" }, new[] { "other" });
        Assert.Equal(30, store.Load().CleanupPresets.Count);
    }

    [Fact]
    public void WindowDensityAndHistoryRoundTripIncludingPreviewDetails()
    {
        var path = Path.Combine(_root, "roundtrip.json"); var store = new AppSettingsStore(path);
        var data = store.Load(); data.Density = "comfortable"; data.StartPage = "whatsnew";
        data.Window = new() { Width = 1200, Height = 750, Left = 42, Top = 12, Maximized = true };
        data.History.Add(CleanupHistoryEntry.FromReport(new CleanupReport { DryRun = true, Result = new() { PreviewBytes = 42,
            PreviewFiles = new[] { new CleanupPreviewFile("sample", "fixture.tmp", 42) }, PreviewFileCounts = new Dictionary<string, int> { ["sample"] = 1 } } }, "preview"));
        Assert.True(store.Save()); var loaded = new AppSettingsStore(path).Load();
        Assert.Equal("comfortable", loaded.Density); Assert.True(loaded.Window!.Maximized); Assert.Equal("whatsnew", loaded.StartPage);
        Assert.Equal(42, Assert.Single(loaded.History).Report!.Result.PreviewFiles[0].Size);
    }

    [Theory]
    [InlineData("preview")][InlineData("declined")][InlineData("failed")][InlineData("canceled")]
    public void HistoryRetainsZeroOperationEvents(string kind)
    {
        var report = new CleanupReport { Result = new() { Canceled = kind == "canceled", FilesSkipped = 3 } };
        var entry = CleanupHistoryEntry.FromReport(report, kind, "diagnostic");
        var vm = new HistoryViewModel(() => new[] { entry });
        Assert.Same(entry, vm.SelectedEntry!.Entry); Assert.Equal("diagnostic", vm.SelectedEntry.ErrorText);
        Assert.Equal(3, vm.SelectedEntry.ExportReport.Result.FilesSkipped);
        vm.SearchText = "no-match"; Assert.True(vm.HasNoEntries); Assert.False(vm.ExportCommand.CanExecute(null));
        vm.SearchText = entry.Id; Assert.Single(vm.FilteredEntries);
    }

    [Fact]
    public void LegacyHistoryCanBeExportedWithoutFabricatingQuarantine()
    {
        var item = new HistoryItem(new CleanupHistoryEntry { BytesFreed = 123, Operations = 2 });
        Assert.True(item.IsLegacy); Assert.False(item.HasSession); Assert.Equal(123, item.ExportReport.Result.BytesFreed);
    }

    private sealed class PreviewRunner : ICleanupRunner
    {
        public int Calls { get; private set; }
        public bool Canceled { get; init; }
        public CleanupSessionResult Run(IEnumerable<string> keys, CleanupContext context, CancellationToken token = default)
        {
            // The only injected runner is a stub; any attempt to apply fails before touching anything.
            if (!context.DryRun) throw new InvalidOperationException("Applying is forbidden in this fixture.");
            Calls++;
            return new() { Canceled = Canceled, OperationsRun = 1, PreviewBytes = 100,
                Outcomes = new[] { new OperationOutcome { Key = keys.First(), Handler = "PathCleanup", Success = true, PreviewBytes = 100 } } };
        }
        public CleanupSessionResult RunPreset(string name, CleanupContext context, CancellationToken token = default) => throw new NotSupportedException();
    }
    private CleanupViewModel CreateCleanup(PreviewRunner runner)
    {
        // No default CleanupService is instantiated and no production operation is ever executed.
        var vm = new CleanupViewModel(new OperationsRepository(), new PresetsRepository(), new LocalizationRepository("en"), runner,
            new SafetyService(), new WhitelistService(Path.Combine(_root, "whitelist.json")), new QuarantineService(Path.Combine(_root, "quarantine")));
        foreach (var op in vm.Categories.SelectMany(c => c.Operations)) op.IsChecked = op.Key == "temp_user";
        return vm;
    }

    [Fact]
    public async Task AnalyzeRecordsOnePreviewAndNeverApplies()
    {
        var runner = new PreviewRunner(); var vm = CreateCleanup(runner);
        await vm.AnalyzeCommand.ExecuteAsync(null);
        Assert.Equal(1, runner.Calls); Assert.False(vm.IsRunning); Assert.True(vm.HasResults);
        var entry = Assert.Single(UserSettings.GetCleanupHistory()); Assert.Equal("preview", entry.Kind); Assert.True(entry.Report!.DryRun);
    }

    [Fact]
    public async Task DeclinedCleanupRecordsOneEntryAndNeverApplies()
    {
        var runner = new PreviewRunner(); var vm = CreateCleanup(runner);
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Equal(1, runner.Calls); Assert.Equal("declined", Assert.Single(UserSettings.GetCleanupHistory()).Kind);
    }

    [Fact]
    public async Task CanceledPreviewDoesNotAskForCleanupConfirmation()
    {
        var dialogs = 0;
        Dialogs.Host = _ => { dialogs++; return Task.FromResult(false); };
        var runner = new PreviewRunner { Canceled = true }; var vm = CreateCleanup(runner);
        await vm.RunCommand.ExecuteAsync(null);
        Assert.Equal(1, runner.Calls); Assert.Equal("canceled", Assert.Single(UserSettings.GetCleanupHistory()).Kind);
        Assert.Equal(0, dialogs);
    }

    [Fact]
    public void WindowIsClampedToWorkAreaAfterMonitorChange()
    {
        var work = new Rect(0, 0, 1280, 720);
        var p = WindowLayout.Fit(new() { Width = 5000, Height = 5000, Left = -9000, Top = 9000, Maximized = true }, work);
        Assert.Equal(1280, p.Width); Assert.Equal(720, p.Height); Assert.Equal(0, p.Left); Assert.Equal(0, p.Top); Assert.True(p.Maximized);
        p = WindowLayout.Fit(new() { Width = double.NaN, Height = 1 }, new Rect(0, 0, 800, 600));
        Assert.Equal(800, p.Width); Assert.Equal(600, p.Height);
    }

    [Fact]
    public void ReleaseCatalogMatchesAssemblyVersionAndHasTwoLanguages()
    {
        var catalog = ReleaseCatalog.Load(Path.Combine(AppContext.BaseDirectory, "Data", "releases.json"), AppVersion.Current);
        Assert.Contains(catalog, r => r.Version == AppVersion.Current); Assert.Contains(catalog, r => r.Version == "1.0.0" && r.Date == null);
        Assert.Equal(AppVersion.Current, catalog[0].Version);
        Assert.Contains(catalog, r => r.Version == "1.1.0-beta.1");
        Assert.Contains(catalog, r => r.Version == "1.1.0-beta.2");
        Assert.All(catalog, r => { Assert.Contains("en", r.Text.Keys); Assert.Contains("ru", r.Text.Keys); });
    }

    [Theory]
    [InlineData("version", "bad")][InlineData("date", "2026-99-99")][InlineData("version", "9.9.9")]
    public void InvalidReleaseMetadataIsRejected(string field, string value)
    {
        var doc = JsonNode.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "releases.json")))!;
        doc["releases"]![0]![field] = value;
        var path = Path.Combine(_root, "releases.json"); File.WriteAllText(path, doc.ToJsonString());
        Assert.Throws<InvalidDataException>(() => ReleaseCatalog.Load(path, AppVersion.Current));
    }
}
