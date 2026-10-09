using System;
using System.IO;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup.Handlers;

public class ToggleHibernationHandlerTests : IDisposable
{
    private readonly string _root;

    public ToggleHibernationHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CleanerTH_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Handler_Name_Is_Correct()
    {
        Assert.Equal("ToggleHibernation", new ToggleHibernationHandler().Name);
    }

    [Fact]
    public void DryRun_Returns_Ok_Without_Invoking_Powercfg()
    {
        var q = Path.Combine(_root, "q");
        Directory.CreateDirectory(q);
        var ctx = new CleanupContext(
            new SafetyService(5),
            new WhitelistService(Path.Combine(_root, "wl.json")),
            new QuarantineService(q),
            dryRun: true);

        var handler = new ToggleHibernationHandler();
        var op = new OperationEntry { Kind = "operation", Key = "hibernate", Handler = "ToggleHibernation" };
        var r = handler.Run(op, ctx);
        Assert.True(r.Success);
        Assert.Equal("dry-run", r.Message);
    }
}