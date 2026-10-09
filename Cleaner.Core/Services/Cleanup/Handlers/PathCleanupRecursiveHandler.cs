using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "PathCleanupRecursive": чистит указанную папку рекурсивно,
/// но пропускает подпапки, перечисленные в <c>skipSubdirs</c>.
/// Используется для %WINDIR%\Logs с исключением "CBS".
/// </summary>
public sealed class PathCleanupRecursiveHandler : ICleanupHandler
{
    public string Name => "PathCleanupRecursive";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        var paths = PathExpander.ExpandAll(op.Paths);
        if (paths.Count == 0)
            return CleanupOperationResult.Ok(message: "no paths");

        var skip = new HashSet<string>(op.SkipSubdirs, System.StringComparer.OrdinalIgnoreCase);

        long bytes = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var path in paths)
        {
            if (ctx.IsCancellationRequested) break;
            if (!Directory.Exists(path)) continue;

            var r = Walk(path, op.Key, ctx, skip, errors);
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

    private (long bytes, int deleted, int skipped) Walk(
        string directory,
        string opKey,
        CleanupContext ctx,
        HashSet<string> skip,
        List<string> errors)
    {
        long bytes = 0;
        int deleted = 0, skipped = 0;

        foreach (var child in PathCleanupHandler.EnumerateSafeChildren(directory))
        {
            if (ctx.IsCancellationRequested) break;

            if (Directory.Exists(child))
            {
                var name = Path.GetFileName(child);
                if (skip.Contains(name))
                {
                    skipped++;
                    continue;
                }

                var sub = Walk(child, opKey, ctx, skip, errors);
                bytes += sub.bytes; deleted += sub.deleted; skipped += sub.skipped;

                if (!ctx.DryRun)
                {
                    try { Directory.Delete(child, recursive: false); } catch { }
                }
            }
            else
            {
                var f = PathCleanupHandler.ProcessSingleFile(child, opKey, ctx, errors);
                bytes += f.bytes; deleted += f.deleted; skipped += f.skipped;
            }
        }

        return (bytes, deleted, skipped);
    }
}