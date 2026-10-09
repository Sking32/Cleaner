using System;
using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup.Handlers;

public class MultiPathCleanupHandlerTests : IDisposable
{
    private readonly string _root;
    private readonly string _dirA;
    private readonly string _dirB;
    private readonly string _quarantineDir;
    private readonly string _whitelistFile;

    private readonly SafetyService _safety;
    private readonly WhitelistService _whitelist;
    private readonly QuarantineService _quarantine;
    private readonly MultiPathCleanupHandler _handler;

    public MultiPathCleanupHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CleanerMPC_" + Guid.NewGuid().ToString("N"));
        _dirA = Path.Combine(_root, "a");
        _dirB = Path.Combine(_root, "b");
        _quarantineDir = Path.Combine(_root, "q");
        _whitelistFile = Path.Combine(_root, "wl.json");

        Directory.CreateDirectory(_dirA);
        Directory.CreateDirectory(_dirB);
        Directory.CreateDirectory(_quarantineDir);

        var old = DateTime.UtcNow.AddMinutes(-30);
        Directory.SetCreationTimeUtc(_root, old);
        Directory.SetCreationTimeUtc(_dirA, old);
        Directory.SetCreationTimeUtc(_dirB, old);

        _safety = new SafetyService(freshMinutes: 5);
        _whitelist = new WhitelistService(_whitelistFile);
        _quarantine = new QuarantineService(_quarantineDir);
        _handler = new MultiPathCleanupHandler();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string MakeFile(string dir, string name, string content = "x")
    {
        var p = Path.Combine(dir, name);
        File.WriteAllText(p, content);
        File.SetCreationTimeUtc(p, DateTime.UtcNow.AddMinutes(-30));
        return p;
    }

    private CleanupContext Ctx() => new(_safety, _whitelist, _quarantine);

    private OperationEntry Op() => new()
    {
        Kind = "operation",
        Key = "test",
        Handler = "MultiPathCleanup",
        Paths = new List<string> { _dirA, _dirB }
    };

    [Fact]
    public void Cleans_Both_Directories()
    {
        var f1 = MakeFile(_dirA, "a.tmp");
        var f2 = MakeFile(_dirB, "b.tmp");

        var result = _handler.Run(Op(), Ctx());

        Assert.True(result.Success);
        Assert.False(File.Exists(f1));
        Assert.False(File.Exists(f2));
        Assert.Equal(2, result.FilesDeleted);
    }

    [Fact]
    public void Keeps_Directories()
    {
        MakeFile(_dirA, "x.tmp");
        _handler.Run(Op(), Ctx());

        Assert.True(Directory.Exists(_dirA));
        Assert.True(Directory.Exists(_dirB));
    }

    [Fact]
    public void Handles_Missing_Directory_Gracefully()
    {
        var op = new OperationEntry
        {
            Kind = "operation",
            Key = "test",
            Handler = "MultiPathCleanup",
            Paths = new List<string> { _dirA, Path.Combine(_root, "nope") }
        };
        var f = MakeFile(_dirA, "x.tmp");

        var result = _handler.Run(op, Ctx());

        Assert.True(result.Success);
        Assert.False(File.Exists(f));
    }
}