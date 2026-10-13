using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using System.Text.Json;
using System.Diagnostics;
using Xunit.Abstractions;

namespace Cleaner.Core.Tests;

public sealed class Beta5CleanupTests(ITestOutputHelper output) : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CleanerBeta5_" + Guid.NewGuid().ToString("N"));
    private string Temp => Path.Combine(_root, "temp");
    private string Session => Path.Combine(_root, "sessions", "fixture");
    private QuarantineService Quarantine => new(Session);
    private WhitelistService Whitelist => new(Path.Combine(_root, "whitelist.json"));
    private SafetyService Safety => new(0);
    private string MakeFile(string name, int hours = 48)
    {
        var path = Path.Combine(Temp, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture");
        File.SetCreationTimeUtc(path, DateTime.UtcNow.AddHours(-hours));
        File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddHours(-hours));
        return path;
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private OperationEntry Op => new() { Key = "fixture", Handler = "PathCleanup", Paths = new() { Temp } };
    private CleanupContext Context(CleanupFileMode mode, bool dryRun = false, CancellationToken token = default)
        => new(Safety, Whitelist, Quarantine, cancellationToken: token, dryRun: dryRun, fileMode: mode);

    [Theory]
    [InlineData(CleanupFileMode.Quarantine)]
    [InlineData(CleanupFileMode.PermanentDelete)]
    public void ModeControlsDestinationAndAccounting(CleanupFileMode mode)
    {
        var path = MakeFile("old.txt");
        var result = new PathCleanupHandler().Run(Op, Context(mode));
        Assert.True(result.Success);
        Assert.False(File.Exists(path));
        Assert.Equal(mode == CleanupFileMode.Quarantine ? 1 : 0, result.FilesQuarantined);
        Assert.Equal(mode == CleanupFileMode.PermanentDelete ? 1 : 0, result.FilesDeleted);
        Assert.Equal(mode == CleanupFileMode.PermanentDelete ? 7 : 0, result.BytesFreed);
        Assert.Equal(mode == CleanupFileMode.Quarantine ? 7 : 0, result.BytesQuarantined);
        Assert.Equal(mode == CleanupFileMode.Quarantine ? 1 : 0, Quarantine.Count);
    }

    [Fact]
    public void PermanentPreviewAndCancellationKeepFiles()
    {
        var path = MakeFile("keep.txt");
        var result = new PathCleanupHandler().Run(Op, Context(CleanupFileMode.PermanentDelete, dryRun: true));
        Assert.Equal(7, result.PreviewBytes);
        Assert.Equal(0, result.FilesDeleted);
        Assert.True(File.Exists(path));
        using var cancellation = new CancellationTokenSource(); cancellation.Cancel();
        result = new PathCleanupHandler().Run(Op, Context(CleanupFileMode.PermanentDelete, token: cancellation.Token));
        Assert.Equal(0, result.FilesDeleted);
        Assert.True(File.Exists(path));
    }

    [Theory]
    [InlineData(CleanupFileMode.Quarantine)]
    [InlineData(CleanupFileMode.PermanentDelete)]
    public void LockedFileIsAnExplainedSkipNotFailedOperation(CleanupFileMode mode)
    {
        var path = MakeFile("locked.txt");
        var context = Context(mode);
        using var handle = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
        var result = new PathCleanupHandler().Run(Op, context);
        Assert.True(result.Success, string.Join(";", result.Errors));
        Assert.Equal(1, result.FilesSkipped);
        Assert.Equal("file_in_use", Assert.Single(context.SkipDetails).Reason);
        Assert.NotEmpty(Assert.Single(context.SkipDetails).Detail!);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void DirectDeletionHonorsProtectedTreesAndWhitelist()
    {
        var keep = MakeFile("excluded/keep.txt");
        var free = MakeFile("free.txt");
        var context = new CleanupContext(new SafetyService(0, new[] { Path.GetDirectoryName(keep)! }), Whitelist, Quarantine,
            fileMode: CleanupFileMode.PermanentDelete);
        var result = new PathCleanupHandler().Run(Op, context);
        Assert.Equal(1, result.FilesDeleted);
        Assert.True(File.Exists(keep)); Assert.False(File.Exists(free));
        var whitelisted = MakeFile("whitelisted.txt");
        var whitelist = Whitelist; whitelist.Add(whitelisted); whitelist.Add(keep);
        result = new PathCleanupHandler().Run(Op, new CleanupContext(Safety, whitelist, Quarantine, fileMode: CleanupFileMode.PermanentDelete));
        Assert.True(File.Exists(whitelisted)); Assert.Equal(0, result.FilesDeleted);
    }

    [Fact]
    public void AutomaticCleanupUsesQuarantineAndRequiresBothTimestampsToBeOld()
    {
        var old = MakeFile("old.txt");
        var modified = MakeFile("modified.txt"); File.SetLastWriteTimeUtc(modified, DateTime.UtcNow);
        var created = MakeFile("created.txt", 1);
        var excluded = MakeFile("excluded.txt"); var whitelist = Whitelist; whitelist.Add(excluded);
        var result = AutomaticCleanup.Run(Temp, Safety, whitelist, Quarantine);
        Assert.True(result.FilesQuarantined == 1, JsonSerializer.Serialize(result)); Assert.Equal(0, result.FilesDeleted);
        Assert.False(File.Exists(old)); Assert.True(File.Exists(modified)); Assert.True(File.Exists(created)); Assert.True(File.Exists(excluded));
        Assert.Equal(3, result.FilesSkipped);
        Assert.Contains(result.SkipDetails, skip => skip.Reason == "too_recent");
    }

    private QuarantineService MakeBulkSession(int count)
    {
        Directory.CreateDirectory(Session);
        var entries = Enumerable.Range(0, count).Select(i => new QuarantineEntry { OriginalPath = Path.Combine(Temp, i + ".txt"),
            StoredName = i + ".txt", SizeBytes = 7, When = DateTime.UtcNow.ToString("o") }).ToArray();
        foreach (var entry in entries) File.WriteAllText(Path.Combine(Session, entry.StoredName), "fixture");
        File.WriteAllText(Path.Combine(Session, "manifest.json"), JsonSerializer.Serialize(new { schemaVersion = 1, sessionId = "fixture",
            createdUtc = DateTime.UtcNow.ToString("o"), entries }));
        return Quarantine;
    }
    private sealed class InlineProgress(Action<QuarantineProgress> callback) : IProgress<QuarantineProgress>
    { public void Report(QuarantineProgress progress) => callback(progress); }
    private sealed class SimulatedCrash : Exception;

    [Fact]
    public void InterruptedBatchDeletionCanResumeWithoutTouchingOriginalPaths()
    {
        var quarantine = MakeBulkSession(130);
        var outside = MakeFile("0.txt");
        Assert.Throws<SimulatedCrash>(() => quarantine.ClearAllDetailed(progress: new InlineProgress(p =>
        { if (p.Completed == 69) throw new SimulatedCrash(); })));
        var session = Assert.Single(QuarantineService.InspectSessions(Path.Combine(_root, "sessions")));
        Assert.Equal(QuarantineHealth.DeletionInterrupted, session.Health);
        Assert.True(session.CanDelete); Assert.False(session.CanRestore);
        var result = new QuarantineService(Session).ClearAllDetailed();
        Assert.Empty(result.Errors); Assert.False(Directory.Exists(Session)); Assert.True(File.Exists(outside));
    }

    [Fact]
    public void CancellationSavesRemainingEntriesAndAllowsRestoration()
    {
        var quarantine = MakeBulkSession(130);
        using var cancellation = new CancellationTokenSource();
        var result = quarantine.ClearAllDetailed(cancellation.Token, new InlineProgress(p => { if (p.Completed == 69) cancellation.Cancel(); }));
        Assert.True(result.Canceled); Assert.Equal(69, result.Deleted);
        Assert.Equal(61, new QuarantineService(Session).Count);
        var session = Assert.Single(QuarantineService.InspectSessions(Path.Combine(_root, "sessions")));
        Assert.Equal(QuarantineHealth.Healthy, session.Health); Assert.True(session.CanRestore);
    }

    [Fact]
    public void BulkDeletionOf3000FixtureFilesCompletesWithEmptyJournal()
    {
        var quarantine = MakeBulkSession(3000);
        var watch = Stopwatch.StartNew();
        var result = quarantine.ClearAllDetailed();
        output.WriteLine("3000 fixture files deleted in " + watch.Elapsed);
        Assert.Equal(3000, result.Deleted); Assert.Equal(0, result.Failed);
        Assert.Empty(result.Errors); Assert.False(Directory.Exists(Session));
    }

    [Fact]
    public void FailedBatchCheckpointKeepsDurableDeletionIntentForRetry()
    {
        var quarantine = MakeBulkSession(130);
        FileStream? lockedManifest = null;
        try
        {
            var result = quarantine.ClearAllDetailed(progress: new InlineProgress(p =>
            {
                if (p.Completed == 69 && lockedManifest == null)
                    lockedManifest = File.Open(Path.Combine(Session, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.Read);
            }));
            Assert.NotEmpty(result.Errors); Assert.True(result.Failed > 0);
        }
        finally { lockedManifest?.Dispose(); }
        var session = Assert.Single(QuarantineService.InspectSessions(Path.Combine(_root, "sessions")));
        Assert.Equal(QuarantineHealth.DeletionInterrupted, session.Health);
        Assert.Empty(new QuarantineService(Session).ClearAllDetailed().Errors);
        Assert.False(Directory.Exists(Session));
    }

    [Fact]
    public void DirectDeletionDoesNotForceReadOnlyAttributes()
    {
        var path = MakeFile("readonly.txt"); File.SetAttributes(path, FileAttributes.ReadOnly);
        try
        {
            var context = Context(CleanupFileMode.PermanentDelete);
            var result = new PathCleanupHandler().Run(Op, context);
            Assert.True(result.Success); Assert.Equal(0, result.FilesDeleted);
            Assert.Equal("access_denied", Assert.Single(context.SkipDetails).Reason);
            Assert.True((File.GetAttributes(path) & FileAttributes.ReadOnly) != 0);
        }
        finally { File.SetAttributes(path, FileAttributes.Normal); }
    }
}
