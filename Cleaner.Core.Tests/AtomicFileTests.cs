using Cleaner.Core.Services;

namespace Cleaner.Core.Tests;

public sealed class AtomicFileTests
{
    private static string Fixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "AtomicFixture_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var path = Path.Combine(root, "journal.json"); File.WriteAllText(path, "original"); return path;
    }
    private static IOException Win32(int code) => new("Simulated Win32 failure", unchecked((int)0x80070000) | code);

    [Theory]
    [InlineData(32)][InlineData(33)][InlineData(1175)]
    public void TransientReplacementFailureRetriesTheSameFlushedContent(int code)
    {
        var path = Fixture(); var calls = 0; var delays = new List<int>();
        AtomicFile.Write(path, "replacement", (source, target) =>
        {
            Assert.Equal("replacement", File.ReadAllText(source));
            if (++calls < 3) throw Win32(code);
            File.Replace(source, target, null);
        }, delays.Add);
        Assert.Equal(3, calls); Assert.Equal(new[] { 40, 80 }, delays); Assert.Equal("replacement", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Theory]
    [InlineData(1176)][InlineData(1177)]
    public void PartialReplacementPreservesRecoveryContentWithoutRetry(int code)
    {
        var path = Fixture(); var calls = 0;
        var ex = Assert.Throws<IOException>(() => AtomicFile.Write(path, "recoverable", (source, target) =>
        {
            calls++; File.Delete(target); throw Win32(code);
        }, _ => throw new InvalidOperationException("Partial replacement must not be retried.")));
        var recovery = Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, "*.tmp"));
        Assert.Equal("recoverable", File.ReadAllText(recovery)); Assert.Contains(recovery, ex.Message); Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(5, 1)][InlineData(32, 5)]
    public void PermanentFailureKeepsOriginalAndStopsWithinBoundedAttempts(int code, int expectedCalls)
    {
        var path = Fixture(); var calls = 0;
        Assert.Throws<IOException>(() => AtomicFile.Write(path, "replacement", (_, _) => { calls++; throw Win32(code); }, _ => { }));
        Assert.Equal(expectedCalls, calls); Assert.Equal("original", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!));
    }

    [Fact]
    public async Task RealTemporarySharingLockDoesNotLoseTheJournal()
    {
        var path = Fixture(); var locked = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var attempted = new ManualResetEventSlim();
        var release = Task.Run(() => { if (!attempted.Wait(TimeSpan.FromSeconds(5))) throw new TimeoutException(); Thread.Sleep(100); locked.Dispose(); });
        try
        {
            await Task.Run(() => AtomicFile.Write(path, "after-lock", (source, target) => { attempted.Set(); File.Replace(source, target, null); }, Thread.Sleep));
            await release; Assert.Equal("after-lock", File.ReadAllText(path));
        }
        finally { locked.Dispose(); }
    }
}
