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
    public string Name => "SmartDownloads";

    private static readonly HashSet<string> SkipExt =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ".exe", ".msi", ".iso", ".zip", ".7z", ".rar"
        };

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        var downloads = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Downloads");

        if (!Directory.Exists(downloads))
            return CleanupOperationResult.Ok(message: "no Downloads folder");

        var days = op.OlderThanDays ?? 30;
        var cutoff = DateTime.UtcNow.AddDays(-days);

        long bytes = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var file in Directory.EnumerateFiles(downloads))
        {
            if (ctx.IsCancellationRequested) break;

            var ext = Path.GetExtension(file);
            if (SkipExt.Contains(ext)) { skipped++; continue; }

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
            BytesFreed = bytes,
            FilesDeleted = deleted,
            FilesSkipped = skipped,
            Errors = errors
        };
    }
}