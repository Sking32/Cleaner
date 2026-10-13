using System.Collections.ObjectModel;
using System.Diagnostics;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup;

public sealed record PlannedFile(string OperationKey, string Path, FileIdentity Identity, string? AllowedRoot = null);

/// <summary>Complete, immutable and session-local. Never reconstruct a plan from an exported report.</summary>
public sealed class CleanupPlan
{
    public IReadOnlyList<PlannedFile> Files { get; }
    public IReadOnlyList<string> OperationKeys { get; }
    public CleanupFileMode Mode { get; }
    public bool CanApply { get; }
    internal IReadOnlyList<PlannedAction> Actions { get; }
    internal static bool ContainsPath(string root, string path) => path.Equals(root, StringComparison.OrdinalIgnoreCase) ||
        path.StartsWith(root.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    internal CleanupPlan(IEnumerable<PlannedFile> files, IEnumerable<string> keys, CleanupFileMode mode, bool canApply, IEnumerable<PlannedAction>? actions = null)
    {
        Files = Array.AsReadOnly(files.ToArray()); OperationKeys = Array.AsReadOnly(keys.ToArray()); Mode = mode; CanApply = canApply;
        Actions = Array.AsReadOnly(actions?.ToArray() ?? Array.Empty<PlannedAction>());
    }
}

internal static class PlannedCleanup
{
    internal static CleanupSessionResult Run(CleanupPlan plan, CleanupContext context, OperationsRepository? operations, ICleanupService? service = null)
    {
        if (!plan.CanApply || plan.Mode != context.FileMode) throw new InvalidOperationException("Analysis must complete successfully before cleanup.");
        var watch = Stopwatch.StartNew();
        var outcomes = new List<OperationOutcome>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in plan.OperationKeys)
        {
            if (context.IsCancellationRequested) break;
            long bytes = 0; int completed = 0, skipped = 0;
            var errors = new List<string>();
            var operation = operations?.GetByKey(key);
            var blocked = operation?.Enabled == false || operation?.ExpertOnly == true && !context.ExpertMode;
            var action = plan.Actions.FirstOrDefault(a => a.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (action != null)
            {
                CleanupOperationResult result;
                if (blocked || operation == null) result = CleanupOperationResult.Fail("compatibility.expert_required");
                else if (!action.Matches(operation, context.Registry)) result = CleanupOperationResult.Fail("cleanup.plan.action_changed");
                else
                {
                    try { result = service?.RunOperation(operation, context) ?? CleanupOperationResult.Fail("System action dispatcher is unavailable."); }
                    catch (OperationCanceledException) when (context.IsCancellationRequested) { break; }
                }
                outcomes.Add(new OperationOutcome { Key = key, Handler = operation?.Handler ?? "", FileMode = context.FileMode,
                    Success = result.Success, Errors = result.Errors, Message = result.Message, FilesSkipped = result.FilesSkipped });
                if (!result.Success) break;
                continue;
            }
            foreach (var candidate in plan.Files.Where(f => f.OperationKey.Equals(key, StringComparison.OrdinalIgnoreCase)))
            {
                if (context.IsCancellationRequested) break;
                var reason = blocked ? "rule_disabled" : context.Activity.IsBusy(key) ? "application_running" : null;
                if (!seen.Add(candidate.Path)) reason = "duplicate";
                if (candidate.AllowedRoot == null || !CleanupPlan.ContainsPath(candidate.AllowedRoot, candidate.Path)) reason = "unverifiable";
                if (!context.CheckSafety(candidate.Path, key).IsSafe || context.Whitelist.IsWhitelisted(candidate.Path)) reason = "safety";
                if (reason != null) { context.RecordSkip(key, candidate.Path, reason); skipped++; continue; }
                try
                {
                    if (context.FileMode == CleanupFileMode.Quarantine)
                    {
                        var move = context.Quarantine.MovePlanned(candidate, key);
                        if (move.Entry == null)
                        {
                            var failure = move.Reason ?? "quarantine_failed";
                            context.RecordSkip(key, candidate.Path, failure, move.Detail); skipped++;
                            if (!FileFailure.IsExpectedSkip(failure)) { errors.Add(move.Detail ?? failure); break; }
                            continue;
                        }
                        if (move.Entry.Pending) { bytes += candidate.Identity.Length; completed++; errors.Add("Quarantine journal needs recovery."); break; }
                    }
                    else
                    {
                        using var handle = context.FileVerifier.Open(candidate.Path, true, candidate.Identity);
                        context.CancellationToken.ThrowIfCancellationRequested();
                        handle.Delete();
                    }
                    bytes += candidate.Identity.Length; completed++;
                    context.Report(key, operation?.Handler ?? "PathCleanup", candidate.Path, completed + skipped, bytes, "applied");
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    context.RecordSkip(key, candidate.Path, FileFailure.Classify(ex), ex.Message); skipped++;
                    if (!FileFailure.IsExpectedSkip(FileFailure.Classify(ex))) { errors.Add(ex.Message); break; }
                }
            }
            outcomes.Add(new OperationOutcome { Key = key, Handler = operation?.Handler ?? "PathCleanup", FileMode = context.FileMode,
                Success = errors.Count == 0, Errors = errors, FilesSkipped = skipped,
                FilesDeleted = context.FileMode == CleanupFileMode.PermanentDelete ? completed : 0,
                FilesQuarantined = context.FileMode == CleanupFileMode.Quarantine ? completed : 0,
                BytesFreed = context.FileMode == CleanupFileMode.PermanentDelete ? bytes : 0,
                BytesQuarantined = context.FileMode == CleanupFileMode.Quarantine ? bytes : 0 });
            if (errors.Count != 0) break; // Stop the whole plan if recovery evidence could not be persisted.
        }
        if (context.FileMode == CleanupFileMode.Quarantine && outcomes.All(o => o.Success))
        {
            try { context.Quarantine.Checkpoint(); }
            catch (Exception ex) { outcomes.Add(new OperationOutcome { Key = "quarantine_journal", FileMode = context.FileMode,
                Success = false, Errors = new[] { ex.Message } }); }
        }
        return new CleanupSessionResult { Outcomes = outcomes, OperationsRun = outcomes.Count, OperationsFailed = outcomes.Count(o => !o.Success),
            FilesDeleted = outcomes.Sum(o => o.FilesDeleted), FilesQuarantined = outcomes.Sum(o => o.FilesQuarantined), FilesSkipped = outcomes.Sum(o => o.FilesSkipped),
            BytesFreed = outcomes.Sum(o => o.BytesFreed), BytesQuarantined = outcomes.Sum(o => o.BytesQuarantined),
            SkipDetails = context.SkipDetails, OmittedSkipDetails = context.OmittedSkipDetails,
            Canceled = context.IsCancellationRequested, Duration = watch.Elapsed };
    }
}
