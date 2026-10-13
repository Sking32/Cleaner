using System.Text;
using System.Text.Json;
using Cleaner.Core.Data;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Core.Tests;

public sealed class RecoveryRegressionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CleanerRegression_" + Guid.NewGuid().ToString("N"));
    private readonly MemoryRegistry _registry = new();
    private readonly FakeCommands _commands = new();
    private const string Key = @"Software\CleanerFixture";
    public RecoveryRegressionTests() => Directory.CreateDirectory(_root);
    public void Dispose() { Directory.Delete(_root, recursive: true); }
    private string Local(string name) => Path.Combine(_root, name);
    private BackupService Backup() => new(Local("backup.json"), _registry, _commands);
    private OptimizeService Optimize(BackupService backup, params TweakAction[] apply)
    {
        var tweak = new TweakDefinition { Key = "fixture", Apply = apply.ToList(), RequiresReboot = true };
        File.WriteAllText(Local("tweaks.json"), JsonSerializer.Serialize(new { schemaVersion = 1, tweaks = new[] { tweak } }));
        File.WriteAllText(Local("presets.json"), """{"schemaVersion":1}""");
        return new(new TweakRepository(_root), new ActionInterpreter(backup, _registry, _commands), backup,
            new AppliedTweaksStore(Local("applied.json")), new PresetsRepository(_root));
    }
    private static TweakAction Set(string name, int value) => new()
    { Type = "regSet", Hive = "HKCU", Key = Key, Name = name, Kind = "DWord", Value = JsonSerializer.SerializeToElement(value), CreateKeyIfMissing = true };

    [Fact]
    public void MultiValueRollbackRestoresEveryOriginalAndMarksReboot()
    {
        _registry.SetValue("HKCU", Key, "one", 7, RegistryValueKind.DWord, true);
        _registry.SetValue("HKCU", Key, "two", 9, RegistryValueKind.DWord, true);
        var backup = Backup();
        var optimize = Optimize(backup, Set("one", 1), Set("two", 2));
        Assert.True(optimize.Apply("fixture").Success);
        Assert.Equal(2, backup.Keys.Count);
        Assert.True(optimize.Unapply("fixture").Success);
        Assert.Equal(7, _registry.GetValue("HKCU", Key, "one"));
        Assert.Equal(9, _registry.GetValue("HKCU", Key, "two"));
        Assert.False(optimize.IsAppliedByCleaner("fixture"));
        Assert.True(optimize.IsPendingReboot("fixture"));
        Assert.Empty(backup.Keys);
    }

    [Fact]
    public void FailedSecondActionRollsBackFirstWithoutLosingOriginal()
    {
        _registry.SetValue("HKCU", Key, "one", 7, RegistryValueKind.DWord, true);
        var optimize = Optimize(Backup(), Set("one", 1), new TweakAction { Type = "unsupported" });
        Assert.False(optimize.Apply("fixture").Success);
        Assert.Equal(7, _registry.GetValue("HKCU", Key, "one"));
        Assert.False(optimize.IsAppliedByCleaner("fixture"));
    }

    [Fact]
    public void FailedAppliedJournalSaveKeepsSnapshotsForRetry()
    {
        var backup = Backup();
        var optimize = Optimize(backup, Set("one", 1));
        Assert.True(optimize.Apply("fixture").Success);
        using (File.Open(Local("applied.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.False(optimize.Rollback("fixture").Success);
            Assert.Single(backup.Keys);
        }
        Assert.True(optimize.Rollback("fixture").Success);
        Assert.Empty(backup.Keys);
    }

    [Fact]
    public void BackupWriteFailurePreventsRegistryMutation()
    {
        var backup = Backup();
        backup.SaveRaw("existing", "HKCU\\" + Key, "old", 1, BackupType.DWord);
        using var locked = File.Open(Local("backup.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
        var interpreter = new ActionInterpreter(backup, _registry, _commands);
        Assert.False(interpreter.Execute(Set("new", 1), "fixture", "test").Success);
        Assert.Null(_registry.GetValue("HKCU", Key, "new"));
        Assert.Single(backup.Keys);
    }

    [Fact]
    public void PowerPlanCommandRollsBackCapturedPlan()
    {
        const string original = "381b4222-f694-41f0-9685-ff5bb260df2e";
        _commands.Respond = (_, args) => new(0, args == "/getactivescheme" ? "GUID: " + original : "", "");
        var optimize = Optimize(Backup(), new TweakAction { Type = "commandRun", FileName = "powercfg.exe",
            Arguments = "/setactive 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c" });
        Assert.True(optimize.Apply("fixture").Success);
        Assert.Equal((1, 0), optimize.RollbackAll());
        Assert.Contains(_commands.Calls, c => c.Arguments == "/setactive " + original);
    }

    [Fact]
    public void FailedCommandRestoreKeepsAppliedMarkAndBackup()
    {
        _commands.Respond = (_, args) => args == "/getactivescheme"
            ? new(0, "381b4222-f694-41f0-9685-ff5bb260df2e", "")
            : new(0, "", "");
        var backup = Backup();
        var optimize = Optimize(backup, new TweakAction { Type = "commandRun", FileName = "powercfg.exe", Arguments = "/setactive scheme_min" });
        Assert.True(optimize.Apply("fixture").Success);
        _commands.Respond = (_, _) => new(5, "", "denied");
        Assert.Equal((0, 1), optimize.RollbackAll());
        Assert.True(optimize.IsAppliedByCleaner("fixture"));
        Assert.Single(backup.Keys);
    }

    [Theory]
    [InlineData("EventLogSize", "wevtutil.exe", "sl System /ms:33554432")]
    [InlineData("ScheduledDefrag", "schtasks.exe", "/Change /TN \"\\Microsoft\\Windows\\Defrag\\ScheduledDefrag\" /Enable")]
    [InlineData("ReservedStorage", "dism.exe", "/Online /Set-ReservedStorageState /State:Enabled")]
    [InlineData("TcpAutoTuning", "netsh.exe", "int tcp set global autotuninglevel=normal")]
    public void CatalogCommandsRestoreCapturedState(string key, string file, string expected)
    {
        var definition = new TweakRepository().GetByKey(key)
            ?? new TweakRepository().Tweaks.Single(t => t.Apply.Any(a => a.FileName == file));
        _commands.Respond = (exe, args) =>
        {
            if (exe == "wevtutil.exe" && args.StartsWith("gl ")) return new(0, "<channel><logging><maxSize>33554432</maxSize></logging></channel>", "");
            if (exe == "schtasks.exe" && args.StartsWith("/Query")) return new(0, "<Task><Settings><Enabled>true</Enabled></Settings></Task>", "");
            if (exe == "powershell.exe")
            {
                var script = Encoding.Unicode.GetString(Convert.FromBase64String(args[(args.LastIndexOf(' ') + 1)..]));
                return new(0, script.Contains("ReservedStorageState") ? "Enabled" : "Normal", "");
            }
            return new(0, "", "");
        };
        var backup = Backup();
        var optimize = Optimize(backup, definition.Apply.ToArray());
        Assert.True(optimize.Apply("fixture").Success);
        Assert.True(optimize.Unapply("fixture").Success);
        Assert.Contains(_commands.Calls, c => c.File == file && c.Arguments == expected);
    }

    [Theory]
    [InlineData("backup.json")]
    [InlineData("applied.json")]
    [InlineData("whitelist.json")]
    public void EmptyPersistenceFilesArePreservedAndRejected(string file)
    {
        File.WriteAllText(Local(file), "");
        Assert.Throws<IOException>(() =>
        {
            if (file == "backup.json") _ = Backup();
            else if (file == "applied.json") _ = new AppliedTweaksStore(Local(file));
            else _ = new WhitelistService(Local(file));
        });
        Assert.Equal("", File.ReadAllText(Local(file)));
    }

    [Fact]
    public void ServiceBackupRestoresDelayedStartupAndRunningStateThroughDoubles()
    {
        var backup = Backup();
        backup.SaveServiceBeforeChange("fixture_svc_Test", "FixtureService", "AutomaticDelayed", "test", wasRunning: true);
        Assert.True(backup.RestoreByKey("fixture_svc_Test"));
        Assert.Contains(_commands.Calls, c => c.File == "sc.exe" && c.Arguments.EndsWith("start= delayed-auto"));
        var ps = _commands.Calls.Single(c => c.File == "powershell.exe");
        var script = Encoding.Unicode.GetString(Convert.FromBase64String(ps.Arguments[(ps.Arguments.LastIndexOf(' ') + 1)..]));
        Assert.Contains("WaitForStatus('Running'", script);
    }

    [Fact]
    public void StateCheckRequiresEverySubkeyToMatch()
    {
        _registry.SetValue("HKCU", Key + "\\one", "flag", 1, RegistryValueKind.DWord, true);
        _registry.SetValue("HKCU", Key + "\\two", "flag", 0, RegistryValueKind.DWord, true);
        var interpreter = new ActionInterpreter(Backup(), _registry, _commands);
        Assert.False(interpreter.Check(new TweakAction { Type = "regGetAllSubKeys", Hive = "HKCU", Key = Key,
            Name = "flag", Kind = "DWord", Expected = JsonSerializer.SerializeToElement(1) }).Matches);
    }

    [Fact]
    public void RemovedDefinitionBackupIsReportedAsUnresolved()
    {
        var backup = Backup();
        backup.SaveRaw("unknown_tweak", "HKCU\\" + Key, "flag", 1, BackupType.DWord);
        var optimize = Optimize(backup, Set("one", 1));
        Assert.Equal((0, 1), optimize.RollbackAll());
        Assert.Single(backup.Keys);
    }

    [Fact]
    public void SystemBinaryTreesAndAnyDriveRootAreProtected()
    {
        var safety = new SafetyService(0);
        Assert.False(safety.Check(@"Z:\").IsSafe);
        Assert.False(safety.Check(Path.Combine(Environment.SystemDirectory, "example.dll")).IsSafe);
        Assert.False(safety.Check(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "example", "app.exe")).IsSafe);
    }

    [Fact]
    public void ExportContainsCommaSeparatedBinaryAndUnexpandedEnvironmentString()
    {
        var backup = Backup();
        backup.SaveRaw("binary", "HKCU\\" + Key, "bytes", new byte[] { 0xAA, 0xBB }, BackupType.Binary);
        backup.SaveRaw("expand", "HKCU\\" + Key, "expand", @"%TEMP%\x", BackupType.ExpandString);
        Assert.NotNull(backup.ExportRegToFile(Local("export.reg")));
        Assert.Contains("hex:aa,bb", File.ReadAllText(Local("export.reg")).ToLowerInvariant());
        Assert.True(backup.RestoreByKey("expand"));
        Assert.Equal(@"%TEMP%\x", _registry.GetValue("HKCU", Key, "expand"));
    }

    [Fact]
    public void ExistingQuarantineInstanceWorksAfterExternalRestoreAndDelete()
    {
        var quarantine = new QuarantineService(Local("q"));
        var file = Local("one.txt");
        File.WriteAllText(file, "data");
        Assert.NotNull(quarantine.MoveToQuarantine(file, "test"));
        Assert.Equal(1, QuarantineService.RestoreFromQuarantine(quarantine.SessionDirectory).Restored);
        Assert.NotNull(quarantine.MoveToQuarantine(file, "test"));
        new QuarantineService(quarantine.SessionDirectory).ClearAll();
        File.WriteAllText(file, "second");
        Assert.NotNull(quarantine.MoveToQuarantine(file, "test"));
        Assert.Equal(1, quarantine.Count);
    }

    [Fact]
    public void UnknownQuarantineFilesSurvivePruningAndDeletion()
    {
        var quarantine = new QuarantineService(Local("q"));
        File.WriteAllText(Path.Combine(quarantine.SessionDirectory, "orphan"), "recover me");
        Assert.Equal(0, QuarantineService.PruneEmptySessions(_root));
        Assert.Throws<IOException>(() => quarantine.ClearAll());
        Assert.True(File.Exists(Path.Combine(quarantine.SessionDirectory, "orphan")));
    }

    [Theory]
    [InlineData("..\\important")]
    [InlineData("MANIFEST.JSON")]
    [InlineData("manifest.json.")]
    [InlineData("manifest.json ")]
    public void TraversalInQuarantineManifestIsRejected(string storedName)
    {
        var dir = Local("q"); Directory.CreateDirectory(dir);
        var outside = Local("important"); File.WriteAllText(outside, "keep");
        File.WriteAllText(Path.Combine(dir, "manifest.json"), JsonSerializer.Serialize(new
        { schemaVersion = 1, entries = new[] { new { originalPath = outside, storedName } } }));
        Assert.Throws<IOException>(() => new QuarantineService(dir));
        Assert.Equal(0, QuarantineService.ClearOldQuarantine(0, _root));
        Assert.Equal("keep", File.ReadAllText(outside));
    }

    [Fact]
    public void WhitelistedDirectoryProtectsChildrenButNotSimilarName()
    {
        var whitelist = new WhitelistService(Local("whitelist.json"));
        whitelist.Add(Local("keep"));
        Assert.True(whitelist.IsWhitelisted(Local(@"keep\child\data")));
        Assert.False(whitelist.IsWhitelisted(Local(@"keeper\data")));
    }

    [Theory]
    [InlineData("PathCleanup")]
    [InlineData("PathCleanupRecursive")]
    public void ForbiddenRootAndWhitelistedSubtreeAreNotTraversed(string handler)
    {
        var parent = Local("data"); var child = Path.Combine(parent, "keep");
        Directory.CreateDirectory(child); var file = Path.Combine(child, "x"); File.WriteAllText(file, "data");
        var whitelist = new WhitelistService(Local("whitelist.json")); whitelist.Add(child);
        var quarantine = new QuarantineService(Local("q"));
        var ctx = new CleanupContext(new SafetyService(0), whitelist, quarantine, commands: _commands);
        ICleanupHandler cleanup = handler == "PathCleanup" ? new PathCleanupHandler() : new PathCleanupRecursiveHandler();
        var op = new OperationEntry { Key = "test", Paths = new() { parent } };
        cleanup.Run(op, ctx);
        Assert.True(File.Exists(file)); Assert.Equal(0, quarantine.Count);
        ctx = new(new SafetyService(0, new[] { parent }), new WhitelistService(Local("empty.json")), quarantine, commands: _commands);
        cleanup.Run(op, ctx);
        Assert.True(File.Exists(file)); Assert.Equal(0, quarantine.Count);
    }

    [Fact]
    public void QuarantineMoveDoesNotClaimFreedSpace()
    {
        var dir = Local("data"); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "x"), "data");
        var ctx = new CleanupContext(new SafetyService(0), new WhitelistService(Local("wl.json")), new QuarantineService(Local("q")));
        var result = new PathCleanupHandler().Run(new OperationEntry { Key = "test", Paths = new() { dir } }, ctx);
        Assert.Equal(0, result.BytesFreed); Assert.Equal(4, result.BytesQuarantined);
        Assert.Equal(0, result.FilesDeleted); Assert.Equal(1, result.FilesQuarantined);
    }

    [Fact]
    public void LastCommandCancellationMarksSessionCanceled()
    {
        using var cts = new CancellationTokenSource();
        _commands.Respond = (_, _) => { cts.Cancel(); throw new OperationCanceledException(cts.Token); };
        var ctx = new CleanupContext(new SafetyService(0), new WhitelistService(Local("wl.json")), new QuarantineService(Local("q")),
            cancellationToken: cts.Token, commands: _commands);
        var runner = new CleanupRunner(new CleanupService(new OperationsRepository()), new PresetsRepository());
        Assert.True(runner.Run(new[] { "dns" }, ctx).Canceled);
    }

    [Theory]
    [InlineData("WindowsStoreCache")]
    [InlineData("ShadowCopy")]
    public void SystemCommandFailureIsReported(string name)
    {
        _commands.Respond = (_, _) => new(5, "", "access denied");
        var ctx = new CleanupContext(new SafetyService(0), new WhitelistService(Local("wl.json")), new QuarantineService(Local("q")), commands: _commands);
        ICleanupHandler handler = name == "ShadowCopy" ? new ShadowCopyHandler() : new WindowsStoreCacheHandler();
        Assert.False(handler.Run(new OperationEntry { Key = "test" }, ctx).Success);
    }
}
