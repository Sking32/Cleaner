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

    public CleanupRunner(ICleanupService cleanup, PresetsRepository presets)
    {
        _cleanup = cleanup ?? throw new ArgumentNullException(nameof(cleanup));
        _presets = presets ?? throw new ArgumentNullException(nameof(presets));
    }

    public CleanupSessionResult RunPreset(
        string presetName,
        CleanupContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(presetName))
            return EmptyResult(canceled: false);

        if (!_presets.CleanupPresets.TryGetValue(presetName, out var preset))
            return EmptyResult(canceled: false);

        return Run(preset.Keys, ctx, cancellationToken);
    }

    public CleanupSessionResult Run(
        IEnumerable<string> operationKeys,
        CleanupContext ctx,
        CancellationToken cancellationToken = default)
    {
        if (operationKeys == null || ctx == null)
            return EmptyResult(canceled: false);

        var keys = operationKeys.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var outcomes = new List<OperationOutcome>(keys.Count);

        long totalBytes = 0;
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
            var result = _cleanup.RunOperation(key, ctx);
            opSw.Stop();

            outcomes.Add(new OperationOutcome
            {
                Key = key,
                Success = result.Success,
                BytesFreed = result.BytesFreed,
                FilesDeleted = result.FilesDeleted,
                FilesSkipped = result.FilesSkipped,
                Errors = result.Errors,
                Duration = opSw.Elapsed
            });

            totalBytes += result.BytesFreed;
            totalDeleted += result.FilesDeleted;
            totalSkipped += result.FilesSkipped;
            if (!result.Success) failed++;
        }

        sw.Stop();

        return new CleanupSessionResult
        {
            BytesFreed = totalBytes,
            FilesDeleted = totalDeleted,
            FilesSkipped = totalSkipped,
            OperationsRun = outcomes.Count,
            OperationsFailed = failed,
            Duration = sw.Elapsed,
            Outcomes = outcomes,
            Canceled = canceled
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