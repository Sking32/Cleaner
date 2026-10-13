using Cleaner.Core.Data;
using Cleaner.Core.Services.Cleanup.Handlers;
using System.Diagnostics;

namespace Cleaner.Core.Services.Cleanup;

/// <summary>A fixed scope independent of manual selections and editable catalog presets.</summary>
public static class AutomaticCleanup
{
    public static CleanupSessionResult Run(string tempDirectory, ISafetyService safety, IWhitelistService whitelist,
        IQuarantineService quarantine, CancellationToken token = default, IProgress<CleanupProgress>? progress = null)
    {
        if (!Path.IsPathFullyQualified(tempDirectory) || Path.GetPathRoot(tempDirectory) == tempDirectory)
            throw new ArgumentException("A dedicated temporary directory is required.", nameof(tempDirectory));
        var scanContext = new CleanupContext(safety, whitelist, quarantine, progress, token, dryRun: true,
            fileMode: CleanupFileMode.Quarantine, minimumFileAge: TimeSpan.FromHours(24), capturePlan: true);
        var watch = Stopwatch.StartNew();
        scanContext.ApproveRoots("temp_user", new[] { tempDirectory });
        var scan = new PathCleanupHandler().Run(new OperationEntry { Key = "temp_user", Handler = "PathCleanup",
            Paths = new() { tempDirectory }, Kind = "operation" }, scanContext);
        if (!scan.Success || token.IsCancellationRequested) return new CleanupSessionResult { Canceled = token.IsCancellationRequested,
            OperationsRun = 1, OperationsFailed = scan.Success ? 0 : 1, FilesSkipped = scan.FilesSkipped, SkipDetails = scanContext.SkipDetails };
        var plan = new CleanupPlan(scanContext.PlannedFiles, new[] { "temp_user" }, CleanupFileMode.Quarantine, true);
        var context = new CleanupContext(safety, whitelist, quarantine, progress, token, plan: plan, minimumFileAge: TimeSpan.FromHours(24));
        var result = PlannedCleanup.Run(plan, context, null);
        return new CleanupSessionResult
        {
            BytesQuarantined = result.BytesQuarantined, FilesQuarantined = result.FilesQuarantined,
            FilesSkipped = scan.FilesSkipped + result.FilesSkipped, OperationsRun = 1, OperationsFailed = result.OperationsFailed,
            Duration = watch.Elapsed, Canceled = token.IsCancellationRequested, SkipDetails = scanContext.SkipDetails.Concat(context.SkipDetails).Take(250).ToArray(),
            OmittedSkipDetails = scanContext.OmittedSkipDetails + context.OmittedSkipDetails,
            Outcomes = new[] { new OperationOutcome { Key = "temp_user", Handler = "PathCleanup", Success = result.OperationsFailed == 0,
                Canceled = token.IsCancellationRequested, BytesQuarantined = result.BytesQuarantined,
                FilesQuarantined = result.FilesQuarantined, FilesSkipped = scan.FilesSkipped + result.FilesSkipped,
                Errors = result.Outcomes.SelectMany(o => o.Errors).ToArray(), Duration = watch.Elapsed } }
        };
    }
}
