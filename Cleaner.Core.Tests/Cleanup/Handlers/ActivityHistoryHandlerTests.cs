using System;
using System.IO;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup.Handlers;

public class ActivityHistoryHandlerTests : IDisposable
{
    private readonly string _root;

    public ActivityHistoryHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CleanerAH_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Handler_Name_Is_Correct()
    {
        Assert.Equal("ActivityHistory", new ActivityHistoryHandler().Name);
    }

    [Fact]
    public void DryRun_Does_Not_Touch_Live_System()
    {
        var q = Path.Combine(_root, "q");
        Directory.CreateDirectory(q);

        var ctx = new CleanupContext(
            new SafetyService(5),
            new WhitelistService(Path.Combine(_root, "wl.json")),
            new QuarantineService(q),
            dryRun: true);

        var handler = new ActivityHistoryHandler();
        var op = new OperationEntry
        {
            Kind = "operation",
            Key = "activity_history",
            Handler = "ActivityHistory"
        };

        var r = handler.Run(op, ctx);

        Assert.True(r.Success, string.Join("; ", r.Errors));
        Assert.Equal(0, r.FilesDeleted);
    }
}