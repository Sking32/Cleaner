using System.Text.Json;
using Cleaner.Core.Data;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;

namespace Cleaner.Core.Tests;

public sealed class UpgradeRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CleanerUpgrade_" + Guid.NewGuid().ToString("N"));
    private string Local(string name) => Path.Combine(_root, name);
    public UpgradeRegressionTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private QuarantineService Session(string name = "q") => new(Local(name));
    private QuarantineEntry Add(QuarantineService q, string name)
    {
        var source = Local(name); File.WriteAllText(source, "data");
        return q.MoveToQuarantine(source, "fixture")!;
    }
    private static void UpdateManifest(QuarantineService q, Action<System.Text.Json.Nodes.JsonNode> update)
    {
        var path = Path.Combine(q.SessionDirectory, "manifest.json");
        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(path))!;
        update(json);
        File.WriteAllText(path, json.ToJsonString());
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{bad")]
    [InlineData("{\"schemaVersion\":99,\"entries\":[]}")]
    public void CorruptSessionsAreVisibleAndCannotBeDeleted(string contents)
    {
        var q = Session();
        var entry = Add(q, "keep.txt");
        var manifest = Path.Combine(q.SessionDirectory, "manifest.json");
        File.WriteAllText(manifest, contents);
        var inspected = Assert.Single(QuarantineService.InspectSessions(_root));
        Assert.Equal(QuarantineHealth.InvalidManifest, inspected.Health);
        Assert.False(inspected.CanDelete); Assert.False(inspected.CanRestore);
        Assert.ThrowsAny<Exception>(() => new QuarantineService(q.SessionDirectory));
        Assert.Equal(contents, File.ReadAllText(manifest));
        Assert.True(File.Exists(Path.Combine(q.SessionDirectory, entry.StoredName)));
    }

    [Fact]
    public void MissingManifestDoesNotCreateAnEmptyReplacement()
    {
        var q = Session(); var entry = Add(q, "orphan.txt");
        var manifest = Path.Combine(q.SessionDirectory, "manifest.json"); File.Delete(manifest);
        var inspected = Assert.Single(QuarantineService.InspectSessions(_root));
        Assert.Equal(QuarantineHealth.UnrecordedFiles, inspected.Health);
        Assert.False(inspected.CanRestore); Assert.False(inspected.CanDelete);
        Assert.Throws<IOException>(() => new QuarantineService(q.SessionDirectory));
        Assert.False(File.Exists(manifest));
        Assert.True(File.Exists(Path.Combine(q.SessionDirectory, entry.StoredName)));
    }

    [Fact]
    public void UnknownFilesAreReportedWithoutModifyingTheManifest()
    {
        var q = Session(); Add(q, "one.txt");
        var manifest = Path.Combine(q.SessionDirectory, "manifest.json");
        var before = File.ReadAllText(manifest);
        File.WriteAllText(Path.Combine(q.SessionDirectory, "unknown.txt"), "keep");
        var inspected = Assert.Single(QuarantineService.InspectSessions(_root, verifyContents: true));
        Assert.Equal(QuarantineHealth.UnrecordedFiles, inspected.Health);
        Assert.True(inspected.CanRestore); Assert.False(inspected.CanDelete);
        Assert.Contains(inspected.Issues, e => e.Contains("unknown.txt"));
        Assert.Equal(before, File.ReadAllText(manifest));
    }

    [Fact]
    public void MissingStoredFileIsReportedForManualRecovery()
    {
        var q = Session(); var entry = Add(q, "missing.txt");
        File.Delete(Path.Combine(q.SessionDirectory, entry.StoredName));
        var inspected = Assert.Single(QuarantineService.InspectSessions(_root, true));
        Assert.Equal(QuarantineHealth.NeedsRecovery, inspected.Health);
        Assert.False(inspected.CanDelete); Assert.NotEmpty(inspected.Issues);
    }

    [Fact]
    public void PendingMoveIsVisibleAndRestorable()
    {
        var q = Session(); Add(q, "pending.txt");
        UpdateManifest(q, n => n["entries"]![0]!["pending"] = true);
        var inspected = Assert.Single(QuarantineService.InspectSessions(_root));
        Assert.Equal(QuarantineHealth.NeedsRecovery, inspected.Health);
        Assert.True(inspected.CanRestore); Assert.False(inspected.CanDelete);
        Assert.Equal(1, new QuarantineService(q.SessionDirectory).RestoreAll().Restored);
        Assert.Equal("data", File.ReadAllText(Local("pending.txt")));
    }

    [Fact]
    public void CompletedRestoreIntentRecoversJournalWithoutMovingAgain()
    {
        var q = Session(); var entry = Add(q, "restored.txt");
        UpdateManifest(q, n => n["entries"]![0]!["restorePath"] = entry.OriginalPath);
        File.Move(Path.Combine(q.SessionDirectory, entry.StoredName), entry.OriginalPath);
        Assert.Equal(QuarantineHealth.NeedsRecovery, Assert.Single(QuarantineService.InspectSessions(_root)).Health);
        Assert.Equal(1, new QuarantineService(q.SessionDirectory).RestoreAll().Restored);
        Assert.Equal("data", File.ReadAllText(entry.OriginalPath));
        Assert.False(File.Exists(entry.OriginalPath + ".restored"));
    }

    [Fact]
    public void CancelDeleteKeepsRemainingEntriesAndAllowsRetry()
    {
        var q = Session(); Add(q, "one.txt"); Add(q, "two.txt");
        using var cts = new CancellationTokenSource();
        var result = q.ClearAllDetailed(cts.Token, new InlineProgress<QuarantineProgress>(p =>
        { if (p.Completed == 1) cts.Cancel(); }));
        Assert.True(result.Canceled); Assert.Equal(1, result.Deleted); Assert.Empty(result.Errors);
        Assert.Single(new QuarantineService(q.SessionDirectory).Entries);
        Assert.Equal(1, new QuarantineService(q.SessionDirectory).ClearAll());
    }

    [Fact]
    public void CancelRestoreKeepsRemainingEntriesAndAllowsRetry()
    {
        var q = Session(); Add(q, "one.txt"); Add(q, "two.txt");
        using var cts = new CancellationTokenSource();
        var result = q.RestoreAll(cts.Token, new InlineProgress<QuarantineProgress>(p =>
        { if (p.Completed == 1) cts.Cancel(); }));
        Assert.True(result.Canceled); Assert.Equal(1, result.Restored);
        Assert.Single(new QuarantineService(q.SessionDirectory).Entries);
        Assert.Equal(1, new QuarantineService(q.SessionDirectory).RestoreAll().Restored);
    }

    [Fact]
    public void PreCanceledDeleteDoesNotRewriteManifest()
    {
        var q = Session(); Add(q, "one.txt");
        var manifest = Path.Combine(q.SessionDirectory, "manifest.json");
        var before = File.ReadAllText(manifest);
        using var cts = new CancellationTokenSource(); cts.Cancel();
        Assert.True(q.ClearAllDetailed(cts.Token).Canceled);
        Assert.Equal(before, File.ReadAllText(manifest));
        Assert.Single(q.Entries);
    }

    [Fact]
    public void SweepReportsBlockedSessionAlongsideSuccessfulDeletion()
    {
        var healthy = Session("healthy"); Add(healthy, "delete.txt");
        var damaged = Session("damaged"); var preserved = Add(damaged, "keep.txt");
        UpdateManifest(healthy, n => n["createdUtc"] = DateTime.UtcNow.AddDays(-10).ToString("o"));
        UpdateManifest(damaged, n => n["createdUtc"] = DateTime.UtcNow.AddDays(-10).ToString("o"));
        File.WriteAllText(Path.Combine(damaged.SessionDirectory, "orphan"), "keep");
        var result = QuarantineService.ClearOldQuarantineDetailed(7, _root);
        Assert.Equal(1, result.DeletedSessions); Assert.Equal(1, result.FailedSessions);
        Assert.NotEmpty(result.Errors);
        Assert.True(File.Exists(Path.Combine(damaged.SessionDirectory, preserved.StoredName)));
        Assert.False(Directory.Exists(healthy.SessionDirectory));
    }

    [Theory]
    [InlineData(19045, false)]
    [InlineData(22000, true)]
    [InlineData(26100, true)]
    [InlineData(27000, false)]
    public void CompatibilityEnforcesDeclaredRangeWithoutClaimingVerification(int build, bool allowed)
    {
        var tweak = new TweakDefinition { Compatibility = new() { MinWindowsBuild = 22000, MaxWindowsBuild = 26100 } };
        var result = TweakCompatibilityService.Evaluate(tweak, build, true);
        Assert.Equal(allowed, result.CanApply); Assert.False(result.Verified);
    }

    [Fact]
    public void MissingCompatibilityIsUnverifiedAndNonWindowsCannotApply()
    {
        var tweak = new TweakDefinition();
        Assert.False(TweakCompatibilityService.Evaluate(tweak, 26100, true).Verified);
        Assert.False(TweakCompatibilityService.Evaluate(tweak, 26100, false).CanApply);
    }

    [Fact]
    public void UnsupportedTweakDoesNotCreateBackupOrChangeRegistry()
    {
        var registry = new MemoryRegistry(); var commands = new FakeCommands();
        var backup = new BackupService(Local("backup.json"), registry, commands);
        var applied = new AppliedTweaksStore(Local("applied.json"));
        var service = new OptimizeService(new TweakRepository(), new ActionInterpreter(backup, registry, commands),
            backup, applied, new PresetsRepository(), _ => new(false, false, "compatibility.unsupported"));
        Assert.False(service.Apply("VisualFx").Success);
        Assert.Empty(backup.Keys); Assert.Empty(applied.GetAppliedKeys());
        Assert.False(File.Exists(backup.BackupFilePath));
        Assert.Empty(commands.Calls);
    }

    [Fact]
    public void DiagnosticReportIncludesPartialOutcomeAndSkipReasons()
    {
        var context = new CleanupContext(new SafetyService(0), new WhitelistService(Local("wl.json")), Session(),
            dryRun: true, commands: new FakeCommands(), registry: new MemoryRegistry());
        var source = Local("keep.txt"); File.WriteAllText(source, "data");
        context.Whitelist.Add(source);
        var cleanup = new CleanupService(new OperationsRepository());
        cleanup.Register(new SkippingHandler(source));
        var result = new CleanupRunner(cleanup, new PresetsRepository()).Run(new[] { "temp_user" }, context);
        var report = new CleanupReport { DryRun = true, Result = result };
        var output = Local("report.json"); report.Save(output);
        using var json = JsonDocument.Parse(File.ReadAllText(output));
        Assert.True(json.RootElement.GetProperty("dryRun").GetBoolean());
        var skip = Assert.Single(result.SkipDetails);
        Assert.Equal("whitelist", skip.Reason); Assert.Equal(source, skip.Path);
        Assert.Equal("data", File.ReadAllText(source)); Assert.Equal(1, result.FilesSkipped);
    }

    [Fact]
    public void SkipDiagnosticsAreBoundedAndReportTruncation()
    {
        var context = new CleanupContext(new SafetyService(0), new WhitelistService(Local("wl.json")), Session(), commands: new FakeCommands());
        for (var i = 0; i < 1000; i++) context.RecordSkip("fixture", Local("file" + i), "safety");
        Assert.Equal(250, context.SkipDetails.Count); Assert.Equal(750, context.OmittedSkipDetails);
    }

    [Fact]
    public void RunnerTokenReachesHandlerAndPreservesCompletedOutcomes()
    {
        var commands = new FakeCommands();
        using var cancellation = new CancellationTokenSource();
        var context = new CleanupContext(new SafetyService(0), new WhitelistService(Local("wl.json")), Session(),
            commands: commands, registry: new MemoryRegistry());
        commands.Respond = (_, _) => { cancellation.Cancel(); throw new OperationCanceledException(); };
        var runner = new CleanupRunner(new ControlledCleanup(), new PresetsRepository());
        var result = runner.Run(new[] { "completed", "cancel", "never" }, context, cancellation.Token);
        Assert.True(result.Canceled); Assert.Equal(2, result.OperationsRun);
        Assert.Equal(0, result.OperationsFailed);
        Assert.True(result.Outcomes[0].Success); Assert.True(result.Outcomes[1].Canceled);
        Assert.Equal("cancel", result.Outcomes[1].Key); Assert.Single(commands.Calls);
    }

    [Theory]
    [InlineData("{\"minWindowsBuild\":-1}")]
    [InlineData("{\"minWindowsBuild\":26100,\"maxWindowsBuild\":22000}")]
    [InlineData("{\"minWindowsBuild\":22000,\"verifiedWindowsBuilds\":[19045]}")]
    [InlineData("{\"verifiedWindowsBuilds\":null}")]
    public void InvalidCompatibilityMetadataIsRejected(string metadata)
    {
        var doc = new { schemaVersion = 1, tweaks = new[] { new { key = "fixture", compatibility = JsonSerializer.Deserialize<JsonElement>(metadata) } } };
        File.WriteAllText(Local("tweaks.json"), JsonSerializer.Serialize(doc));
        Assert.Throws<InvalidDataException>(() => new TweakRepository(_root));
    }

    private sealed class SkippingHandler(string source) : ICleanupHandler
    {
        public string Name => "PathCleanup";
        public CleanupOperationResult Run(OperationEntry op, CleanupContext context) => new PathCleanupHandler().Run(
            new OperationEntry { Key = op.Key, Handler = "PathCleanup", Paths = new() { source } }, context);
    }

    private sealed class ControlledCleanup : ICleanupService
    {
        public IReadOnlyDictionary<string, ICleanupHandler> Handlers => new Dictionary<string, ICleanupHandler>();
        public void Register(ICleanupHandler handler) => throw new NotSupportedException();
        public CleanupOperationResult RunOperation(OperationEntry op, CleanupContext context) => RunOperation(op.Key, context);
        public CleanupOperationResult RunOperation(string key, CleanupContext context)
        {
            if (key == "cancel") context.Commands.Run("fixture", "cancel", cancellationToken: context.CancellationToken);
            return CleanupOperationResult.Ok(message: "fixture completed");
        }
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    { public void Report(T value) => report(value); }
}
