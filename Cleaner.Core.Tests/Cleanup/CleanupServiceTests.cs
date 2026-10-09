using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup;

public class CleanupServiceTests
{
    private readonly CleanupService _svc;

    public CleanupServiceTests()
    {
        _svc = new CleanupService(new OperationsRepository());
    }

    [Fact]
    public void Registers_All_Known_Handlers()
    {
        // Простые
        Assert.True(_svc.Handlers.ContainsKey("PathCleanup"));
        Assert.True(_svc.Handlers.ContainsKey("MultiPathCleanup"));
        Assert.True(_svc.Handlers.ContainsKey("PathCleanupRecursive"));
        Assert.True(_svc.Handlers.ContainsKey("Command"));

        // Браузеры
        Assert.True(_svc.Handlers.ContainsKey("BrowserProfiles"));
        Assert.True(_svc.Handlers.ContainsKey("FirefoxProfiles"));
        Assert.True(_svc.Handlers.ContainsKey("JumpLists"));
        Assert.True(_svc.Handlers.ContainsKey("ObsLogs"));

        // Кэши
        Assert.True(_svc.Handlers.ContainsKey("ThumbnailCache"));
        Assert.True(_svc.Handlers.ContainsKey("FontCache"));
        Assert.True(_svc.Handlers.ContainsKey("WindowsStoreCache"));

        // Реестр
        Assert.True(_svc.Handlers.ContainsKey("RegCleanKey"));
        Assert.True(_svc.Handlers.ContainsKey("RegCleanRunMRU"));
        Assert.True(_svc.Handlers.ContainsKey("ActivityHistory"));

        // Тяжёлое
        Assert.True(_svc.Handlers.ContainsKey("SmartDownloads"));
        Assert.True(_svc.Handlers.ContainsKey("WindowsUpdateCache"));
        Assert.True(_svc.Handlers.ContainsKey("DismComponentCleanup"));
        Assert.True(_svc.Handlers.ContainsKey("WindowsOld"));
        Assert.True(_svc.Handlers.ContainsKey("ShadowCopy"));
        Assert.True(_svc.Handlers.ContainsKey("ToggleHibernation"));
    }

    [Fact]
    public void Every_Operation_In_Json_Has_Handler()
    {
        var ops = new OperationsRepository();
        foreach (var op in ops.Operations)
        {
            Assert.True(_svc.Handlers.ContainsKey(op.Handler!),
                $"{op.Key}: no handler for '{op.Handler}'");
        }
    }

    [Fact]
    public void Unknown_Operation_Returns_Fail()
    {
        var ctx = Ctx();
        var r = _svc.RunOperation("no-such-op", ctx);
        Assert.False(r.Success);
        Assert.Contains("not found", r.Message);
    }

    [Fact]
    public void Section_Is_Not_Operation()
    {
        var ctx = Ctx();
        var r = _svc.RunOperation("sec.quick", ctx);
        Assert.False(r.Success);
    }

    [Fact]
    public void Empty_Key_Returns_Fail()
    {
        var ctx = Ctx();
        Assert.False(_svc.RunOperation("", ctx).Success);
        Assert.False(_svc.RunOperation("   ", ctx).Success);
    }

    [Fact]
    public void Custom_Handler_Can_Override_Default()
    {
        _svc.Register(new FakeHandler("PathCleanup"));
        Assert.IsType<FakeHandler>(_svc.Handlers["PathCleanup"]);
    }

    // ------------------------------------------------------------------

    private static CleanupContext Ctx()
    {
        var root = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "CleanerCS_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(root);
        return new CleanupContext(
            new SafetyService(5),
            new WhitelistService(System.IO.Path.Combine(root, "wl.json")),
            new QuarantineService(System.IO.Path.Combine(root, "q")));
    }

    private sealed class FakeHandler : ICleanupHandler
    {
        public string Name { get; }
        public FakeHandler(string name) => Name = name;
        public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
            => CleanupOperationResult.Ok();
    }
}