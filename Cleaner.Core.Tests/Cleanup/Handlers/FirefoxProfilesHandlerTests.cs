using System;
using System.IO;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup.Handlers;

public class FirefoxProfilesHandlerTests : IDisposable
{
    private readonly string _root;

    public FirefoxProfilesHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CleanerFF_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private CleanupContext Ctx()
    {
        var q = Path.Combine(_root, "q");
        Directory.CreateDirectory(q);
        return new CleanupContext(
            new SafetyService(freshMinutes: 5),
            new WhitelistService(Path.Combine(_root, "wl.json")),
            new QuarantineService(q));
    }

    [Fact]
    public void No_Firefox_Returns_Ok()
    {
        // На CI-машине Firefox обычно не установлен, тест должен пройти без падений.
        var handler = new FirefoxProfilesHandler();
        var op = new OperationEntry { Kind = "operation", Key = "firefox", Handler = "FirefoxProfiles" };
        var r = handler.Run(op, Ctx());
        Assert.True(r.Success);
    }

    [Fact]
    public void Handler_Name_Is_Correct()
    {
        Assert.Equal("FirefoxProfiles", new FirefoxProfilesHandler().Name);
    }
}