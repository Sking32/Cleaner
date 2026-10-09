using System;
using Cleaner.Core.Data;
using Cleaner.Core.Registry;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Microsoft.Win32;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup.Handlers;

public class RegCleanKeyHandlerTests : IDisposable
{
    private const string Hive = "HKCU";
    private const string RootKey = "Software\\CleanerTest_RegCleanKey";
    private readonly string _qDir;

    public RegCleanKeyHandlerTests()
    {
        try { RegistryHelper.DeleteKey(Hive, RootKey, recursive: true); } catch { }

        _qDir = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "CleanerRCKq_" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(_qDir);
    }

    public void Dispose()
    {
        try { RegistryHelper.DeleteKey(Hive, RootKey, recursive: true); } catch { }
        try { if (System.IO.Directory.Exists(_qDir)) System.IO.Directory.Delete(_qDir, recursive: true); } catch { }
    }

    private CleanupContext Ctx()
        => new(new SafetyService(5),
               new WhitelistService(System.IO.Path.Combine(_qDir, "wl.json")),
               new QuarantineService(_qDir));

    private OperationEntry Op()
        => new()
        {
            Kind = "operation",
            Key = "recent_typed",
            Handler = "RegCleanKey",
            RegistryKey = $"HKCU:\\{RootKey}"
        };

    [Fact]
    public void Deletes_Values_But_Keeps_Key()
    {
        RegistryHelper.SetValue(Hive, RootKey, "a", "1", RegistryValueKind.String, true);
        RegistryHelper.SetValue(Hive, RootKey, "b", "2", RegistryValueKind.String, true);
        RegistryHelper.SetValue(Hive, RootKey, "URL1", "https://x", RegistryValueKind.String, true);

        var handler = new RegCleanKeyHandler();
        var result = handler.Run(Op(), Ctx());

        Assert.True(result.Success, string.Join(";", result.Errors));
        Assert.True(RegistryHelper.KeyExists(Hive, RootKey));
        Assert.Null(RegistryHelper.GetValue(Hive, RootKey, "URL1"));
        Assert.Null(RegistryHelper.GetValue(Hive, RootKey, "a"));
        Assert.Null(RegistryHelper.GetValue(Hive, RootKey, "b"));
    }

    [Fact]
    public void Missing_Key_Returns_Ok()
    {
        var handler = new RegCleanKeyHandler();
        var result = handler.Run(Op(), Ctx());
        Assert.True(result.Success);
    }

    [Fact]
    public void Invalid_Path_Fails()
    {
        var op = new OperationEntry
        {
            Kind = "operation",
            Key = "x",
            Handler = "RegCleanKey",
            RegistryKey = "garbage"
        };
        var handler = new RegCleanKeyHandler();
        var result = handler.Run(op, Ctx());
        Assert.False(result.Success);
    }

    [Fact]
    public void DryRun_Does_Not_Delete()
    {
        RegistryHelper.SetValue(Hive, RootKey, "keep", "1", RegistryValueKind.String, true);
        var ctx = new CleanupContext(
            new SafetyService(5),
            new WhitelistService(System.IO.Path.Combine(_qDir, "wl.json")),
            new QuarantineService(_qDir),
            dryRun: true);

        var handler = new RegCleanKeyHandler();
        handler.Run(Op(), ctx);

        Assert.Equal("1", RegistryHelper.GetValue(Hive, RootKey, "keep"));
    }

    [Theory]
    [InlineData("HKCU:\\Software\\X", "HKCU", "Software\\X")]
    [InlineData("HKCU:\\Software\\X\\Y", "HKCU", "Software\\X\\Y")]
    [InlineData("HKLM:/Software/X", "HKLM", "Software\\X")]
    public void ParseRegistryPath_Works(string input, string hive, string sub)
    {
        var parsed = RegCleanKeyHandler.ParseRegistryPath(input);
        Assert.Equal(hive, parsed.hive);
        Assert.Equal(sub, parsed.subKey);
    }
}