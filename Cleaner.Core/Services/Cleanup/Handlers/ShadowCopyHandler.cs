using System;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "ShadowCopy": удаляет старые теневые копии через vssadmin.
/// Оставляет последнюю (свежую). Требует админских прав.
/// </summary>
public sealed class ShadowCopyHandler : ICleanupHandler
{
    public string Name => "ShadowCopy";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        if (ctx.DryRun)
        {
            ctx.Report(op.Key, Name, "vssadmin.exe", 0, 0, "preview");
            return CleanupOperationResult.Ok(message: "dry-run");
        }

        try
        {
            var result = CommandRunner.Run(
                "vssadmin.exe",
                "delete shadows /for=C: /oldest",
                timeoutMs: 300_000);

            ctx.Report(op.Key, Name, "vssadmin.exe", 0, 0,
                result.Success ? "done" : "failed");

            // vssadmin часто возвращает ошибку даже когда копий нет — не считаем фейлом.
            return CleanupOperationResult.Ok(
                message: $"vssadmin exit={result.ExitCode}");
        }
        catch (Exception ex)
        {
            return CleanupOperationResult.Fail($"vssadmin: {ex.Message}");
        }
    }
}