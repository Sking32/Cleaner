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
        var context = new CleanupContext(safety, whitelist, quarantine, progress, token,
            fileMode: CleanupFileMode.Quarantine, minimumFileAge: TimeSpan.FromHours(24));
        var watch = Stopwatch.StartNew();
        var result = new PathCleanupHandler().Run(new OperationEntry { Key = "temp_user", Handler = "PathCleanup",
            Paths = new() { tempDirectory }, Kind = "operation" }, context);
        return new CleanupSessionResult
        {
            BytesQuarantined = result.BytesQuarantined, FilesQuarantined = result.FilesQuarantined,
            FilesSkipped = result.FilesSkipped, OperationsRun = 1, OperationsFailed = result.Success ? 0 : 1,
            Duration = watch.Elapsed, Canceled = token.IsCancellationRequested, SkipDetails = context.SkipDetails,
            OmittedSkipDetails = context.OmittedSkipDetails,
            Outcomes = new[] { new OperationOutcome { Key = "temp_user", Handler = "PathCleanup", Success = result.Success,
                Canceled = token.IsCancellationRequested, BytesQuarantined = result.BytesQuarantined,
                FilesQuarantined = result.FilesQuarantined, FilesSkipped = result.FilesSkipped,
                Errors = result.Errors, Duration = watch.Elapsed } }
        };
    }
}
