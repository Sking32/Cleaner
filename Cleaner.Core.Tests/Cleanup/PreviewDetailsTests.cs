using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;
using Cleaner.Core.Services.Cleanup.Handlers;

namespace Cleaner.Core.Tests.Cleanup;

public sealed class PreviewDetailsTests
{
    [Fact]
    public void DryRunSamplesOnlyEligibleFixtureFilesWithoutMovingAnything()
    {
        var root = Path.Combine(Path.GetTempPath(), "PreviewDetails_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root); var files = Path.Combine(root, "files"); Directory.CreateDirectory(files);
        var whitelist = new WhitelistService(Path.Combine(root, "whitelist.json"));
        var quarantine = new QuarantineService(Path.Combine(root, "q"));
        var old = DateTime.UtcNow.AddHours(-1); Directory.SetCreationTimeUtc(files, old); Directory.SetCreationTimeUtc(root, old);
        for (var i = 0; i < 10; i++) { var file = Path.Combine(files, i + ".tmp"); File.WriteAllText(file, "1234"); File.SetCreationTimeUtc(file, old); }
        whitelist.Add(Path.Combine(files, "0.tmp"));
        var context = new CleanupContext(new SafetyService(), whitelist, quarantine, dryRun: true);
        var outcome = new PathCleanupHandler().Run(new OperationEntry { Kind = "operation", Key = "fixture", Handler = "PathCleanup", Paths = new() { files } }, context);
        Assert.Equal(36, outcome.PreviewBytes); Assert.Equal(9, context.PreviewFileCounts["fixture"]);
        Assert.Equal(5, context.PreviewFiles.Count); Assert.Equal(4, context.OmittedPreviewFiles);
        Assert.DoesNotContain(context.PreviewFiles, f => f.Path.EndsWith("0.tmp"));
        Assert.Equal("whitelist", Assert.Single(context.SkipDetails).Reason);
        Assert.Equal(10, Directory.GetFiles(files).Length); Assert.Equal(0, quarantine.Count); Assert.Equal(0, outcome.FilesDeleted);
    }

    [Fact]
    public void PreviewSamplingIsBoundedAcrossOperationsAndAbsentDuringApply()
    {
        var root = Path.Combine(Path.GetTempPath(), "PreviewBound_" + Guid.NewGuid().ToString("N"));
        var q = new QuarantineService(Path.Combine(root, "q")); var whitelist = new WhitelistService(Path.Combine(root, "whitelist.json"));
        var context = new CleanupContext(new SafetyService(), whitelist, q, dryRun: true);
        for (var op = 0; op < 60; op++) for (var file = 0; file < 10; file++) context.RecordPreviewFile("op" + op, "fixture-path", 1);
        Assert.Equal(250, context.PreviewFiles.Count); Assert.Equal(350, context.OmittedPreviewFiles);
        var apply = new CleanupContext(new SafetyService(), whitelist, q);
        apply.RecordPreviewFile("op", "fixture-path", 1); Assert.Empty(apply.PreviewFiles); Assert.Empty(apply.PreviewFileCounts);
    }
}
