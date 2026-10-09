using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup.Handlers;

public class CommandHandlerTests
{
    [Fact]
    public void SplitCommand_Parses_Exe_With_Args()
    {
        var (exe, args) = CommandHandler.SplitCommand("ipconfig /flushdns");
        Assert.Equal("ipconfig", exe);
        Assert.Equal("/flushdns", args);
    }

    [Fact]
    public void SplitCommand_Parses_Exe_Only()
    {
        var (exe, args) = CommandHandler.SplitCommand("cmd.exe");
        Assert.Equal("cmd.exe", exe);
        Assert.Equal("", args);
    }

    [Fact]
    public void SplitCommand_Wraps_PowerShell_Cmdlet()
    {
        var (exe, args) = CommandHandler.SplitCommand("Clear-RecycleBin");
        Assert.Equal("powershell.exe", exe);
        Assert.Contains("Clear-RecycleBin", args);
    }

    [Fact]
    public void SplitCommand_Wraps_Delete_Cmdlet()
    {
        var (exe, _) = CommandHandler.SplitCommand("Delete-DeliveryOptimizationCache");
        Assert.Equal("powershell.exe", exe);
    }

    [Fact]
    public void SplitCommand_Explicit_PowerShell()
    {
        var (exe, _) = CommandHandler.SplitCommand("powershell.exe -Command Get-Date");
        Assert.Equal("powershell.exe", exe);
    }

    [Fact]
    public void Run_Empty_Command_Returns_Ok()
    {
        var ctx = Ctx();
        var op = new OperationEntry { Kind = "operation", Key = "test", Handler = "Command" };

        var handler = new CommandHandler();
        var result = handler.Run(op, ctx);

        Assert.True(result.Success);
    }

    [Fact]
    public void Run_DryRun_Does_Not_Execute()
    {
        var ctx = Ctx(dryRun: true);
        var op = new OperationEntry
        {
            Kind = "operation",
            Key = "test",
            Handler = "Command",
            Command = "cmd.exe /c exit 1"
        };

        var handler = new CommandHandler();
        var result = handler.Run(op, ctx);

        Assert.True(result.Success);
        Assert.Equal("dry-run", result.Message);
    }

    [Fact]
    public void Run_Executes_Cmd()
    {
        var ctx = Ctx();
        var op = new OperationEntry
        {
            Kind = "operation",
            Key = "test",
            Handler = "Command",
            Command = "cmd.exe /c echo hi"
        };

        var handler = new CommandHandler();
        var result = handler.Run(op, ctx);

        Assert.True(result.Success, result.Message);
    }

    [Fact]
    public void Run_Fails_On_NonZero_Exit()
    {
        var ctx = Ctx();
        var op = new OperationEntry
        {
            Kind = "operation",
            Key = "test",
            Handler = "Command",
            Command = "cmd.exe /c exit 3"
        };

        var handler = new CommandHandler();
        var result = handler.Run(op, ctx);

        Assert.False(result.Success);
    }

    // ------------------------------------------------------------------

    private static CleanupContext Ctx(bool dryRun = false)
    {
        var tmp = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "CleanerCH_" + System.Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(tmp);

        return new CleanupContext(
            new SafetyService(freshMinutes: 5),
            new WhitelistService(System.IO.Path.Combine(tmp, "wl.json")),
            new QuarantineService(System.IO.Path.Combine(tmp, "q")),
            dryRun: dryRun);
    }
}