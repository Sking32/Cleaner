using System;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "ToggleHibernation": powercfg /h off — отключает гибернацию
/// и удаляет hiberfil.sys. Требует админских прав.
/// </summary>
public sealed class ToggleHibernationHandler : ICleanupHandler
{
    public string Name => "ToggleHibernation";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        if (ctx.DryRun)
        {
            ctx.Report(op.Key, Name, "powercfg.exe", 0, 0, "preview");
            return CleanupOperationResult.Ok(message: "dry-run");
        }

        try
        {
            var result = CommandRunner.Run(
                "powercfg.exe",
                "/h off",
                timeoutMs: 60_000);

            ctx.Report(op.Key, Name, "powercfg.exe", 0, 0,
                result.Success ? "done" : "failed");

            return result.Success
                ? CleanupOperationResult.Ok(message: "hibernation off")
                : CleanupOperationResult.Fail(
                    $"powercfg exit={result.ExitCode}: {result.StdErr.Trim()}".Trim());
        }
        catch (Exception ex)
        {
            return CleanupOperationResult.Fail($"powercfg: {ex.Message}");
        }
    }
}