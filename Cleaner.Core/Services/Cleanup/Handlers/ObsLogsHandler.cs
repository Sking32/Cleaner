using System;
using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "ObsLogs": удаляет *.txt из %APPDATA%\obs-studio\logs
/// старше op.OlderThanDays (по умолчанию 7).
/// </summary>
public sealed class ObsLogsHandler : ICleanupHandler
{
    public string Name => "ObsLogs";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var logsDir = Path.Combine(appData, "obs-studio", "logs");

        if (!Directory.Exists(logsDir))
            return CleanupOperationResult.Ok(message: "no obs logs");

        var days = op.OlderThanDays ?? 7;
        var cutoff = DateTime.UtcNow.AddDays(-days);

        long bytes = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var file in Directory.EnumerateFiles(logsDir, "*.txt"))
        {
            if (ctx.IsCancellationRequested) break;

            DateTime created;
            try { created = File.GetCreationTimeUtc(file); }
            catch { continue; }

            if (created > cutoff)
            {
                skipped++;
                continue;
            }

            var r = PathCleanupHandler.ProcessSingleFile(file, op.Key, ctx, errors);
            bytes += r.bytes; deleted += r.deleted; skipped += r.skipped;
        }

        ctx.Report(op.Key, Name, null, deleted + skipped, bytes, "done");

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