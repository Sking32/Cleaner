using System;
using System.IO;
using Cleaner.Core.Services;
using Xunit;

namespace Cleaner.Core.Tests;

public class QuarantinePruneTests : IDisposable
{
    private readonly string _root;

    public QuarantinePruneTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CleanerPrune_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void Empty_Sessions_Are_Removed_And_Used_Sessions_Are_Kept()
    {
        var emptyDir = Path.Combine(_root, "empty");
        var usedDir = Path.Combine(_root, "used");
        Directory.CreateDirectory(emptyDir);
        Directory.CreateDirectory(usedDir);

        _ = new QuarantineService(emptyDir);
        var used = new QuarantineService(usedDir);

        var source = Path.Combine(_root, "source.txt");
        File.WriteAllText(source, "data");
        Assert.NotNull(used.MoveToQuarantine(source, "test"));

        var removed = QuarantineService.PruneEmptySessions(_root);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(emptyDir));
        Assert.True(Directory.Exists(usedDir));
    }
}
