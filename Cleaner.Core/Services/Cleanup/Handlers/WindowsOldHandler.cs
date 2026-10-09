using System;
using System.IO;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "WindowsOld": удаляет C:\Windows.old. Требует админских прав
/// и владения папкой (takeown/icacls). Просто Directory.Delete часто падает
/// с UnauthorizedAccessException.
/// </summary>
public sealed class WindowsOldHandler : ICleanupHandler
{
    public string Name => "WindowsOld";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        var drive = Environment.GetEnvironmentVariable("SystemDrive") ?? "C:";
        var oldDir = Path.Combine(drive + "\\", "Windows.old");

        if (!Directory.Exists(oldDir))
            return CleanupOperationResult.Ok(message: "no Windows.old");

        if (ctx.DryRun)
        {
            ctx.Report(op.Key, Name, oldDir, 0, 0, "preview");
            return CleanupOperationResult.Ok(message: "dry-run");
        }

        // 1. Забираем владение и права (best-effort).
        CommandRunner.Run("takeown.exe", $"/F \"{oldDir}\" /R /A /D Y", timeoutMs: 300_000);
        CommandRunner.Run("icacls.exe", $"\"{oldDir}\" /grant *S-1-5-32-544:F /T /C /Q",
            timeoutMs: 300_000);

        // 2. Пробуем удалить.
        try
        {
            Directory.Delete(oldDir, recursive: true);
            ctx.Report(op.Key, Name, oldDir, 0, 0, "done");
            return CleanupOperationResult.Ok(message: "Windows.old removed");
        }
        catch (Exception ex)
        {
            return CleanupOperationResult.Fail($"{oldDir}: {ex.Message}");
        }
    }
}