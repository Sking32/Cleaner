using Cleaner.Core.Data;
using Cleaner.Core.Models;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Microsoft.Win32;
using System.Text.Json;

namespace Cleaner.Core.Tests;

public sealed class Beta7AccessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CleanerBeta7_" + Guid.NewGuid().ToString("N"));
    private readonly MemoryRegistry _registry = new();
    private readonly FakeCommands _commands = new();
    private string Local(string name) => Path.Combine(_root, name);
    public Beta7AccessTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);
    private CleanupRunner Runner(params OperationEntry[] definitions)
    {
        var data = Local("data"); Directory.CreateDirectory(data);
        File.WriteAllText(Path.Combine(data, "operations.json"), JsonSerializer.Serialize(new { schemaVersion = 1, entries = definitions }));
        var operations = new OperationsRepository(data);
        return new(new CleanupService(operations), new PresetsRepository(), operations);
    }
    private CleanupContext Context(bool preview, bool expert = false, CleanupPlan? plan = null, CleanupFileMode mode = CleanupFileMode.Quarantine,
        CancellationToken token = default) => new(new SafetyService(5), new WhitelistService(Local("whitelist.json")), new QuarantineService(Local("q")),
            dryRun: preview, commands: _commands, registry: _registry, capturePlan: preview, plan: plan, expertMode: expert, fileMode: mode, cancellationToken: token);

    [Fact] public void CatalogAndPresetsSeparateAvailabilityFromDefaultSelection()
    {
        var operations = new OperationsRepository(); var presets = new PresetsRepository();
        Assert.All(operations.Operations, op => Assert.True(op.Enabled));
        Assert.Equal(45, operations.Operations.Count(op => !op.ExpertOnly)); Assert.Equal(11, operations.Operations.Count(op => op.ExpertOnly));
        foreach (var preset in presets.CleanupPresets.Values)
            Assert.All(preset.Keys, key => { var op = operations.GetByKey(key)!; Assert.True(op.Recommended); Assert.False(op.ExpertOnly); Assert.True(CleanupService.IsFileHandler(op.Handler!)); });
        foreach (var key in new[] { "office", "prefetch", "downloads_smart", "recycle", "d3dscache", "steam_shader", "minidump", "shadowcopy" })
            Assert.DoesNotContain(key, presets.CleanupPresets.Values.SelectMany(p => p.Keys));
        var tweaks = new TweakRepository(); Assert.NotEmpty(presets.OptimizePresets["safe"].Keys);
        Assert.All(presets.OptimizePresets.Values.SelectMany(p => p.Keys), key => Assert.False(TweakCompatibilityService.RequiresExpert(tweaks.GetByKey(key)!)));
        Assert.DoesNotContain("XboxGameBar", presets.OptimizePresets["gamer"].Keys);
    }

    [Theory] [InlineData("temp_user")] [InlineData("temp_sys")]
    public void ManualTempIncludesFreshFilesOfAnyExtensionAndStillUsesAnExactPlan(string key)
    {
        var source = Local("temp"); Directory.CreateDirectory(source);
        var config = Path.Combine(source, "fresh.json"); File.WriteAllText(config, "fixture");
        var locked = Path.Combine(source, "locked.tmp"); File.WriteAllText(locked, "fixture");
        using var lockHandle = File.Open(locked, FileMode.Open, FileAccess.Write, FileShare.None);
        var runner = Runner(new OperationEntry { Key = key, Handler = "PathCleanup", Paths = new() { source } });
        var scan = runner.Run(new[] { key }, Context(true, mode: CleanupFileMode.PermanentDelete));
        Assert.Equal(config, Assert.Single(scan.Plan!.Files).Path);
        var newcomer = Path.Combine(source, "new.tmp"); File.WriteAllText(newcomer, "fixture");
        var result = runner.Run(new[] { key }, Context(false, plan: scan.Plan, mode: CleanupFileMode.PermanentDelete));
        Assert.Equal(1, result.FilesDeleted); Assert.False(File.Exists(config)); Assert.True(File.Exists(locked)); Assert.True(File.Exists(newcomer));
        Assert.Empty(_commands.Calls);
    }
    [Fact] public void AutomaticTempRetainsDocumentsConfigurationsAndFreshFiles()
    {
        var source = Local("temp"); Directory.CreateDirectory(source);
        foreach (var name in new[] { "document.docx", "settings.json", "old.tmp" }) { var file = Path.Combine(source, name); File.WriteAllText(file, "fixture"); File.SetCreationTimeUtc(file, DateTime.UtcNow.AddDays(-2)); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-2)); }
        File.WriteAllText(Path.Combine(source, "new.tmp"), "fixture");
        var result = AutomaticCleanup.Run(source, new SafetyService(0), new WhitelistService(Local("whitelist.json")), new QuarantineService(Local("q")));
        Assert.Equal(1, result.FilesQuarantined); Assert.True(File.Exists(Path.Combine(source, "document.docx"))); Assert.True(File.Exists(Path.Combine(source, "settings.json"))); Assert.True(File.Exists(Path.Combine(source, "new.tmp")));
    }
    [Fact] public void TempKeysDoNotBypassExtensionsOutsideTempOrExplicitExclusions()
    {
        var excluded = Local("excluded"); Directory.CreateDirectory(excluded); var path = Path.Combine(excluded, "one.json"); File.WriteAllText(path, "fixture");
        Assert.False(new SafetyService(0, new[] { excluded }).CheckForCleanup(path, "temp_user").IsSafe);
        Assert.False(new SafetyService(0).CheckForCleanup(path, "thumbs").IsSafe);
        Assert.False(new SafetyService(0).CheckForCleanup(@"C:\Windows\System32\kernel32.dll", "temp_sys").IsSafe);
    }
    [Theory] [InlineData("dns", false)] [InlineData("expert_command", true)]
    public void CapturedSystemCommandsActuallyExecuteOnceAndPreviewNeverRunsThem(string key, bool expert)
    {
        var runner = Runner(new OperationEntry { Key = key, Handler = "Command", Command = "ipconfig /flushdns", ExpertOnly = expert });
        var scan = runner.Run(new[] { key }, Context(true, expert)); Assert.True(scan.Plan!.CanApply); Assert.Empty(_commands.Calls);
        var applied = runner.Run(new[] { key }, Context(false, expert, scan.Plan));
        Assert.Equal(0, applied.OperationsFailed); Assert.Equal(1, applied.OperationsRun);
        Assert.Single(_commands.Calls); Assert.Equal(0, applied.BytesFreed); Assert.Equal(0, applied.FilesQuarantined);
    }
    [Fact] public void ChangingOutOfExpertModeBlocksApprovedSystemAction()
    {
        var runner = Runner(new OperationEntry { Key = "system", Handler = "Command", Command = "fixture.exe", ExpertOnly = true });
        Assert.False(runner.Run(new[] { "system" }, Context(true)).Plan!.CanApply);
        var scan = runner.Run(new[] { "system" }, Context(true, true));
        Assert.Equal(1, runner.Run(new[] { "system" }, Context(false, false, scan.Plan)).OperationsFailed); Assert.Empty(_commands.Calls);
    }
    [Fact] public void CancellingAnApprovedSystemActionDoesNotRunIt()
    {
        var runner = Runner(new OperationEntry { Key = "system", Handler = "Command", Command = "fixture.exe", ExpertOnly = true });
        var scan = runner.Run(new[] { "system" }, Context(true, true)); using var token = new CancellationTokenSource(); token.Cancel();
        Assert.True(runner.Run(new[] { "system" }, Context(false, true, scan.Plan, token: token.Token)).Canceled); Assert.Empty(_commands.Calls);
    }
    [Fact] public void ChangedRegistryHistoryPreventsRemovalOfNewValues()
    {
        const string key = @"Software\CleanerFixture\History";
        _registry.SetValue("HKCU", key, "old", "fixture", RegistryValueKind.String, true);
        var runner = Runner(new OperationEntry { Key = "recent_typed", Handler = "RegCleanKey", RegistryKey = "HKCU:\\" + key });
        var scan = runner.Run(new[] { "recent_typed" }, Context(true));
        _registry.SetValue("HKCU", key, "new", "fixture", RegistryValueKind.String, true);
        Assert.Equal(1, runner.Run(new[] { "recent_typed" }, Context(false, plan: scan.Plan)).OperationsFailed);
        Assert.Equal(2, _registry.GetValueNames("HKCU", key).Count);
        scan = runner.Run(new[] { "recent_typed" }, Context(true));
        Assert.Equal(0, runner.Run(new[] { "recent_typed" }, Context(false, plan: scan.Plan)).OperationsFailed);
        Assert.Empty(_registry.GetValueNames("HKCU", key));
    }
    [Fact] public void FilePatternsAndMinimumAgePreserveActiveAndUnrelatedLogs()
    {
        var folder = Local("logs"); Directory.CreateDirectory(folder); Directory.SetCreationTimeUtc(folder, DateTime.UtcNow.AddDays(-8));
        foreach (var name in new[] { "CbsPersist_old.log", "CBS.log", "unrelated.dll" }) { var file = Path.Combine(folder, name); File.WriteAllText(file, "fixture"); File.SetCreationTimeUtc(file, DateTime.UtcNow.AddDays(-8)); File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-8)); }
        File.WriteAllText(Path.Combine(folder, "CbsPersist_new.log"), "fixture");
        var runner = Runner(new OperationEntry { Key = "cbs", Handler = "PathCleanup", Paths = new() { folder }, OlderThanDays = 7, FilePatterns = new() { "CbsPersist_*.log" } });
        Assert.Equal("CbsPersist_old.log", Path.GetFileName(Assert.Single(runner.Run(new[] { "cbs" }, Context(true)).Plan!.Files).Path));
    }
    [Theory] [InlineData("safe", false, true)] [InlineData("caution", false, false)] [InlineData("caution", true, true)] [InlineData("danger", true, true)]
    public void UnverifiedTweaksFollowModeWithoutClaimingVerification(string level, bool expert, bool expected)
    {
        var result = TweakCompatibilityService.Evaluate(new() { Level = level }, 26100, true, expert);
        Assert.Equal(expected, result.CanApply); Assert.False(result.Verified);
        Assert.False(TweakCompatibilityService.Evaluate(new() { Level = level, Compatibility = new() { MinWindowsBuild = 26100 } }, 19045, true, true).CanApply);
    }
    private sealed class FakeServices : IServiceAccess
    {
        public string Startup = "AutomaticDelayed"; public int Changes;
        public string? ReadStartup(string name) => Startup;
        public bool SetStartup(string name, string startup) { Changes++; Startup = startup; return true; }
    }
    [Fact] public void ServiceStartupBackupPersistsAndDoesNotOverwriteOutsideChanges()
    {
        var services = new FakeServices(); var backup = new BackupService(Local("backup.json"), _registry, _commands, services);
        var interpreter = new ActionInterpreter(backup, _registry, _commands, services);
        Assert.True(interpreter.Execute(new() { Type = "serviceSet", ServiceName = "FixtureService", StartupType = "Disabled", StopFirst = true }, "fixture", "test").Success);
        Assert.Equal("Disabled", services.Startup); Assert.Equal(1, services.Changes); Assert.Empty(_commands.Calls);
        backup = new BackupService(Local("backup.json"), _registry, _commands, services);
        services.Startup = "Manual"; Assert.False(backup.RestoreByKey("fixture_svc_FixtureService")); Assert.Single(backup.Keys);
        services.Startup = "Disabled"; Assert.True(backup.RestoreByKey("fixture_svc_FixtureService")); Assert.Equal("AutomaticDelayed", services.Startup); Assert.Empty(backup.Keys);
    }
    [Fact] public void CriticalServicesCannotBeMutatedThroughTweaks()
    {
        var services = new FakeServices(); var backup = new BackupService(Local("backup.json"), _registry, _commands, services);
        Assert.False(new ActionInterpreter(backup, _registry, _commands, services).Execute(new() { Type = "serviceSet", ServiceName = "RpcSs", StartupType = "Disabled" }, "fixture", "test").Success);
        Assert.Equal(0, services.Changes); Assert.Empty(backup.Keys);
    }
    [Fact] public void NativeMaintenanceCommandsUseDocumentedArgumentsOnly()
    {
        var ctx = Context(false, true);
        Assert.True(new WindowsStoreCacheHandler().Run(new() { Key = "windows_store" }, ctx).Success);
        Assert.True(new WindowsOldHandler().Run(new() { Key = "windowsold" }, ctx).Success);
        Assert.True(new DismComponentCleanupHandler().Run(new() { Key = "winsxs" }, ctx).Success);
        Assert.Contains(_commands.Calls, c => c.File == "wsreset.exe" && c.Arguments == "");
        Assert.Contains(_commands.Calls, c => c.File == "cleanmgr.exe" && c.Arguments == "/AUTOCLEAN");
        Assert.DoesNotContain(_commands.Calls, c => c.Arguments.Contains("ResetBase", StringComparison.OrdinalIgnoreCase));
    }
    [Fact] public void LogCleanupPreservesTodayRecoveryFilesAndChangedOrNewLogs()
    {
        var logs = Local("logs"); Directory.CreateDirectory(logs);
        var old = Path.Combine(logs, "cleaner-2000-01-01.log"); File.WriteAllText(old, "fixture"); File.SetLastWriteTime(old, DateTime.Today.AddDays(-2));
        var changed = Path.Combine(logs, "cleaner-2000-01-02.log"); File.WriteAllText(changed, "fixture"); File.SetLastWriteTime(changed, DateTime.Today.AddDays(-2));
        var today = Path.Combine(logs, "cleaner-" + DateTime.Today.ToString("yyyy-MM-dd") + ".log"); File.WriteAllText(today, "fixture");
        var recovery = Path.Combine(logs, "opt-backup.json"); File.WriteAllText(recovery, "fixture");
        var maintenance = new ApplicationLogMaintenance(logs); var plan = maintenance.Analyze(); Assert.Equal(2, plan.Count);
        File.WriteAllText(changed, "changed"); var newcomer = Path.Combine(logs, "cleaner-2000-01-03.log"); File.WriteAllText(newcomer, "fixture"); File.SetLastWriteTime(newcomer, DateTime.Today.AddDays(-2));
        var result = maintenance.Apply(plan); Assert.Equal(1, result.Deleted); Assert.Equal(1, result.Skipped);
        Assert.False(File.Exists(old)); Assert.True(File.Exists(changed)); Assert.True(File.Exists(today)); Assert.True(File.Exists(recovery)); Assert.True(File.Exists(newcomer));
    }
}
