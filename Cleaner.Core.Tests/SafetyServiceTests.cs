using System;
using System.IO;
using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Core.Tests;

public class SafetyServiceTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly SafetyService _safety;

    public SafetyServiceTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "CleanerTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);

        // Сдвигаем время создания назад, чтобы папка не попадала под "TooFresh"
        Directory.SetCreationTimeUtc(_tempRoot, DateTime.UtcNow.AddMinutes(-30));

        _safety = new SafetyService(freshMinutes: 5);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void Forbids_C_Root()
    {
        var r = _safety.Check(@"C:\");
        Assert.Equal(SafetyVerdict.Forbidden, r.Verdict);
    }

    [Fact]
    public void Forbids_Windows_Directory()
    {
        var r = _safety.Check(@"C:\Windows");
        Assert.Equal(SafetyVerdict.Forbidden, r.Verdict);
        Assert.Contains("forbidden", r.Reason);
    }

    [Fact]
    public void Forbids_UserProfile_Root()
    {
        var profile = Environment.GetEnvironmentVariable("USERPROFILE")!;
        var r = _safety.Check(profile);
        Assert.Equal(SafetyVerdict.Forbidden, r.Verdict);
    }

    [Fact]
    public void Forbids_Documents_Folder()
    {
        var profile = Environment.GetEnvironmentVariable("USERPROFILE")!;
        var docs = Path.Combine(profile, "Documents");
        var r = _safety.Check(docs);
        Assert.Equal(SafetyVerdict.Forbidden, r.Verdict);
    }

    [Fact]
    public void Allows_Subfolder_Of_Forbidden_Path()
    {
        var r = _safety.Check(_tempRoot);
        Assert.Equal(SafetyVerdict.Safe, r.Verdict);
    }

    [Fact]
    public void Detects_TooFresh_File()
    {
        var file = Path.Combine(_tempRoot, "fresh.txt");
        File.WriteAllText(file, "x");
        var r = _safety.Check(file);
        Assert.Equal(SafetyVerdict.TooFresh, r.Verdict);
    }

    [Fact]
    public void Allows_Old_File()
    {
        var file = Path.Combine(_tempRoot, "old.txt");
        File.WriteAllText(file, "x");
        File.SetCreationTimeUtc(file, DateTime.UtcNow.AddMinutes(-30));
        var r = _safety.Check(file);
        Assert.Equal(SafetyVerdict.Safe, r.Verdict);
    }

    [Fact]
    public void Empty_Path_Is_Forbidden()
    {
        Assert.Equal(SafetyVerdict.Forbidden, _safety.Check("").Verdict);
        Assert.Equal(SafetyVerdict.Forbidden, _safety.Check("   ").Verdict);
    }

    [Fact]
    public void Case_Insensitive_Path_Match()
    {
        Assert.Equal(SafetyVerdict.Forbidden, _safety.Check(@"C:\WINDOWS").Verdict);
        Assert.Equal(SafetyVerdict.Forbidden, _safety.Check(@"c:\windows").Verdict);
    }

    [Fact]
    public void Trailing_Slash_Ignored()
    {
        var r = _safety.Check(@"C:\Windows\");
        Assert.Equal(SafetyVerdict.Forbidden, r.Verdict);
    }

    [Fact]
    public void IsSafe_Property_Works()
    {
        Assert.True(_safety.Check(_tempRoot).IsSafe);
        Assert.False(_safety.Check(@"C:\Windows").IsSafe);
    }

    [Fact]
    public void Extra_Forbidden_Paths_Are_Honored()
    {
        var custom = new SafetyService(5, new[] { _tempRoot });
        var r = custom.Check(_tempRoot);
        Assert.Equal(SafetyVerdict.Forbidden, r.Verdict);
    }
}