using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "MultiPathCleanup": чистит содержимое каждой из указанных папок,
/// но НЕ удаляет сами папки (в отличие от PathCleanup).
/// </summary>
public sealed class MultiPathCleanupHandler : ICleanupHandler
{
    public string Name => "MultiPathCleanup";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        var paths = PathExpander.ExpandAll(op.Paths);
        if (paths.Count == 0)
            return CleanupOperationResult.Ok(message: "no paths");

        long bytes = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var path in paths)
        {
            if (ctx.IsCancellationRequested) break;

            if (File.Exists(path))
            {
                var r = PathCleanupHandler.ProcessSingleFile(path, op.Key, ctx, errors);
                bytes += r.bytes; deleted += r.deleted; skipped += r.skipped;
            }
            else if (Directory.Exists(path))
            {
                // deleteSelf: false — сама папка остаётся.
                var r = PathCleanupHandler.ProcessDirectory(path, op.Key, ctx, errors, deleteSelf: false);
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