using System;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "ShadowCopy": удаляет старые теневые копии системного тома через CIM.
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
            // Address exact snapshots on the actual system volume, retaining the newest.
            const string script = "$ErrorActionPreference='Stop'; " +
                "$volume=Get-CimInstance Win32_Volume | Where-Object DriveLetter -eq $env:SystemDrive; " +
                "if(!$volume){throw 'System volume not found'}; " +
                "$copies=@(Get-CimInstance Win32_ShadowCopy | Where-Object VolumeName -eq $volume.DeviceID | Sort-Object InstallDate); " +
                "$old=@($copies | Select-Object -SkipLast 1); " +
                "foreach($copy in $old){$copy | Remove-CimInstance -Confirm:$false -ErrorAction Stop}";
            var encoded = Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(script));
            var result = ctx.Commands.Run("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " + encoded,
                timeoutMs: 300_000, cancellationToken: ctx.CancellationToken);

            ctx.Report(op.Key, Name, "vssadmin.exe", 0, 0,
                result.Success ? "done" : "failed");

            return result.Success ? CleanupOperationResult.Ok(message: "Old system-volume snapshots removed; newest retained")
                : CleanupOperationResult.Fail(result.Combined.Trim());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return CleanupOperationResult.Fail($"vssadmin: {ex.Message}");
        }
    }
}
