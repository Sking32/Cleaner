using System;
using Cleaner.Core.Data;
using Cleaner.Core.Registry;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "RegCleanRunMRU": чистит историю диалога «Выполнить» (Win+R).
/// Ветка HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU.
/// </summary>
public sealed class RegCleanRunMRUHandler : ICleanupHandler
{
    public string Name => "RegCleanRunMRU";

    private const string Hive = "HKCU";
    private const string SubKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        if (!RegistryHelper.KeyExists(Hive, SubKey))
            return CleanupOperationResult.Ok(message: "no RunMRU");

        if (ctx.DryRun)
        {
            ctx.Report(op.Key, Name, SubKey, 0, 0, "preview");
            return CleanupOperationResult.Ok(message: "dry-run");
        }

        try
        {
            var names = RegistryHelper.GetValueNames(Hive, SubKey);
            int deleted = 0;
            foreach (var name in names)
            {
                if (ctx.IsCancellationRequested) break;
                if (string.IsNullOrEmpty(name)) continue;   // (Default) оставляем
                if (RegistryHelper.DeleteValue(Hive, SubKey, name)) deleted++;
            }

            ctx.Report(op.Key, Name, SubKey, deleted, 0, "done");
            return CleanupOperationResult.Ok(message: $"deleted {deleted} values");
        }
        catch (Exception ex)
        {
            return CleanupOperationResult.Fail($"{SubKey}: {ex.Message}");
        }
    }
}