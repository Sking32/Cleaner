using System;
using System.IO;
using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Core.Tests;

/// <summary>Проверки защиты от обхода запрещённых путей.</summary>
public class SafetyServiceHardeningTests : IDisposable
{
    private readonly string _tempRoot;
    private readonly SafetyService _safety = new(freshMinutes: 5);

    public SafetyServiceHardeningTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "CleanerHard_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
        Directory.SetCreationTimeUtc(_tempRoot, DateTime.UtcNow.AddMinutes(-30));
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempRoot, recursive: true); } catch { }
    }

    [Fact]
    public void DotDot_Cannot_Bypass_Forbidden_Path()
    {
        Assert.Equal(SafetyVerdict.Forbidden, _safety.Check(@"C:\Windows\System32\..").Verdict);
        Assert.Equal(SafetyVerdict.Forbidden, _safety.Check(@"C:\Users\..\Windows").Verdict);
    }

    [Fact]
    public void Forward_Slashes_Cannot_Bypass_Forbidden_Path()
    {
        Assert.Equal(SafetyVerdict.Forbidden, _safety.Check("C:/Windows").Verdict);
    }

    [Fact]
    public void Long_Path_Prefix_Cannot_Bypass_Forbidden_Path()
    {
        Assert.Equal(SafetyVerdict.Forbidden, _safety.Check(@"\\?\C:\Windows").Verdict);
    }

    [Fact]
    public void Bare_Drive_Is_Forbidden()
    {
        Assert.Equal(SafetyVerdict.Forbidden, _safety.Check("C:").Verdict);
    }

    [Fact]
    public void Missing_Path_Is_Still_Safe()
    {
        var missing = Path.Combine(_tempRoot, "no-such-file.tmp");
        Assert.Equal(SafetyVerdict.Safe, _safety.Check(missing).Verdict);
    }
}
