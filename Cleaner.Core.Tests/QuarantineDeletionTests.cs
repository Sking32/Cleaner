using System.Text.Json;
using Cleaner.Core.Services;

namespace Cleaner.Core.Tests;

public sealed class QuarantineDeletionTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "CleanerQDelete_" + Guid.NewGuid().ToString("N"));
    private readonly List<string> _readOnlyPaths = new();
    private string Session => Path.Combine(_root, "session");

    public QuarantineDeletionTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        foreach (var path in _readOnlyPaths)
            if (File.Exists(path) || Directory.Exists(path))
                File.SetAttributes(path, File.GetAttributes(path) & ~FileAttributes.ReadOnly);
        if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
    }

    private string MakeFile(string name)
    {
        var path = Path.Combine(_root, "source", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, "fixture data");
        return path;
    }

    private void MakeReadOnly(string path)
    {
        _readOnlyPaths.Add(path);
        File.SetAttributes(path, File.GetAttributes(path) | FileAttributes.ReadOnly);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ClearAllDeletesReadOnlyFileFromLegacyManifest(bool cleanOld)
    {
        Directory.CreateDirectory(Session);
        var original = MakeFile("ClrBindingWorker.cs");
        var stored = Path.Combine(Session, "00001_ClrBindingWorker.cs");
        File.Move(original, stored);
        MakeReadOnly(stored);
        // Older manifests omit pending and restorePath; use the old shape explicitly.
        File.WriteAllText(Path.Combine(Session, "manifest.json"), JsonSerializer.Serialize(new
        {
            schemaVersion = 1,
            sessionId = "session",
            createdUtc = DateTime.UtcNow.AddDays(-10).ToString("o"),
            entries = new[] { new { originalPath = original, storedName = Path.GetFileName(stored), isDirectory = false, sizeBytes = 12 } }
        }));

        Assert.Equal(1, cleanOld
            ? QuarantineService.ClearOldQuarantine(7, _root)
            : new QuarantineService(Session).ClearAll());
        Assert.False(Directory.Exists(Session));
        Assert.False(File.Exists(original));
    }

    [Fact]
    public void RepeatedDirectoryMovesCommitBeforeReadOnlyDeletion()
    {
        for (var i = 0; i < 100; i++)
        {
            MakeFile(Path.Combine("batch" + i, "nested", "data.txt"));
            var quarantine = new QuarantineService(Session);
            var entry = quarantine.MoveToQuarantine(Path.Combine(_root, "source", "batch" + i), "fixture")!;
            Assert.NotNull(entry);
            Assert.False(entry.Pending, "Final journal update pending at iteration " + i + "; see CoreLog diagnostics.");
            Assert.False(Assert.Single(new QuarantineService(Session).Entries).Pending);
            MakeReadOnly(Path.Combine(Session, entry.StoredName, "nested", "data.txt"));
            Assert.Equal(1, quarantine.ClearAll());
            Assert.False(Directory.Exists(Session));
        }
    }

    [Fact]
    public void ClearAllDeletesReadOnlyFilesAndNestedDirectories()
    {
        var original = MakeFile(Path.Combine("folder", "nested", "read-only.txt"));
        var sourceDirectory = Path.Combine(_root, "source", "folder");
        var quarantine = new QuarantineService(Session);
        var entry = quarantine.MoveToQuarantine(sourceDirectory, "fixture")!;
        Assert.False(entry.Pending, "The fixture move did not commit its journal; see CoreLog diagnostics.");
        var storedDirectory = Path.Combine(Session, entry.StoredName);
        MakeReadOnly(Path.Combine(storedDirectory, "nested", "read-only.txt"));
        MakeReadOnly(Path.Combine(storedDirectory, "nested"));
        MakeReadOnly(storedDirectory);

        Assert.Equal(1, quarantine.ClearAll());
        Assert.False(Directory.Exists(Session));
        Assert.False(File.Exists(original));
    }

    [Fact]
    public void LockedFileIsRetainedWhileOtherEntriesAreDeletedAndRetrySucceeds()
    {
        var quarantine = new QuarantineService(Session);
        var lockedEntry = quarantine.MoveToQuarantine(MakeFile("locked.txt"), "fixture")!;
        var freeEntry = quarantine.MoveToQuarantine(MakeFile("free.txt"), "fixture")!;
        var lockedPath = Path.Combine(Session, lockedEntry.StoredName);
        using (File.Open(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = quarantine.ClearAllDetailed();
            Assert.Equal(1, result.Deleted);
            Assert.Equal(1, result.Failed);
            Assert.Single(result.Errors);
            Assert.Contains(lockedEntry.StoredName, result.Errors[0]);
            Assert.True(File.Exists(lockedPath));
            Assert.False(File.Exists(Path.Combine(Session, freeEntry.StoredName)));
            var reloaded = new QuarantineService(Session);
            Assert.Equal(lockedEntry.StoredName, Assert.Single(reloaded.Entries).StoredName);
            Assert.Throws<IOException>(() => reloaded.ClearAll());
        }

        Assert.Equal(1, new QuarantineService(Session).ClearAll());
        Assert.False(Directory.Exists(Session));
    }

    [Fact]
    public void LockedNestedFileKeepsDirectoryEntryForRetry()
    {
        var quarantine = new QuarantineService(Session);
        MakeFile(Path.Combine("folder", "nested", "locked.txt"));
        MakeFile(Path.Combine("folder", "free.txt"));
        var entry = quarantine.MoveToQuarantine(Path.Combine(_root, "source", "folder"), "fixture")!;
        var lockedPath = Path.Combine(Session, entry.StoredName, "nested", "locked.txt");
        using (File.Open(lockedPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = quarantine.ClearAllDetailed();
            Assert.Equal(0, result.Deleted);
            Assert.Equal(1, result.Failed);
            Assert.Single(result.Errors);
            Assert.True(File.Exists(lockedPath));
            Assert.Single(new QuarantineService(Session).Entries);
        }

        Assert.Equal(1, new QuarantineService(Session).ClearAll());
        Assert.False(Directory.Exists(Session));
    }

    [Fact]
    public void UnwritableManifestPreventsAnyDeletion()
    {
        var quarantine = new QuarantineService(Session);
        var entry = quarantine.MoveToQuarantine(MakeFile("preserved.txt"), "fixture")!;
        using (File.Open(Path.Combine(Session, "manifest.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
            Assert.ThrowsAny<IOException>(() => quarantine.ClearAllDetailed());

        Assert.True(File.Exists(Path.Combine(Session, entry.StoredName)));
        Assert.Single(new QuarantineService(Session).Entries);
    }

    [Fact]
    public void StoredTypeMismatchIsRetainedForManualRecovery()
    {
        var quarantine = new QuarantineService(Session);
        var entry = quarantine.MoveToQuarantine(MakeFile("changed.txt"), "fixture")!;
        var stored = Path.Combine(Session, entry.StoredName);
        File.Delete(stored);
        Directory.CreateDirectory(stored);
        var unexpected = Path.Combine(stored, "unexpected.txt");
        File.WriteAllText(unexpected, "keep");

        var result = quarantine.ClearAllDetailed();
        Assert.Equal(0, result.Deleted);
        Assert.Equal(1, result.Failed);
        Assert.Single(result.Errors);
        Assert.Equal("keep", File.ReadAllText(unexpected));
        Assert.Single(new QuarantineService(Session).Entries);
    }

    [Fact]
    public void RestorePreservesReadOnlyAttribute()
    {
        var original = MakeFile("restore.txt");
        var quarantine = new QuarantineService(Session);
        var entry = quarantine.MoveToQuarantine(original, "fixture")!;
        MakeReadOnly(Path.Combine(Session, entry.StoredName));
        _readOnlyPaths.Add(original);

        Assert.Equal(1, quarantine.RestoreAll().Restored);
        Assert.True((File.GetAttributes(original) & FileAttributes.ReadOnly) != 0);
    }
}
