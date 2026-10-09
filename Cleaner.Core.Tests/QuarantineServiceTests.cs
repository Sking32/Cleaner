using System;
using System.IO;
using System.Text.Json;
using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Core.Tests;

public class QuarantineServiceTests : IDisposable
{
    private readonly string _root;
    private readonly string _sourceDir;
    private readonly string _sessionDir;

    public QuarantineServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CleanerQTest_" + Guid.NewGuid().ToString("N"));
        _sourceDir = Path.Combine(_root, "src");
        _sessionDir = Path.Combine(_root, "sess");
        Directory.CreateDirectory(_sourceDir);
        Directory.CreateDirectory(_sessionDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private string MakeFile(string relativeName, string content = "data")
    {
        var p = Path.Combine(_sourceDir, relativeName);
        var parent = Path.GetDirectoryName(p);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.WriteAllText(p, content);
        return p;
    }

    private string MakeDir(string relativeName)
    {
        var p = Path.Combine(_sourceDir, relativeName);
        Directory.CreateDirectory(p);
        return p;
    }

    // ---------- MoveToQuarantine ----------

    [Fact]
    public void MoveToQuarantine_Moves_File_And_Records_Entry()
    {
        var src = MakeFile("a.txt", "hello");
        var q = new QuarantineService(_sessionDir);

        var entry = q.MoveToQuarantine(src, "temp");

        Assert.NotNull(entry);
        Assert.False(File.Exists(src));
        Assert.True(File.Exists(Path.Combine(_sessionDir, entry!.StoredName)));
        Assert.Equal(src, entry.OriginalPath);
        Assert.False(entry.IsDirectory);
        Assert.Equal(5, entry.SizeBytes);
        Assert.Equal("temp", entry.Category);
        Assert.Equal(1, q.Count);
        Assert.Equal(5, q.TotalSizeBytes);
    }

    [Fact]
    public void MoveToQuarantine_Moves_Directory()
    {
        var dir = MakeDir("sub");
        File.WriteAllText(Path.Combine(dir, "x.txt"), "abc");
        File.WriteAllText(Path.Combine(dir, "y.txt"), "de");

        var q = new QuarantineService(_sessionDir);
        var entry = q.MoveToQuarantine(dir, "logs");

        Assert.NotNull(entry);
        Assert.False(Directory.Exists(dir));
        Assert.True(entry!.IsDirectory);
        Assert.True(Directory.Exists(Path.Combine(_sessionDir, entry.StoredName)));
        Assert.Equal(5, entry.SizeBytes);
    }

    [Fact]
    public void MoveToQuarantine_Returns_Null_For_Missing_Path()
    {
        var q = new QuarantineService(_sessionDir);
        var missing = Path.Combine(_sourceDir, "nope.txt");

        Assert.Null(q.MoveToQuarantine(missing, "temp"));
        Assert.Equal(0, q.Count);
    }

    [Fact]
    public void MoveToQuarantine_Returns_Null_For_Empty_Path()
    {
        var q = new QuarantineService(_sessionDir);
        Assert.Null(q.MoveToQuarantine("", "temp"));
        Assert.Null(q.MoveToQuarantine("   ", "temp"));
    }

    [Fact]
    public void MoveToQuarantine_Handles_Duplicate_File_Names()
    {
        var a = MakeFile("dupe.txt", "A");
        var b = MakeFile(Path.Combine("other", "dupe.txt"), "BB");

        var q = new QuarantineService(_sessionDir);
        var e1 = q.MoveToQuarantine(a, "temp");
        var e2 = q.MoveToQuarantine(b, "temp");

        Assert.NotNull(e1);
        Assert.NotNull(e2);
        Assert.NotEqual(e1!.StoredName, e2!.StoredName);
        Assert.Equal(2, q.Count);
    }

    [Fact]
    public void MoveToQuarantine_Defaults_Category()
    {
        var src = MakeFile("c.txt");
        var q = new QuarantineService(_sessionDir);
        var entry = q.MoveToQuarantine(src, "");
        Assert.NotNull(entry);
        Assert.Equal("General", entry!.Category);
    }

    // ---------- Manifest persistence ----------

    [Fact]
    public void Manifest_Persists_Across_Reload()
    {
        var src = MakeFile("p.txt", "persistent");
        var q1 = new QuarantineService(_sessionDir);
        q1.MoveToQuarantine(src, "temp");

        var q2 = new QuarantineService(_sessionDir);
        Assert.Equal(1, q2.Count);
        Assert.Single(q2.Entries);
        Assert.Equal(src, q2.Entries[0].OriginalPath);
    }

    // ---------- RestoreAll ----------

    [Fact]
    public void RestoreAll_Restores_Files()
    {
        var a = MakeFile("a.txt", "A");
        var b = MakeFile("b.txt", "B");

        var q = new QuarantineService(_sessionDir);
        q.MoveToQuarantine(a, "temp");
        q.MoveToQuarantine(b, "temp");

        Assert.False(File.Exists(a));
        Assert.False(File.Exists(b));

        var result = q.RestoreAll();

        Assert.Equal(2, result.Restored);
        Assert.Equal(0, result.Failed);
        Assert.True(File.Exists(a));
        Assert.True(File.Exists(b));
        Assert.Equal("A", File.ReadAllText(a));
        Assert.Equal("B", File.ReadAllText(b));
    }

    [Fact]
    public void RestoreAll_Removes_Session_Directory_When_Complete()
    {
        var src = MakeFile("r.txt");
        var q = new QuarantineService(_sessionDir);
        q.MoveToQuarantine(src, "temp");

        q.RestoreAll();

        Assert.False(Directory.Exists(_sessionDir));
    }

    [Fact]
    public void RestoreAll_Recreates_Parent_Directory()
    {
        var src = MakeFile(Path.Combine("nested", "deep", "f.txt"), "x");
        var q = new QuarantineService(_sessionDir);
        q.MoveToQuarantine(src, "temp");

        var parent = Path.GetDirectoryName(src)!;
        Directory.Delete(parent, recursive: true);
        Assert.False(Directory.Exists(parent));

        var result = q.RestoreAll();

        Assert.Equal(1, result.Restored);
        Assert.True(File.Exists(src));
    }

    [Fact]
    public void RestoreAll_Uses_Restored_Suffix_On_Collision()
    {
        var src = MakeFile("c.txt", "original");
        var q = new QuarantineService(_sessionDir);
        q.MoveToQuarantine(src, "temp");

        // Кто-то создал файл на прежнем месте, пока он лежал в карантине
        File.WriteAllText(src, "new");

        var result = q.RestoreAll();

        Assert.Equal(1, result.Restored);
        Assert.True(File.Exists(src));
        Assert.Equal("new", File.ReadAllText(src));

        var restored = src + ".restored";
        Assert.True(File.Exists(restored));
        Assert.Equal("original", File.ReadAllText(restored));
    }

    [Fact]
    public void RestoreAll_Empty_Session_Returns_Zero()
    {
        var q = new QuarantineService(_sessionDir);
        var result = q.RestoreAll();
        Assert.Equal(0, result.Restored);
        Assert.Equal(0, result.Failed);
    }

    // ---------- ClearAll ----------

    [Fact]
    public void ClearAll_Deletes_Files_Permanently()
    {
        var src = MakeFile("z.txt");
        var q = new QuarantineService(_sessionDir);
        q.MoveToQuarantine(src, "temp");

        var count = q.ClearAll();

        Assert.Equal(1, count);
        Assert.False(Directory.Exists(_sessionDir));
        Assert.False(File.Exists(src));
    }

    // ---------- Static RestoreFromQuarantine ----------

    [Fact]
    public void RestoreFromQuarantine_Static_Works()
    {
        var src = MakeFile("st.txt", "static");
        var q = new QuarantineService(_sessionDir);
        q.MoveToQuarantine(src, "temp");

        var result = QuarantineService.RestoreFromQuarantine(_sessionDir);

        Assert.Equal(1, result.Restored);
        Assert.Equal(0, result.Failed);
        Assert.True(File.Exists(src));
        Assert.Equal("static", File.ReadAllText(src));
    }

    [Fact]
    public void RestoreFromQuarantine_Handles_Missing_Directory()
    {
        var result = QuarantineService.RestoreFromQuarantine(Path.Combine(_root, "nope"));
        Assert.Equal(0, result.Restored);
        Assert.Equal(0, result.Failed);
    }

    // ---------- ListSessions ----------

    [Fact]
    public void ListSessions_Returns_All_Sessions()
    {
        var sessionA = Path.Combine(_root, "sA");
        var sessionB = Path.Combine(_root, "sB");
        Directory.CreateDirectory(sessionA);
        Directory.CreateDirectory(sessionB);

        var srcA = MakeFile("aa.txt", "aa");
        var srcB = MakeFile("bb.txt", "bb");

        var qA = new QuarantineService(sessionA);
        qA.MoveToQuarantine(srcA, "temp");

        var qB = new QuarantineService(sessionB);
        qB.MoveToQuarantine(srcB, "temp");

        var sessions = QuarantineService.ListSessions(_root);

        Assert.Equal(2, sessions.Count);
        Assert.All(sessions, s => Assert.Equal(1, s.Count));
    }

    // ---------- ClearOldQuarantine ----------

    [Fact]
    public void ClearOldQuarantine_Removes_Old_Sessions()
    {
        var oldDir = Path.Combine(_root, "old");
        var newDir = Path.Combine(_root, "new");
        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(newDir);

        WriteManifest(oldDir, "old", DateTime.UtcNow.AddDays(-10));
        WriteManifest(newDir, "new", DateTime.UtcNow);

        var cleared = QuarantineService.ClearOldQuarantine(7, _root);

        Assert.Equal(1, cleared);
        Assert.False(Directory.Exists(oldDir));
        Assert.True(Directory.Exists(newDir));
    }

    [Fact]
    public void ClearOldQuarantine_Zero_Removes_Any_Older_Than_Now()
    {
        var dir = Path.Combine(_root, "ancient");
        Directory.CreateDirectory(dir);
        WriteManifest(dir, "ancient", DateTime.UtcNow.AddMinutes(-5));

        var cleared = QuarantineService.ClearOldQuarantine(0, _root);

        Assert.Equal(1, cleared);
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void ClearOldQuarantine_No_Root_Returns_Zero()
    {
        var missing = Path.Combine(_root, "does-not-exist");
        Assert.Equal(0, QuarantineService.ClearOldQuarantine(7, missing));
    }

    private static void WriteManifest(string sessionDir, string id, DateTime createdUtc)
    {
        var manifest = new
        {
            schemaVersion = 1,
            sessionId = id,
            createdUtc = createdUtc.ToString("o"),
            entries = Array.Empty<object>()
        };
        File.WriteAllText(
            Path.Combine(sessionDir, "manifest.json"),
            JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }
}