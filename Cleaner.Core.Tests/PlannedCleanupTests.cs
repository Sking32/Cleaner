using Cleaner.Core.Data;
using Cleaner.Core.Models;
using Cleaner.Core.Registry;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text.Json;
using Xunit.Abstractions;

namespace Cleaner.Core.Tests;

public sealed class PlannedCleanupTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CleanerPlan_" + Guid.NewGuid().ToString("N"));
    private readonly string _runtimeFixture = Path.Combine(Path.GetTempPath(), ".net", "CleanerPlan_" + Guid.NewGuid().ToString("N"));
    private string Temp => Path.Combine(_root, "temp");
    private string Session => Path.Combine(_root, "sessions", "fixture");
    private readonly MemoryRegistry _registry = new();
    private readonly FakeCommands _commands = new();
    private QuarantineService Q => new(Session);
    private WhitelistService W => new(Path.Combine(_root, "whitelist.json"));
    private CleanupRunner Runner(string? target = null)
    {
        var data = Path.Combine(_root, "data"); Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "operations.json"), JsonSerializer.Serialize(new { schemaVersion = 1,
            entries = new[] { new OperationEntry { Key = "fixture", Handler = "PathCleanup", Paths = new() { target ?? Temp } } } }));
        var operations = new OperationsRepository(data);
        return new(new CleanupService(operations), new PresetsRepository(), operations);
    }
    private string FileAt(string name)
    {
        var path = Path.Combine(Temp, name); Directory.CreateDirectory(Path.GetDirectoryName(path)!); File.WriteAllText(path, "fixture");
        File.SetCreationTimeUtc(path, DateTime.UtcNow.AddDays(-2)); File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddDays(-2)); return path;
    }
    private CleanupContext Context(bool preview, CleanupFileMode mode = CleanupFileMode.Quarantine, CleanupPlan? plan = null,
        CancellationToken token = default, IApplicationActivity? activity = null) => new(new SafetyService(0), W, Q,
            cancellationToken: token, dryRun: preview, commands: _commands, registry: _registry, fileMode: mode,
            minimumFileAge: TimeSpan.FromHours(24), capturePlan: preview, plan: plan, activity: activity);
    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
        if (Directory.Exists(_runtimeFixture)) Directory.Delete(_runtimeFixture, true);
    }

    [Fact] public void OldSingleFileRuntimeLibrariesAreProtectedAndLegacyQuarantineCanRestoreThem()
    {
        Directory.CreateDirectory(_runtimeFixture); var file = Path.Combine(_runtimeFixture, "native.dll"); File.WriteAllText(file, "fixture");
        File.SetCreationTimeUtc(file, DateTime.UtcNow.AddDays(-2)); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-2));
        var scan = Runner(_runtimeFixture).Run(new[] { "fixture" }, Context(true));
        Assert.Empty(scan.Plan!.Files); Assert.Equal(1, scan.FilesSkipped); Assert.True(File.Exists(file));
        // Simulate a legacy session on this test's own file, then verify recovery is still possible.
        Assert.NotNull(Q.MoveToQuarantine(file, "fixture")); Assert.Equal(1, Q.RestoreAll().Restored);
        Assert.Equal("fixture", File.ReadAllText(file));
    }

    private sealed class RefusingVerifier(string refusedPath) : IFileVerifier
    {
        public IVerifiedFile Open(string path, bool forMutation = false, FileIdentity? expected = null)
            => path == refusedPath ? throw new UnverifiableFileException("Fixture validation refused") : new VerifiedFile(path, forMutation, expected);
    }
    [Fact] public void UnverifiableFileAfterAnalysisIsSkippedWithoutBlockingOtherApprovedFiles()
    {
        var refused = FileAt("refused.tmp"); var approved = FileAt("approved.tmp"); var runner = Runner();
        var scan = runner.Run(new[] { "fixture" }, Context(true, CleanupFileMode.PermanentDelete));
        var context = new CleanupContext(new SafetyService(0), W, Q, commands: _commands, registry: _registry,
            fileMode: CleanupFileMode.PermanentDelete, plan: scan.Plan, fileVerifier: new RefusingVerifier(refused));
        var result = runner.Run(new[] { "fixture" }, context);
        Assert.True(File.Exists(refused)); Assert.False(File.Exists(approved));
        Assert.Equal(1, result.FilesSkipped); Assert.Equal(1, result.FilesDeleted); Assert.Equal(0, result.OperationsFailed);
        Assert.Contains(result.SkipDetails, detail => detail.Reason == "unverifiable");
    }

    [Theory]
    [InlineData(CleanupFileMode.Quarantine)]
    [InlineData(CleanupFileMode.PermanentDelete)]
    public void ApplyUsesOnlyApprovedFilesAndDoesNotEnumerateNewOnes(CleanupFileMode mode)
    {
        var approved = FileAt("approved.txt"); var runner = Runner();
        var scan = runner.Run(new[] { "fixture" }, Context(true, mode));
        Assert.True(scan.Plan!.CanApply); Assert.True(File.Exists(approved));
        var newcomer = FileAt("new.txt");
        var applied = runner.Run(new[] { "fixture" }, Context(false, mode, scan.Plan));
        Assert.False(File.Exists(approved)); Assert.True(File.Exists(newcomer));
        if (mode == CleanupFileMode.Quarantine)
            Assert.True(File.Exists(Path.Combine(Session, Assert.Single(Q.Entries).StoredName)), string.Join(";", Directory.GetFileSystemEntries(Session)));
        Assert.Equal(1, applied.FilesDeleted + applied.FilesQuarantined);
        Assert.Equal(mode == CleanupFileMode.PermanentDelete ? 7 : 0, applied.BytesFreed);
        Assert.Equal(mode == CleanupFileMode.Quarantine ? 7 : 0, applied.BytesQuarantined);
        if (mode == CleanupFileMode.Quarantine) { var restored = Q.RestoreAll(); Assert.True(restored.Restored == 1, string.Join(";", restored.Errors)); Assert.Equal("fixture", File.ReadAllText(approved)); }
        Assert.Empty(_commands.Calls);
    }
    [Fact] public void PlanIncludesEveryCandidateBeyondTheDisplaySample()
    {
        for (var i = 0; i < 12; i++) FileAt(i + ".txt");
        var scan = Runner().Run(new[] { "fixture" }, Context(true));
        Assert.Equal(12, scan.Plan!.Files.Count); Assert.Equal(5, scan.PreviewFiles.Count); Assert.Equal(7, scan.OmittedPreviewFiles);
    }
    [Fact] public void ModifiedOrReplacedFileIsPreserved()
    {
        var changed = FileAt("changed.txt"); var replaced = FileAt("replaced.txt"); var runner = Runner();
        var scan = runner.Run(new[] { "fixture" }, Context(true));
        File.WriteAllText(changed, "new content"); File.Delete(replaced); FileAt("replaced.txt");
        var result = runner.Run(new[] { "fixture" }, Context(false, plan: scan.Plan));
        Assert.Equal(0, result.FilesQuarantined); Assert.Equal(2, result.FilesSkipped);
        Assert.Equal("new content", File.ReadAllText(changed)); Assert.True(File.Exists(replaced));
    }
    [Fact] public void WriterAndNewWhitelistBlockMutation()
    {
        var locked = FileAt("locked.txt"); var excluded = FileAt("excluded.txt"); var runner = Runner();
        var scan = runner.Run(new[] { "fixture" }, Context(true)); W.Add(excluded);
        using var writer = File.Open(locked, FileMode.Open, FileAccess.Write, FileShare.ReadWrite);
        var result = runner.Run(new[] { "fixture" }, Context(false, plan: scan.Plan));
        Assert.Equal(2, result.FilesSkipped); Assert.True(File.Exists(locked)); Assert.True(File.Exists(excluded));
    }
    [Fact] public void CancellationAndModeChangesCannotExpandAnApprovedPlan()
    {
        var file = FileAt("keep.txt"); var runner = Runner(); var scan = runner.Run(new[] { "fixture" }, Context(true));
        Assert.Throws<InvalidOperationException>(() => runner.Run(new[] { "fixture" }, Context(false, CleanupFileMode.PermanentDelete, scan.Plan)));
        using var cancel = new CancellationTokenSource(); cancel.Cancel();
        Assert.True(runner.Run(new[] { "fixture" }, Context(false, plan: scan.Plan, token: cancel.Token)).Canceled);
        Assert.True(File.Exists(file));
    }
    private sealed class BusyActivity : IApplicationActivity { public bool IsBusy(string key) => true; }
    [Fact] public void ApplicationStartingAfterAnalysisBlocksItsCategory()
    {
        var file = FileAt("keep.txt"); var runner = Runner(); var scan = runner.Run(new[] { "fixture" }, Context(true));
        var result = runner.Run(new[] { "fixture" }, Context(false, plan: scan.Plan, activity: new BusyActivity()));
        Assert.Equal(1, result.FilesSkipped); Assert.Contains(result.SkipDetails, s => s.Reason == "application_running"); Assert.True(File.Exists(file));
    }
    [Fact] public void TornJournalBlocksMutationsAndKeepsRecoveryEvidence()
    {
        var file = FileAt("one.txt"); var runner = Runner(); var scan = runner.Run(new[] { "fixture" }, Context(true));
        Assert.NotNull(Q.MovePlanned(Assert.Single(scan.Plan!.Files), "fixture").Entry);
        var journal = Path.Combine(Session, "moves.jsonl"); File.AppendAllText(journal, "{torn");
        Assert.ThrowsAny<Exception>(() => new QuarantineService(Session));
        Assert.Contains("{torn", File.ReadAllText(journal)); Assert.False(File.Exists(file));
        Assert.False(Assert.Single(QuarantineService.InspectSessions(Path.GetDirectoryName(Session))).CanDelete);
    }
    [Fact] public void MoveIntentWithoutFinalRecordCanBeRecovered()
    {
        var file = FileAt("one.txt"); var runner = Runner(); var scan = runner.Run(new[] { "fixture" }, Context(true));
        Assert.NotNull(Q.MovePlanned(Assert.Single(scan.Plan!.Files), "fixture").Entry);
        var journal = Path.Combine(Session, "moves.jsonl"); var lines = File.ReadAllLines(journal); Assert.Equal(2, lines.Length);
        File.WriteAllText(journal, lines[0] + "\n");
        Assert.Equal(QuarantineHealth.NeedsRecovery, Assert.Single(QuarantineService.InspectSessions(Path.GetDirectoryName(Session))).Health);
        Assert.Equal(1, new QuarantineService(Session).RestoreAll().Restored); Assert.Equal("fixture", File.ReadAllText(file));
    }
    [Fact] public void NewRegistryValueFromAnotherProgramIsNotOverwrittenOnRollback()
    {
        var backup = new BackupService(Path.Combine(_root, "backup.json"), _registry, _commands);
        _registry.SetValue("HKCU", "Fixture", "Value", 1, RegistryValueKind.DWord, true);
        backup.SaveBeforeChange("fixture", "HKCU", "Fixture", "Value"); backup.ExpectRegistry("fixture", 2, RegistryValueKind.DWord);
        _registry.SetValue("HKCU", "Fixture", "Value", 3, RegistryValueKind.DWord);
        Assert.False(backup.RestoreByKey("fixture")); Assert.Equal(3, _registry.GetValue("HKCU", "Fixture", "Value")); Assert.Contains("fixture", backup.Keys);
    }
    [Fact] public void UnverifiedSafeTweaksAreAvailableAndExpertActionsAreNotSelectedByPresets()
    {
        Assert.True(TweakCompatibilityService.Evaluate(new TweakDefinition(), 26300, true).CanApply);
        Assert.False(TweakCompatibilityService.Evaluate(new TweakDefinition { ExpertOnly = true }, 26300, true).CanApply);
        Assert.True(TweakCompatibilityService.Evaluate(new TweakDefinition { ExpertOnly = true }, 26300, true, expertMode: true).CanApply);
        Assert.True(TweakCompatibilityService.Evaluate(new TweakDefinition { Compatibility = new() { VerifiedWindowsBuilds = new() { 26300 } } }, 26300, true).CanApply);
        var operations = new OperationsRepository(); var presets = new PresetsRepository();
        foreach (var key in new[] { "prefetch", "downloads_smart", "onedrive", "notifications", "wupdate", "winsxs", "shadowcopy" }) Assert.True(operations.GetByKey(key)!.Enabled);
        foreach (var preset in presets.CleanupPresets.Values) Assert.All(preset.Keys, key => { Assert.True(operations.GetByKey(key)!.Enabled); Assert.False(operations.GetByKey(key)!.ExpertOnly); });
    }
    [Fact] public void DiagnosticExportHidesPathsAndFreeFormErrors()
    {
        var json = DiagnosticExport.CreateJson(new { path = "C:\\Users\\Alice\\private.txt", errors = new[] { "Alice's file failed" },
            result = new { detail = "secret", reason = "file_in_use", count = 2 } });
        Assert.DoesNotContain("Alice", json); Assert.DoesNotContain("secret", json); Assert.Contains("file_in_use", json); Assert.Contains("count", json);
    }
    [Fact] public void CommandRollbackPreservesOutsideChangesAndRestoresOnlyExpectedState()
    {
        const string original = "381b4222-f694-41f0-9685-ff5bb260df2e";
        const string expected = "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c";
        var current = "a1841308-3541-4fab-bc81-f71556f20b4a";
        _commands.Respond = (_, _) => new CommandResult(0, "Power Scheme GUID: " + current, "");
        var backup = new BackupService(Path.Combine(_root, "command.json"), _registry, _commands);
        backup.SaveRaw("fixture", "", "", new CommandRestore("powercfg.exe", "/setactive " + original), BackupType.Command);
        backup.ExpectCommand("fixture", new("powercfg.exe", "/setactive " + expected));
        Assert.False(backup.RestoreByKey("fixture")); Assert.Single(backup.Keys);
        Assert.All(_commands.Calls, call => Assert.Equal("/getactivescheme", call.Arguments));
        current = expected; _commands.Calls.Clear();
        Assert.True(backup.RestoreByKey("fixture")); Assert.Empty(backup.Keys);
        Assert.Contains(_commands.Calls, call => call.Arguments == "/setactive " + original);
    }
    [Fact] public void LegacySettingWithoutExpectedStateKeepsItsBackup()
    {
        var backup = new BackupService(Path.Combine(_root, "legacy-settings.json"), _registry, _commands);
        _registry.SetValue("HKCU", "Fixture", "Value", 1, RegistryValueKind.DWord, true);
        backup.SaveBeforeChange("legacy", "HKCU", "Fixture", "Value");
        _registry.SetValue("HKCU", "Fixture", "Value", 4, RegistryValueKind.DWord);
        Assert.False(backup.RestoreByKey("legacy")); Assert.Equal(4, _registry.GetValue("HKCU", "Fixture", "Value")); Assert.Single(backup.Keys);
        Assert.Empty(_commands.Calls);
    }
    private sealed class FullQuarantine : IQuarantineService
    {
        public int Calls;
        public string SessionDirectory => "fixture";
        public int Count => 0;
        public long TotalSizeBytes => 0;
        public IReadOnlyList<QuarantineEntry> Entries => Array.Empty<QuarantineEntry>();
        public QuarantineMoveResult MovePlanned(PlannedFile file, string category) { Calls++; return new(null, "disk_full", "Fixture: no space."); }
        public QuarantineEntry? MoveToQuarantine(string path, string category) => throw new InvalidOperationException();
        public QuarantineRestoreResult RestoreAll() => throw new InvalidOperationException();
        public int ClearAll() => throw new InvalidOperationException();
    }
    [Fact] public void NoSpaceStopsFurtherActionsAndPreservesEveryFile()
    {
        var one = FileAt("one.txt"); var two = FileAt("two.txt"); var runner = Runner(); var scan = runner.Run(new[] { "fixture" }, Context(true));
        var full = new FullQuarantine();
        var context = new CleanupContext(new SafetyService(0), W, full, commands: _commands, registry: _registry, plan: scan.Plan);
        var result = runner.Run(new[] { "fixture" }, context);
        Assert.Equal(1, full.Calls); Assert.Equal(1, result.OperationsFailed); Assert.Equal(0, result.FilesQuarantined);
        Assert.True(File.Exists(one)); Assert.True(File.Exists(two));
    }
    [Fact] public void BenchmarkSequentialJournalAgainstLegacyFullManifest()
    {
        foreach (var count in new[] { 100, 300 })
        {
            var legacy = new QuarantineService(Path.Combine(_root, "legacy-" + count));
            var modern = new QuarantineService(Path.Combine(_root, "modern-" + count));
            var oldFiles = Enumerable.Range(0, count).Select(i => FileAt("old-" + count + "-" + i)).ToArray();
            var newFiles = Enumerable.Range(0, count).Select(i => FileAt("new-" + count + "-" + i)).ToArray();
            var candidates = newFiles.Select(path => { using var file = new VerifiedFile(path); return new PlannedFile("fixture", path, file.Identity); }).ToArray();
            var watch = Stopwatch.StartNew(); foreach (var file in oldFiles) Assert.NotNull(legacy.MoveToQuarantine(file, "fixture")); var before = watch.Elapsed;
            watch.Restart(); foreach (var candidate in candidates) { var move = modern.MovePlanned(candidate, "fixture"); Assert.True(move.Entry != null, move.Reason + ": " + move.Detail); Assert.True(File.Exists(Path.Combine(modern.SessionDirectory, move.Entry!.StoredName))); }
            modern.Checkpoint(); var after = watch.Elapsed;
            Assert.Equal(count, new QuarantineService(modern.SessionDirectory).Count);
            output.WriteLine($"{count} files: full manifest {before.TotalMilliseconds:0} ms; sequential journal {after.TotalMilliseconds:0} ms. Fixture measurement, not a performance guarantee.");
        }
    }
    [Fact] public void LongLocalPathsMoveAndRestoreByTheVerifiedHandle()
    {
        var file = FileAt(new string('a', 120) + "/" + new string('b', 160) + ".txt"); var runner = Runner();
        var plan = runner.Run(new[] { "fixture" }, Context(true)).Plan;
        var result = runner.Run(new[] { "fixture" }, Context(false, plan: plan));
        Assert.True(result.FilesQuarantined == 1, JsonSerializer.Serialize(result));
        var entry = Assert.Single(Q.Entries); Assert.True(File.Exists(Path.Combine(Session, entry.StoredName)));
        Assert.Equal(1, Q.RestoreAll().Restored); Assert.Equal("fixture", File.ReadAllText(file));
    }
    [Fact] public void TemporaryGameSavesSettingsAndDocumentsArePreserved()
    {
        foreach (var name in new[] { "game.sav", "settings.ini", "passwords.db", "document.docx", "profile.json" }) FileAt(name);
        FileAt("old.tmp"); var runner = Runner(); var scan = runner.Run(new[] { "fixture" }, Context(true));
        Assert.Equal("old.tmp", Path.GetFileName(Assert.Single(scan.Plan!.Files).Path));
        Assert.Equal(5, scan.FilesSkipped);
    }
}
