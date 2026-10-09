using System;
using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup.Handlers;

public class PathCleanupRecursiveHandlerTests : IDisposable
{
    private readonly string _root;
    private readonly string _logsDir;
    private readonly string _quarantineDir;
    private readonly string _whitelistFile;

    private readonly SafetyService _safety;
    private readonly WhitelistService _whitelist;
    private readonly QuarantineService _quarantine;
    private readonly PathCleanupRecursiveHandler _handler;

    public PathCleanupRecursiveHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CleanerPCR_" + Guid.NewGuid().ToString("N"));
        _logsDir = Path.Combine(_root, "Logs");
        _quarantineDir = Path.Combine(_root, "q");
        _whitelistFile = Path.Combine(_root, "wl.json");

        Directory.CreateDirectory(_logsDir);
        Directory.CreateDirectory(_quarantineDir);

        var old = DateTime.UtcNow.AddMinutes(-30);
        Directory.SetCreationTimeUtc(_root, old);
        Directory.SetCreationTimeUtc(_logsDir, old);

        _safety = new SafetyService(freshMinutes: 5);
        _whitelist = new WhitelistService(_whitelistFile);
        _quarantine = new QuarantineService(_quarantineDir);
        _handler = new PathCleanupRecursiveHandler();
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private static string MakeFile(string dir, string relative)
    {
        var p = Path.Combine(dir, relative);
        var parent = Path.GetDirectoryName(p)!;
        Directory.CreateDirectory(parent);
        Directory.SetCreationTimeUtc(parent, DateTime.UtcNow.AddMinutes(-30));
        File.WriteAllText(p, "x");
        File.SetCreationTimeUtc(p, DateTime.UtcNow.AddMinutes(-30));
        return p;
    }

    private CleanupContext Ctx() => new(_safety, _whitelist, _quarantine);

    private OperationEntry Op(params string[] skip)
        => new()
        {
            Kind = "operation",
            Key = "test",
            Handler = "PathCleanupRecursive",
            Paths = new List<string> { _logsDir },
            SkipSubdirs = new List<string>(skip)
        };

    [Fact]
    public void Cleans_Recursively()
    {
        var f1 = MakeFile(_logsDir, "a.log");
        var f2 = MakeFile(_logsDir, Path.Combine("sub", "b.log"));

        var result = _handler.Run(Op(), Ctx());

        Assert.True(result.Success);
        Assert.False(File.Exists(f1));
        Assert.False(File.Exists(f2));
    }

    [Fact]
    public void Skips_Subdir_By_Name()
    {
        var keep = MakeFile(_logsDir, Path.Combine("CBS", "important.log"));
        var remove = MakeFile(_logsDir, "other.log");

        var result = _handler.Run(Op("CBS"), Ctx());

        Assert.True(File.Exists(keep));
        Assert.False(File.Exists(remove));
        Assert.True(result.FilesSkipped >= 1);
    }

    [Fact]
    public void Skips_Are_Case_Insensitive()
    {
        var keep = MakeFile(_logsDir, Path.Combine("cbs", "x.log"));
        _handler.Run(Op("CBS"), Ctx());
        Assert.True(File.Exists(keep));
    }

    [Fact]
    public void Root_Directory_Not_Deleted()
    {
        MakeFile(_logsDir, "a.log");
        _handler.Run(Op(), Ctx());
        Assert.True(Directory.Exists(_logsDir));
    }
}