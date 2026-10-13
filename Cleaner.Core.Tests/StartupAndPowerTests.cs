using System;
using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Core.Tests;

public class StartupAndPowerTests
{
    [Fact]
    public void Quoted_Command_Gives_Path_Without_Arguments()
    {
        Assert.Equal(@"C:\Program Files\App\app.exe",
            StartupService.ExtractExecutablePath("\"C:\\Program Files\\App\\app.exe\" --silent"));
    }

    [Fact]
    public void Unquoted_Command_Is_Cut_After_Exe()
    {
        Assert.Equal(@"C:\Tools\tool.exe", StartupService.ExtractExecutablePath(@"C:\Tools\tool.exe /x /y"));
    }

    [Fact]
    public void Environment_Variables_Are_Expanded()
    {
        var expected = Environment.ExpandEnvironmentVariables(@"%SystemRoot%\system32\foo.exe");
        Assert.Equal(expected, StartupService.ExtractExecutablePath(@"%SystemRoot%\system32\foo.exe -a"));
    }

    [Fact]
    public void Empty_Command_Gives_Empty_Path()
    {
        Assert.Equal("", StartupService.ExtractExecutablePath(""));
    }

    [Theory]
    [InlineData(new byte[] { 2, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 6, 0, 0, 0 }, false)]
    [InlineData(new byte[] { 3, 0, 0, 0 }, true)]
    [InlineData(new byte[] { 7, 0, 0, 0 }, true)]
    public void Disabled_Marker_Is_Detected(byte[] data, bool expectedDisabled)
    {
        Assert.Equal(expectedDisabled, StartupService.IsDisabledMarker(data));
    }

    [Fact]
    public void Missing_Marker_Means_Enabled()
    {
        Assert.False(StartupService.IsDisabledMarker(null));
        Assert.False(StartupService.IsDisabledMarker(Array.Empty<byte>()));
    }

    [Theory]
    [InlineData(0L, StartupImpact.Unknown)]
    [InlineData(1_000_000L, StartupImpact.Low)]
    [InlineData(10_000_000L, StartupImpact.Medium)]
    [InlineData(200_000_000L, StartupImpact.High)]
    public void Impact_Estimate_Follows_File_Size(long size, StartupImpact expected)
    {
        Assert.Equal(expected, StartupService.EstimateImpact(size));
    }

    [Fact]
    public void Power_Plans_Are_Parsed_From_Powercfg_Output()
    {
        const string output =
            "Existing Power Schemes (* Active)\n" +
            "-----------------------------------\n" +
            "Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *\n" +
            "Power Scheme GUID: 8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c  (High performance)\n" +
            "Power Scheme GUID: 11111111-2222-3333-4444-555555555555  (My plan)\n";

        var plans = PowerPlanService.Parse(output);

        Assert.Equal(3, plans.Count);
        Assert.True(plans[0].IsActive);
        Assert.Equal(PowerPlanKind.Balanced, plans[0].Kind);
        Assert.False(plans[1].IsActive);
        Assert.Equal(PowerPlanKind.HighPerformance, plans[1].Kind);
        Assert.Equal(PowerPlanKind.Custom, plans[2].Kind);
        Assert.Equal("My plan", plans[2].Name);
    }
}
