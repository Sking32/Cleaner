using System;
using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "SmartDownloads": чистит устаревшие файлы из %USERPROFILE%\Downloads.
/// Возраст и фильтр задаются: op.OlderThanDays (по умолчанию 30).
/// Safety: средняя — тест в dry-run обязателен.
/// </summary>
public sealed class SmartDownloadsHandler : ICleanupHandler
{
    private readonly Func<string> _downloads;
    public SmartDownloadsHandler(Func<string>? downloads = null) => _downloads = downloads ?? (() => KnownFolders.Downloads);
    public string Name => "SmartDownloads";

    private static readonly HashSet<string> TemporaryExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".tmp", ".part", ".crdownload", ".download"
        };

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        var downloads = _downloads();
        if (string.IsNullOrWhiteSpace(downloads)) return CleanupOperationResult.Fail("Cannot resolve Downloads.");

        if (!Directory.Exists(downloads))
            return CleanupOperationResult.Ok(message: "no Downloads folder");

        var days = op.OlderThanDays ?? 30;
        if (ctx.CapturePlan) ctx.ApproveRoots(op.Key, new[] { downloads });
        var cutoff = DateTime.UtcNow.AddDays(-days);

        long bytes = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var file in Directory.EnumerateFiles(downloads))
        {
            if (ctx.IsCancellationRequested) break;

            var ext = Path.GetExtension(file);
            if (!TemporaryExtensions.Contains(ext)) { skipped++; continue; }

            DateTime lastWrite;
            try { lastWrite = File.GetLastWriteTimeUtc(file); }
            catch { skipped++; continue; }

            if (lastWrite > cutoff) { skipped++; continue; }

            var r = PathCleanupHandler.ProcessSingleFile(file, op.Key, ctx, errors);
            bytes += r.bytes; deleted += r.deleted; skipped += r.skipped;
        }

        ctx.Report(op.Key, Name, downloads, deleted + skipped, bytes, "done");

        return new CleanupOperationResult
        {
            Success = errors.Count == 0,
            BytesFreed = !ctx.DryRun && ctx.FileMode == CleanupFileMode.PermanentDelete ? bytes : 0, PreviewBytes = ctx.DryRun ? bytes : 0, BytesQuarantined = !ctx.DryRun && ctx.FileMode == CleanupFileMode.Quarantine ? bytes : 0,
            FilesDeleted = ctx.FileMode == CleanupFileMode.PermanentDelete ? deleted : 0, FilesQuarantined = ctx.FileMode == CleanupFileMode.Quarantine ? deleted : 0,
            FilesSkipped = skipped,
            Errors = errors
        };
    }
}
