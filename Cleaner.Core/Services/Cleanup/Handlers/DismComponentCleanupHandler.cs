using System;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "DismComponentCleanup": DISM /Online /Cleanup-Image /StartComponentCleanup.
/// Может занять 5–60 минут. Требует админских прав.
/// </summary>
public sealed class DismComponentCleanupHandler : ICleanupHandler
{
    public string Name => "DismComponentCleanup";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        if (ctx.DryRun)
        {
            ctx.Report(op.Key, Name, "dism.exe", 0, 0, "preview");
            return CleanupOperationResult.Ok(message: "dry-run");
        }

        try
        {
            var result = CommandRunner.Run(
                "dism.exe",
                "/Online /Cleanup-Image /StartComponentCleanup",
                timeoutMs: 3_600_000);   // до часа

            ctx.Report(op.Key, Name, "dism.exe", 0, 0,
                result.Success ? "done" : "failed");

            return result.Success
                ? CleanupOperationResult.Ok(message: "dism ok")
                : CleanupOperationResult.Fail(
                    $"dism exit={result.ExitCode}: {result.StdErr.Trim()}".Trim());
        }
        catch (Exception ex)
        {
            return CleanupOperationResult.Fail($"dism: {ex.Message}");
        }
    }
}