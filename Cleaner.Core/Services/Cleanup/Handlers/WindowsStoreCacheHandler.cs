using System;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "WindowsStoreCache": сбрасывает кэш Microsoft Store
/// через wsreset.exe. Сам файлы не трогает.
/// </summary>
public sealed class WindowsStoreCacheHandler : ICleanupHandler
{
    public string Name => "WindowsStoreCache";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        if (ctx.DryRun)
        {
            ctx.Report(op.Key, Name, "wsreset.exe", 0, 0, "preview");
            return CleanupOperationResult.Ok(message: "dry-run");
        }

        try
        {
            var result = ctx.Commands.Run("wsreset.exe", "", timeoutMs: 120_000, cancellationToken: ctx.CancellationToken);
            ctx.Report(op.Key, Name, "wsreset.exe", 0, 0,
                result.Success ? "done" : "failed");

            // wsreset возвращает 0 не всегда корректно; успех = процесс не упал.
            return result.Success ? CleanupOperationResult.Ok(message: "Store cache reset")
                : CleanupOperationResult.Fail($"wsreset exit={result.ExitCode}: {result.Combined.Trim()}");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return CleanupOperationResult.Fail($"wsreset: {ex.Message}");
        }
    }
}
