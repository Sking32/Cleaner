using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup;

public interface ICleanupRunner
{
    /// <summary>Прогнать указанные операции.</summary>
    CleanupSessionResult Run(
        IEnumerable<string> operationKeys,
        CleanupContext ctx,
        CancellationToken cancellationToken = default);

    /// <summary>Прогнать cleanup-пресет soft/std/deep/custom.</summary>
    CleanupSessionResult RunPreset(
        string presetName,
        CleanupContext ctx,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Прогоняет набор cleanup-операций через <see cref="ICleanupService"/>,
/// агрегируя результат и сообщая о прогрессе.
/// </summary>
public sealed class CleanupRunner : ICleanupRunner
{
    private readonly ICleanupService _cleanup;
    private readonly PresetsRepository _presets;
    private readonly OperationsRepository? _operations;

    public CleanupRunner(ICleanupService cleanup, PresetsRepository presets, OperationsRepository? operations = null)
    {
        _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
        _presets = presets ?? throw new ArgumentNullException(nameof(presets));
        _operations = operations;
    }

    public CleanupSessionResult RunPreset(
        string presetName,
        CleanupContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presetName))
            throw new ArgumentException("Preset is required.", nameof(presetName));

        if (!_presets.CleanupPresets.TryGetValue(presetName, out var preset))
            throw new ArgumentException("Unknown cleanup preset: " + presetName, nameof(presetName));

        return Run(preset.Keys, ctx, cancellationToken);
    }

    public CleanupSessionResult Run(
        IEnumerable<string> operationKeys,
        CleanupContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (operationKeys == null || ctx == null)
            return EmptyResult(canceled: false);

        using var linkedCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, ctx.CancellationToken);
        ctx = new CleanupContext(ctx.Safety, ctx.Whitelist, ctx.Quarantine, ctx.Progress,
            linkedCancellation.Token, ctx.DryRun, ctx.Commands, ctx.Registry);

        var keys = operationKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var outcomes = new List<OperationOutcome>(keys.Count);

        long totalBytes = 0;
        long previewBytes = 0, quarantinedBytes = 0;
        int quarantinedFiles = 0;
        int totalDeleted = 0, totalSkipped = 0, failed = 0;
        var sw = Stopwatch.StartNew();
        bool canceled = false;

        foreach (var key in keys)
        {
            if (cancellationToken.IsCancellationRequested || ctx.IsCancellationRequested)
            {
                canceled = true;
                break;
            }

            var opSw = Stopwatch.StartNew();
            CleanupOperationResult result;
            try { result = _cleanup.RunOperation(key, ctx); }
            catch (OperationCanceledException)
            {
                canceled = true;
                outcomes.Add(new OperationOutcome { Key = key, Success = false, Canceled = true,
                    Message = "Operation canceled; previously completed changes remain.", Duration = opSw.Elapsed });
                break;
            }
            catch (Exception ex) { result = CleanupOperationResult.Fail(ex.Message); }
            opSw.Stop();

            outcomes.Add(new OperationOutcome
            {
                Key = key,
                Handler = _operations?.GetByKey(key)?.Handler ?? "",
                Success = result.Success,
                BytesFreed = result.BytesFreed,
                PreviewBytes = result.PreviewBytes,
                BytesQuarantined = result.BytesQuarantined,
                FilesQuarantined = result.FilesQuarantined,
                FilesDeleted = result.FilesDeleted,
                FilesSkipped = result.FilesSkipped,
                Errors = result.Errors,
                Message = result.Message,
                Duration = opSw.Elapsed
            });

            totalBytes += result.BytesFreed;
            previewBytes += result.PreviewBytes;
            quarantinedBytes += result.BytesQuarantined;
            quarantinedFiles += result.FilesQuarantined;
            totalDeleted += result.FilesDeleted;
            totalSkipped += result.FilesSkipped;
            if (!result.Success) failed++;
            if (cancellationToken.IsCancellationRequested || ctx.IsCancellationRequested) { canceled = true; break; }
        }

        sw.Stop();

        return new CleanupSessionResult
        {
            BytesFreed = totalBytes,
            PreviewBytes = previewBytes,
            BytesQuarantined = quarantinedBytes,
            FilesQuarantined = quarantinedFiles,
            FilesDeleted = totalDeleted,
            FilesSkipped = totalSkipped,
            OperationsRun = outcomes.Count,
            OperationsFailed = failed,
            Duration = sw.Elapsed,
            Outcomes = outcomes,
            Canceled = canceled,
            SkipDetails = ctx.SkipDetails,
            OmittedSkipDetails = ctx.OmittedSkipDetails,
            PreviewFiles = ctx.PreviewFiles,
            PreviewFileCounts = ctx.PreviewFileCounts,
            OmittedPreviewFiles = ctx.OmittedPreviewFiles
        };
    }

    private static CleanupSessionResult EmptyResult(bool canceled) => new()
    {
        BytesFreed = 0,
        FilesDeleted = 0,
        FilesSkipped = 0,
        OperationsRun = 0,
        OperationsFailed = 0,
        Duration = TimeSpan.Zero,
        Outcomes = Array.Empty<OperationOutcome>(),
        Canceled = canceled
    };
}
