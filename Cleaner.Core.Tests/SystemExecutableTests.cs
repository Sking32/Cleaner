using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup.Handlers;

namespace Cleaner.Core.Tests;

public sealed class SystemExecutableTests
{
    [Theory]
    [InlineData("cmd")]
    [InlineData("powercfg")]
    [InlineData("sc")]
    [InlineData("wsreset")]
    [InlineData("cleanmgr")]
    [InlineData("dism")]
    [InlineData("vssadmin")]
    [InlineData("wevtutil")]
    [InlineData("netsh")]
    [InlineData("schtasks")]
    [InlineData("shutdown")]
    [InlineData("ipconfig")]
    public void SystemUtilitiesResolveToTheSystemDirectory(string name)
    {
        var expected = Path.Combine(Environment.SystemDirectory, name + ".exe");
        Assert.Equal(expected, SystemExecutable.Resolve(name));
        Assert.Equal(expected, SystemExecutable.Resolve(name + ".exe"));
        Assert.Equal(expected, SystemExecutable.Resolve(name.ToUpperInvariant() + ".EXE"));
        Assert.True(Path.IsPathFullyQualified(expected));
    }

    [Fact]
    public void PowerShellResolvesToTheWindowsPowerShellDirectory()
    {
        var expected = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        Assert.Equal(expected, SystemExecutable.Resolve("powershell"));
        Assert.Equal(expected, SystemExecutable.Resolve("PowerShell.EXE"));
    }

    [Fact]
    public void ExplorerResolvesToTheWindowsDirectory()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
        Assert.Equal(expected, SystemExecutable.Resolve("explorer"));
        Assert.Equal(expected, SystemExecutable.Resolve("EXPLORER.EXE"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown.exe")]
    [InlineData("pwsh.exe")]
    [InlineData(".\\powercfg.exe")]
    [InlineData("subdir/powercfg.exe")]
    [InlineData("C:powercfg.exe")]
    [InlineData(@"C:\Windows\System32\powercfg.exe")]
    [InlineData(@"\\server\share\powercfg.exe")]
    [InlineData(@"\\?\C:\Windows\System32\powercfg.exe")]
    [InlineData(@"%WINDIR%\System32\powercfg.exe")]
    [InlineData("\"powercfg.exe\"")]
    [InlineData("powercfg.exe ")]
    [InlineData("powercfg.exe.")]
    [InlineData("powercfg.exe:payload")]
    [InlineData("powercfg.exe /list")]
    [InlineData("powercfg.exe.exe")]
    public void QualifiedUnknownAndAmbiguousNamesAreRejected(string? name)
        => Assert.Throws<ArgumentException>(() => SystemExecutable.Resolve(name!));

    [Fact]
    public void BundledCatalogExecutablesHaveTrustedLocations()
    {
        var actions = new TweakRepository().Tweaks.SelectMany(t => t.Apply.Concat(t.Unapply).Concat(t.State));
        var executables = actions.Where(a => a.FileName != null).Select(a => a.FileName!)
            .Concat(new OperationsRepository().Operations.Where(o => !string.IsNullOrWhiteSpace(o.Command))
                .Select(o => CommandHandler.SplitCommand(o.Command!).fileName));
        foreach (var executable in executables)
            Assert.True(Path.IsPathFullyQualified(SystemExecutable.Resolve(executable)), executable);
    }
}
