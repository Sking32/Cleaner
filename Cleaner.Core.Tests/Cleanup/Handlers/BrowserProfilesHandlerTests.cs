using System;
using System.IO;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup.Handlers;

public class BrowserProfilesHandlerTests : IDisposable
{
    private readonly string _root;
    private readonly string _userData;
    private readonly string _profileDefault;

    public BrowserProfilesHandlerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CleanerBP_" + Guid.NewGuid().ToString("N"));
        _userData = Path.Combine(_root, "User Data");
        _profileDefault = Path.Combine(_userData, "Default");

        Directory.CreateDirectory(_profileDefault);
        File.WriteAllText(Path.Combine(_profileDefault, "Preferences"), "{}");

        var old = DateTime.UtcNow.AddMinutes(-30);
        Directory.SetCreationTimeUtc(_root, old);
        Directory.SetCreationTimeUtc(_userData, old);
        Directory.SetCreationTimeUtc(_profileDefault, old);
        File.SetCreationTimeUtc(Path.Combine(_profileDefault, "Preferences"), old);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private string MakeCacheFile(string profileDir, string subdir, string name, string content = "x")
    {
        var dir = Path.Combine(profileDir, subdir);
        Directory.CreateDirectory(dir);
        Directory.SetCreationTimeUtc(dir, DateTime.UtcNow.AddMinutes(-30));

        var p = Path.Combine(dir, name);
        File.WriteAllText(p, content);
        File.SetCreationTimeUtc(p, DateTime.UtcNow.AddMinutes(-30));
        return p;
    }

    private CleanupContext Ctx()
    {
        var q = Path.Combine(_root, "q");
        Directory.CreateDirectory(q);
        Directory.SetCreationTimeUtc(q, DateTime.UtcNow.AddMinutes(-30));
        return new CleanupContext(
            new SafetyService(freshMinutes: 5),
            new WhitelistService(Path.Combine(_root, "wl.json")),
            new QuarantineService(q));
    }

    private OperationEntry Op() => new()
    {
        Kind = "operation",
        Key = "chrome",
        Handler = "BrowserProfiles",
        Paths = new System.Collections.Generic.List<string> { _userData }
    };

    [Fact]
    public void Deletes_Cache_Files()
    {
        var cache = MakeCacheFile(_profileDefault, "Cache", "data_0");
        var gpu = MakeCacheFile(_profileDefault, "GPUCache", "data_1");

        var handler = new BrowserProfilesHandler();
        var result = handler.Run(Op(), Ctx());

        Assert.True(result.Success);
        Assert.False(File.Exists(cache));
        Assert.False(File.Exists(gpu));
    }

    [Fact]
    public void Keeps_Non_Cache_Files()
    {
        var bookmarks = Path.Combine(_profileDefault, "Bookmarks");
        File.WriteAllText(bookmarks, "data");
        File.SetCreationTimeUtc(bookmarks, DateTime.UtcNow.AddMinutes(-30));

        var handler = new BrowserProfilesHandler();
        handler.Run(Op(), Ctx());

        Assert.True(File.Exists(bookmarks));
    }

    [Fact]
    public void Finds_Multiple_Profiles()
    {
        var profile2 = Path.Combine(_userData, "Profile 1");
        Directory.CreateDirectory(profile2);
        Directory.SetCreationTimeUtc(profile2, DateTime.UtcNow.AddMinutes(-30));

        var prefs = Path.Combine(profile2, "Preferences");
        File.WriteAllText(prefs, "{}");
        File.SetCreationTimeUtc(prefs, DateTime.UtcNow.AddMinutes(-30));

        var c1 = MakeCacheFile(_profileDefault, "Cache", "a");
        var c2 = MakeCacheFile(profile2, "Cache", "b");

        var handler = new BrowserProfilesHandler();
        handler.Run(Op(), Ctx());

        Assert.False(File.Exists(c1));
        Assert.False(File.Exists(c2));
    }

    [Fact]
    public void Skips_Folders_Without_Preferences()
    {
        // Папка без Preferences — не профиль, не чистим.
        var crashpad = Path.Combine(_userData, "Crashpad");
        Directory.CreateDirectory(crashpad);
        Directory.SetCreationTimeUtc(crashpad, DateTime.UtcNow.AddMinutes(-30));

        var file = Path.Combine(crashpad, "report.txt");
        File.WriteAllText(file, "x");
        File.SetCreationTimeUtc(file, DateTime.UtcNow.AddMinutes(-30));

        var handler = new BrowserProfilesHandler();
        handler.Run(Op(), Ctx());

        Assert.True(File.Exists(file));
    }

    [Fact]
    public void Missing_UserData_Returns_Ok()
    {
        var op = new OperationEntry
        {
            Kind = "operation",
            Key = "chrome",
            Handler = "BrowserProfiles",
            Paths = new System.Collections.Generic.List<string> { Path.Combine(_root, "nope") }
        };

        var handler = new BrowserProfilesHandler();
        var r = handler.Run(op, Ctx());
        Assert.True(r.Success);
    }
}