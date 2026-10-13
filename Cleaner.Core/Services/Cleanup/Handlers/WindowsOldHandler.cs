using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>Use Windows upgrade cleanup; never traverse Windows.old or change ownership/ACLs.</summary>
public sealed class WindowsOldHandler : ICleanupHandler
{
    public string Name => "WindowsOld";
    public CleanupOperationResult Run(OperationEntry operation, CleanupContext context)
    {
        if (context.DryRun) return CleanupOperationResult.Ok(message: "cleanup.system.preview");
        try
        {
            var result = context.Commands.Run("cleanmgr.exe", "/AUTOCLEAN", timeoutMs: 3_600_000, cancellationToken: context.CancellationToken);
            return result.Success ? CleanupOperationResult.Ok(message: "cleanup.windowsold.completed") : CleanupOperationResult.Fail(result.Combined.Trim());
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception error) { return CleanupOperationResult.Fail(error.Message); }
    }
}
