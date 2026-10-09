using System;
using System.IO;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup.Handlers;

public class PathCleanupHandlerTests : IDisposable
{
    private readonly string _root;
    private readonly string _tempDir;
    private readonly string _quarantineDir;
    private readonly string _whitelistFile;

    private readonly SafetyService _safety;
    private readonly WhitelistService _whitelist;
    private readonly QuarantineService _quarantine;
    private readonly PathCleanupHandler _handler;

    public PathCleanupHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CleanerPC_" + Guid.NewGuid().ToString("N"));
        _tempDir = Path.Combine(_root, "temp");
        _quarantineDir = Path.Combine(_root, "q");
        _whitelistFile = Path.Combine(_root, "wl.json");

        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_quarantineDir);

        // Отодвигаем создание папки назад, чтобы SafetyService не считал "слишком свежей".
        Directory.SetCreationTimeUtc(_tempDir, DateTime.UtcNow.AddMinutes(-30));
        Directory.SetCreationTimeUtc(_root, DateTime.UtcNow.AddMinutes(-30));

        _safety = new SafetyService(freshMinutes: 5);
        _whitelist = new WhitelistService(_whitelistFile);
        _quarantine = new QuarantineService(_quarantineDir);
        _handler = new PathCleanupHandler();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private string MakeFile(string name, string content = "x")
    {
        var p = Path.Combine(_tempDir, name);
        var parent = Path.GetDirectoryName(p);
        if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        File.WriteAllText(p, content);
        File.SetCreationTimeUtc(p, DateTime.UtcNow.AddMinutes(-30));
        return p;
    }

    private string MakeSubdir(string name)
    {
        var p = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(p);
        Directory.SetCreationTimeUtc(p, DateTime.UtcNow.AddMinutes(-30));
        return p;
    }

    private CleanupContext Ctx(bool dryRun = false)
        => new(_safety, _whitelist, _quarantine, dryRun: dryRun);

    private OperationEntry OpFor(string path)
        => new()
        {
            Kind = "operation",
            Key = "test",
            Handler = "PathCleanup",
            Paths = new System.Collections.Generic.List<string> { path }
        };

    [Fact]
    public void Deletes_All_Files_In_Directory()
    {
        var f1 = MakeFile("a.tmp");
        var f2 = MakeFile("b.tmp");

        var result = _handler.Run(OpFor(_tempDir), Ctx());

        Assert.True(result.Success, string.Join("; ", result.Errors));
        Assert.False(File.Exists(f1));
        Assert.False(File.Exists(f2));
        Assert.Equal(2, result.FilesDeleted);
    }

    [Fact]
    public void Moves_Files_To_Quarantine()
    {
        var f = MakeFile("q.tmp", "hello");
        _handler.Run(OpFor(_tempDir), Ctx());

        Assert.False(File.Exists(f));
        Assert.Equal(1, _quarantine.Count);
    }

    [Fact]
    public void Directory_Itself_Remains()
    {
        MakeFile("a.tmp");
        _handler.Run(OpFor(_tempDir), Ctx());
        Assert.True(Directory.Exists(_tempDir));
    }

    [Fact]
    public void Deletes_Nested_Files_And_Directories()
    {
        var sub = MakeSubdir("sub");
        var nested = Path.Combine(sub, "n.tmp");
        File.WriteAllText(nested, "x");
        File.SetCreationTimeUtc(nested, DateTime.UtcNow.AddMinutes(-30));

        var result = _handler.Run(OpFor(_tempDir), Ctx());

        Assert.True(result.Success);
        Assert.False(File.Exists(nested));
        Assert.False(Directory.Exists(sub));
        Assert.True(result.FilesDeleted >= 1);
    }

    [Fact]
    public void Skips_Whitelisted_File()
    {
        var keep = MakeFile("keep.me", "important");
        var remove = MakeFile("remove.tmp", "x");

        _whitelist.Add("*.me");

        var result = _handler.Run(OpFor(_tempDir), Ctx());

        Assert.True(File.Exists(keep));
        Assert.False(File.Exists(remove));
        Assert.Equal(1, result.FilesDeleted);
        Assert.Equal(1, result.FilesSkipped);
    }

    [Fact]
    public void DryRun_Does_Not_Delete()
    {
        var f = MakeFile("d.tmp");
        var result = _handler.Run(OpFor(_tempDir), Ctx(dryRun: true));

        Assert.True(File.Exists(f));
        Assert.Equal(0, result.FilesDeleted);
        Assert.Equal(0, _quarantine.Count);
        Assert.True(result.BytesFreed > 0);
    }

    [Fact]
    public void Missing_Path_Does_Not_Throw()
    {
        var op = OpFor(Path.Combine(_root, "does-not-exist"));
        var result = _handler.Run(op, Ctx());
        Assert.True(result.Success);
        Assert.Equal(0, result.FilesDeleted);
    }

    [Fact]
    public void No_Paths_Returns_Ok()
    {
        var op = new OperationEntry { Kind = "operation", Key = "test", Handler = "PathCleanup" };
        var result = _handler.Run(op, Ctx());
        Assert.True(result.Success);
    }

    [Fact]
    public void Reports_BytesFreed()
    {
        MakeFile("x.tmp", "12345");
        MakeFile("y.tmp", "1234567890");

        var result = _handler.Run(OpFor(_tempDir), Ctx());

        Assert.Equal(15, result.BytesFreed);
    }
}