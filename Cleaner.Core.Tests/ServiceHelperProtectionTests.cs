using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Core.Tests;

public class ServiceHelperProtectionTests
{
    [Theory]
    [InlineData("RpcSs")]
    [InlineData("rpcss")]
    [InlineData("DcomLaunch")]
    [InlineData("Winmgmt")]
    [InlineData("  EventLog ")]
    public void Critical_Services_Are_Protected(string name)
    {
        Assert.True(ServiceHelper.IsProtected(name));
    }

    [Theory]
    [InlineData("wuauserv")]
    [InlineData("SysMain")]
    [InlineData("DiagTrack")]
    [InlineData("")]
    [InlineData("   ")]
    public void Ordinary_Or_Empty_Names_Are_Not_Protected(string name)
    {
        Assert.False(ServiceHelper.IsProtected(name));
    }

    [Fact]
    public void Stop_Refuses_Protected_Service()
    {
        Assert.False(ServiceHelper.Stop("RpcSs"));
    }

    [Fact]
    public void Disabling_Protected_Service_Is_Refused()
    {
        Assert.False(ServiceHelper.SetStartType("RpcSs", "Disabled"));
    }
}
