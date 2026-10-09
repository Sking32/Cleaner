using System;
using Cleaner.Core.Data;
using Cleaner.Core.Registry;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup.Handlers;

public class RegCleanRunMRUHandlerTests : IDisposable
{
    private const string Hive = "HKCU";
    private const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU";
    private readonly string _qDir;

    public RegCleanRunMRUHandlerTests()
    {
        _qDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "CleanerRMRUq_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_qDir);
    }

    public void Dispose()
    {
        try { RegistryHelper.DeleteKey(Hive, SubKey, recursive: true); } catch { }
        try { if (System.IO.Directory.Exists(_qDir)) System.IO.Directory.Delete(_qDir, recursive: true); } catch { }
    }

    private CleanupContext Ctx()
        => new(new SafetyService(5),
               new WhitelistService(System.IO.Path.Combine(_qDir, "wl.json")),
               new QuarantineService(_qDir));

    [Fact]
    public void Clears_Values_In_RunMRU()
    {
        RegistryHelper.SetValue(Hive, SubKey, "a", "cmd.exe\\1", RegistryValueKind.String, true);
        RegistryHelper.SetValue(Hive, SubKey, "b", "notepad.exe\\1", RegistryValueKind.String, true);

        var handler = new RegCleanRunMRUHandler();
        var op = new OperationEntry { Kind = "operation", Key = "run_history", Handler = "RegCleanRunMRU" };
        var result = handler.Run(op, Ctx());

        Assert.True(result.Success, string.Join(";", result.Errors));
        Assert.Null(RegistryHelper.GetValue(Hive, SubKey, "a"));
        Assert.Null(RegistryHelper.GetValue(Hive, SubKey, "b"));
    }

    [Fact]
    public void Missing_Key_Returns_Ok()
    {
        try { RegistryHelper.DeleteKey(Hive, SubKey, recursive: true); } catch { }
        var handler = new RegCleanRunMRUHandler();
        var op = new OperationEntry { Kind = "operation", Key = "run_history", Handler = "RegCleanRunMRU" };
        var r = handler.Run(op, Ctx());
        Assert.True(r.Success);
    }

    [Fact]
    public void Handler_Name_Is_Correct()
    {
        Assert.Equal("RegCleanRunMRU", new RegCleanRunMRUHandler().Name);
    }
}