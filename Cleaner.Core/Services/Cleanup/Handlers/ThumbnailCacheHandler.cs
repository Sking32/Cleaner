using System;
using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "ThumbnailCache": чистит thumbcache_*.db и iconcache_*.db
/// в %LOCALAPPDATA%\Microsoft\Windows\Explorer. Explorer перезапускается
/// после удаления (файлы удерживаются, но на NTFS это не блокирует удаление).
/// </summary>
public sealed class ThumbnailCacheHandler : ICleanupHandler
{
    public string Name => "ThumbnailCache";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var explorerDir = Path.Combine(local, "Microsoft", "Windows", "Explorer");

        if (!Directory.Exists(explorerDir))
            return CleanupOperationResult.Ok(message: "no explorer cache dir");

        long bytes = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();

        IEnumerable<string> files;
        try { files = Directory.EnumerateFiles(explorerDir); }
        catch { return CleanupOperationResult.Ok(message: "no access"); }

        foreach (var file in files)
        {
            if (ctx.IsCancellationRequested) break;

            var name = Path.GetFileName(file);
            if (!name.StartsWith("thumbcache_", StringComparison.OrdinalIgnoreCase) &&
                !name.StartsWith("iconcache_", StringComparison.OrdinalIgnoreCase))
                continue;

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