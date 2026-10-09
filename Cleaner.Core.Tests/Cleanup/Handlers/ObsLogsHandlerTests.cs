using System;
using System.IO;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup.Handlers;

public class ObsLogsHandlerTests : IDisposable
{
    private readonly string _root;

    public ObsLogsHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CleanerObs_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Handler_Name_Is_Correct()
    {
        Assert.Equal("ObsLogs", new ObsLogsHandler().Name);
    }

    [Fact]
    public void Missing_Obs_Returns_Ok()
    {
        var q = Path.Combine(_root, "q");
        Directory.CreateDirectory(q);
        var ctx = new CleanupContext(
            new SafetyService(freshMinutes: 5),
            new WhitelistService(Path.Combine(_root, "wl.json")),
            new QuarantineService(q));

        var handler = new ObsLogsHandler();
        var op = new OperationEntry
        {
            Kind = "operation",
            Key = "obs_logs",
            Handler = "ObsLogs",
            OlderThanDays = 7
        };
        var r = handler.Run(op, ctx);
        Assert.True(r.Success);
    }
}