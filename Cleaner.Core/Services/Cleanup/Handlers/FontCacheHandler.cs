using System;
using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "FontCache": останавливает службу FontCache, удаляет *.dat
/// из %WINDIR%\ServiceProfiles\LocalService\AppData\Local\FontCache
/// и %LOCALAPPDATA%\FontCache, затем запускает службу обратно.
/// </summary>
public sealed class FontCacheHandler : ICleanupHandler
{
    public string Name => "FontCache";

    private const string ServiceName = "FontCache";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        if (ctx.DryRun)
        {
            ctx.Report(op.Key, Name, null, 0, 0, "preview");
            return CleanupOperationResult.Ok(message: "dry-run");
        }

        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var dirs = new List<string>
        {
            Path.Combine(windir, "ServiceProfiles", "LocalService", "AppData", "Local", "FontCache"),
            Path.Combine(local, "FontCache")
        };

        bool stopped = ServiceHelper.Exists(ServiceName);
        if (stopped) ServiceHelper.Stop(ServiceName);

        long bytes = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();

        try
        {
            foreach (var dir in dirs)
            {
                if (ctx.IsCancellationRequested) break;
                if (!Directory.Exists(dir)) continue;

                IEnumerable<string> files;
                try { files = Directory.EnumerateFiles(dir, "*.dat"); }
                catch { continue; }

                foreach (var f in files)
                {
                    if (ctx.IsCancellationRequested) break;
                    var r = PathCleanupHandler.ProcessSingleFile(f, op.Key, ctx, errors);
                    bytes += r.bytes; deleted += r.deleted; skipped += r.skipped;
                }
            }
        }
        finally
        {
            if (stopped) ServiceHelper.Start(ServiceName);
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