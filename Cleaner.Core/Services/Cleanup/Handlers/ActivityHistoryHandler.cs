using System;
using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "ActivityHistory": удаляет ActivitiesCache.db* из
/// %LOCALAPPDATA%\ConnectedDevicesPlatform\L.&lt;user&gt;\.
/// </summary>
public sealed class ActivityHistoryHandler : ICleanupHandler
{
    public string Name => "ActivityHistory";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var root = Path.Combine(local, "ConnectedDevicesPlatform");

        if (!Directory.Exists(root))
            return CleanupOperationResult.Ok(message: "no ConnectedDevicesPlatform");

        long bytes = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();

        IEnumerable<string> userDirs;
        try { userDirs = Directory.EnumerateDirectories(root); }
        catch { return CleanupOperationResult.Ok(message: "no access"); }

        foreach (var userDir in userDirs)
        {
            if (ctx.IsCancellationRequested) break;

            IEnumerable<string> files;
            try { files = Directory.EnumerateFiles(userDir, "ActivitiesCache.db*"); }
            catch { continue; }

            foreach (var f in files)
            {
                if (ctx.IsCancellationRequested) break;
                var r = PathCleanupHandler.ProcessSingleFile(f, op.Key, ctx, errors);
                bytes += r.bytes; deleted += r.deleted; skipped += r.skipped;
            }
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