using Cleaner.Core.Services;
using Microsoft.Win32;

namespace Cleaner.Core.Tests;

public sealed class AutomationPolicyTests
{
    [Fact]
    public void ScheduleWaitsForIntervalAndIdleStateAndNeverCatchesUpWithMultipleRuns()
    {
        var schedule = new InAppCleanupSchedule(); var now = DateTimeOffset.UtcNow;
        Assert.False(schedule.TryBegin(now, true, 60, false));
        Assert.False(schedule.TryBegin(now.AddMinutes(59), true, 60, false));
        Assert.False(schedule.TryBegin(now.AddHours(1), true, 60, true));
        Assert.True(schedule.TryBegin(now.AddDays(3), true, 60, false));
        Assert.False(schedule.TryBegin(now.AddDays(3), true, 60, false));
        Assert.False(schedule.TryBegin(now.AddDays(3), false, 60, false)); Assert.Null(schedule.NextRun);
        Assert.False(schedule.TryBegin(now.AddDays(3), true, 60, false));
        Assert.False(schedule.TryBegin(now.AddDays(3).AddMinutes(1), true, 360, false));
        Assert.Equal(now.AddDays(3).AddMinutes(361), schedule.NextRun);
    }

    [Fact]
    public void InvalidScheduleIsDisabled()
    {
        var schedule = new InAppCleanupSchedule();
        Assert.False(schedule.TryBegin(DateTimeOffset.UtcNow, true, -1, false)); Assert.Null(schedule.NextRun);
    }

    [Fact]
    public void StartupTouchesOnlyCurrentUserCleanerValueAndQuotesPath()
    {
        var registry = new MemoryRegistry();
        registry.SetValue("HKCU", StartupRegistration.Key, "OtherApp", "other.exe", RegistryValueKind.String, true);
        var registration = new StartupRegistration(registry, @"C:\Fixture Folder\Cleaner.App.exe");
        Assert.False(registration.IsEnabled);
        registration.SetEnabled(true); Assert.True(registration.IsEnabled);
        Assert.Equal("\"C:\\Fixture Folder\\Cleaner.App.exe\" --startup", registry.GetValue("HKCU", StartupRegistration.Key, StartupRegistration.ValueName));
        Assert.False(registry.KeyExists("HKLM", StartupRegistration.Key));
        registration.SetEnabled(false); Assert.False(registration.IsEnabled);
        Assert.Equal("other.exe", registry.GetValue("HKCU", StartupRegistration.Key, "OtherApp"));
    }

    [Theory]
    [InlineData("dotnet.exe")]
    [InlineData("Cleaner.App.exe")]
    [InlineData("C:\\fixture\\evil.exe")]
    [InlineData("C:\\fixture\\bad\"\\Cleaner.App.exe")]
    public void InvalidStartupCommandsAreRejected(string path)
    {
        var registry = new MemoryRegistry(); var registration = new StartupRegistration(registry, path);
        Assert.Throws<InvalidOperationException>(() => registration.SetEnabled(true));
        Assert.False(registry.KeyExists("HKCU", StartupRegistration.Key));
    }
}
