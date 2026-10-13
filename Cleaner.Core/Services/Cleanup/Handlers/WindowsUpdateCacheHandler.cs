using System;
using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "WindowsUpdateCache": останавливает wuauserv и bits,
/// чистит %WINDIR%\SoftwareDistribution\Download, запускает службы обратно.
/// Требует админских прав.
/// </summary>
public sealed class WindowsUpdateCacheHandler : ICleanupHandler
{
    public string Name => "WindowsUpdateCache";

    private static readonly string[] Services = { "wuauserv", "bits" };

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        if (ctx.DryRun)
        {
            ctx.Report(op.Key, Name, null, 0, 0, "preview");
            return CleanupOperationResult.Ok(message: "dry-run");
        }

        var windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var dir = Path.Combine(windir, "SoftwareDistribution", "Download");

        // Останавливаем службы (если существуют).
        var stopped = new List<string>();
        foreach (var svc in Services)
        {
            if (ServiceHelper.GetStatus(svc) == "Running")
            {
                if (!ServiceHelper.StopAndWait(svc))
                {
                    ServiceHelper.StartAndWait(svc);
                    foreach (var previous in stopped) ServiceHelper.StartAndWait(previous);
                    return CleanupOperationResult.Fail("Cannot stop service safely: " + svc);
                }
                stopped.Add(svc);
            }
        }

        long bytes = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();

        try
        {
            if (Directory.Exists(dir))
            {
                var r = PathCleanupHandler.ProcessDirectory(
                    dir, op.Key, ctx, errors, deleteSelf: false);
                bytes += r.bytes; deleted += r.deleted; skipped += r.skipped;
            }
        }
        finally
        {
            foreach (var svc in stopped)
                if (!ServiceHelper.StartAndWait(svc)) errors.Add("Cannot restore service running state: " + svc);
        }

        ctx.Report(op.Key, Name, dir, deleted + skipped, bytes, "done");

        return new CleanupOperationResult
        {
            Success = errors.Count == 0,
            BytesFreed = 0, PreviewBytes = ctx.DryRun ? bytes : 0, BytesQuarantined = ctx.DryRun ? 0 : bytes,
            FilesDeleted = 0, FilesQuarantined = deleted,
            FilesSkipped = skipped,
            Errors = errors
        };
    }
}
