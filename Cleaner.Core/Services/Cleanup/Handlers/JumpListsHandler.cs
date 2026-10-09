using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "JumpLists": чистит AutomaticDestinations и CustomDestinations.
/// Файлы НЕ удаляются — только история переходов.
/// </summary>
public sealed class JumpListsHandler : ICleanupHandler
{
    public string Name => "JumpLists";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        var appData = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.ApplicationData);

        var targets = new[]
        {
            Path.Combine(appData, "Microsoft", "Windows", "Recent",
                "AutomaticDestinations"),
            Path.Combine(appData, "Microsoft", "Windows", "Recent",
                "CustomDestinations")
        };

        long bytes = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var dir in targets)
        {
            if (ctx.IsCancellationRequested) break;
            if (!Directory.Exists(dir)) continue;

            foreach (var file in Directory.EnumerateFiles(dir))
            {
                if (ctx.IsCancellationRequested) break;

                var r = PathCleanupHandler.ProcessSingleFile(file, op.Key, ctx, errors);
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