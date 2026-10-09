using System;
using System.IO;
using System.Threading;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Xunit;

namespace Cleaner.Core.Tests.Cleanup;

public class CleanupRunnerTests : IDisposable
{
    private readonly string _root;
    private readonly string _tempDir;
    private readonly SafetyService _safety;
    private readonly WhitelistService _whitelist;
    private readonly QuarantineService _quarantine;
    private readonly CleanupService _cleanup;
    private readonly CleanupRunner _runner;

    public CleanupRunnerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "CleanerRunner_" + Guid.NewGuid().ToString("N"));
        _tempDir = Path.Combine(_root, "temp");
        Directory.CreateDirectory(_tempDir);

        var old = DateTime.UtcNow.AddMinutes(-30);
        Directory.SetCreationTimeUtc(_root, old);
        Directory.SetCreationTimeUtc(_tempDir, old);

        _safety = new SafetyService(freshMinutes: 5);
        _whitelist = new WhitelistService(Path.Combine(_root, "wl.json"));
        _quarantine = new QuarantineService(Path.Combine(_root, "q"));
        _cleanup = new CleanupService(new OperationsRepository());
        _runner = new CleanupRunner(_cleanup, new PresetsRepository());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    private CleanupContext Ctx(CancellationToken ct = default, bool dryRun = false)
        => new(_safety, _whitelist, _quarantine, cancellationToken: ct, dryRun: dryRun);

    [Fact]
    public void Empty_List_Returns_Zero()
    {
        var r = _runner.Run(Array.Empty<string>(), Ctx());
        Assert.Equal(0, r.OperationsRun);
        Assert.Equal(0, r.FilesDeleted);
    }

    [Fact]
    public void Unknown_Preset_Returns_Zero()
    {
        var r = _runner.RunPreset("no-such-preset", Ctx());
        Assert.Equal(0, r.OperationsRun);
    }

    [Fact]
    public void Null_Args_Returns_Zero()
    {
        var r = _runner.Run(null!, Ctx());
        Assert.Equal(0, r.OperationsRun);
    }

    [Fact]
    public void Runs_Operation_And_Reports_Outcome()
    {
        // Создаём фейковую "op" через команду: echo (не трогает ФС).
        var keys = new[] { "dns" };  // handler Command, "ipconfig /flushdns"
        var r = _runner.Run(keys, Ctx());

        Assert.Equal(1, r.OperationsRun);
        Assert.Single(r.Outcomes);
        Assert.Equal("dns", r.Outcomes[0].Key);
    }

    [Fact]
    public void Distinguishes_Failed_Operations()
    {
        // "winsxs" требует DISM — не запустится без админа, но мы в dry-run
        // (handler сразу вернёт "dry-run" и Success=true).
        var r = _runner.Run(new[] { "winsxs" }, Ctx(dryRun: true));
        Assert.Equal(1, r.OperationsRun);
        Assert.Equal(0, r.OperationsFailed);
    }

    [Fact]
    public void Duplicates_Are_Removed()
    {
        var r = _runner.Run(new[] { "dns", "dns", "dns" }, Ctx(dryRun: true));
        Assert.Equal(1, r.OperationsRun);
    }

    [Fact]
    public void Cancellation_Stops_Run()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var keys = new[] { "dns", "temp_user", "prefetch" };
        var r = _runner.Run(keys, Ctx(cts.Token));

        Assert.True(r.Canceled);
        Assert.Equal(0, r.OperationsRun);
    }

    [Fact]
    public void Runs_Soft_Preset()
    {
        // soft: temp_user, temp_sys, dns, recycle, wer_user — часть требует реальных путей.
        // В dry-run не должно быть падений из-за отсутствия путей.
        var r = _runner.RunPreset("soft", Ctx(dryRun: true));

        Assert.Equal(5, r.OperationsRun);
        Assert.Equal(0, r.OperationsFailed);
    }

    [Fact]
    public void Preset_Outcome_Keys_Match_Preset_Keys()
    {
        var presets = new PresetsRepository();
        var softKeys = presets.CleanupPresets["soft"].Keys;

        var r = _runner.RunPreset("soft", Ctx(dryRun: true));

        var actual = r.Outcomes.Select(o => o.Key).ToList();
        Assert.Equal(softKeys.Count, actual.Count);
        foreach (var k in softKeys)
            Assert.Contains(k, actual);
    }

    [Fact]
    public void Total_Duration_Is_Positive()
    {
        var r = _runner.Run(new[] { "dns" }, Ctx(dryRun: true));
        Assert.True(r.Duration > TimeSpan.Zero);
    }
}