using System;
using System.IO;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup.Handlers;

public class WindowsStoreCacheHandlerTests : IDisposable
{
    private readonly string _root;

    public WindowsStoreCacheHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CleanerWSC_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private CleanupContext Ctx(bool dryRun = false)
    {
        var q = Path.Combine(_root, "q");
        Directory.CreateDirectory(q);
        return new CleanupContext(
            new SafetyService(5),
            new WhitelistService(Path.Combine(_root, "wl.json")),
            new QuarantineService(q),
            dryRun: dryRun);
    }

    [Fact]
    public void Handler_Name_Is_Correct()
    {
        Assert.Equal("WindowsStoreCache", new WindowsStoreCacheHandler().Name);
    }

    [Fact]
    public void DryRun_Returns_Ok()
    {
        var handler = new WindowsStoreCacheHandler();
        var op = new OperationEntry { Kind = "operation", Key = "windows_store", Handler = "WindowsStoreCache" };
        var r = handler.Run(op, Ctx(dryRun: true));
        Assert.True(r.Success);
        Assert.Equal("dry-run", r.Message);
    }
}