using System;
using System.IO;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "WindowsOld": удаляет C:\Windows.old. Требует админских прав
/// После проверки дерева удаляет его без изменения владельцев и ACL.
/// При отказе доступа предлагает штатные параметры хранилища Windows.
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

        try
        {
            // Never recursively change ownership through an unverified tree.
            var pending = new System.Collections.Generic.Stack<string>();
            pending.Push(oldDir);
            while (pending.Count > 0)
            {
                ctx.CancellationToken.ThrowIfCancellationRequested();
                var current = pending.Pop();
                if (!ctx.Safety.Check(current).IsSafe || ctx.Whitelist.IsWhitelisted(current))
                    return CleanupOperationResult.Fail("Windows.old contains a protected or excluded path; use Windows Storage settings.");
                if (Directory.Exists(current))
                    foreach (var child in Directory.GetFileSystemEntries(current)) pending.Push(child);
            }
            ctx.CancellationToken.ThrowIfCancellationRequested();
            Directory.Delete(oldDir, recursive: true);
            ctx.Report(op.Key, Name, oldDir, 0, 0, "done");
            return CleanupOperationResult.Ok(message: "Windows.old removed");
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return CleanupOperationResult.Fail($"{oldDir}: {ex.Message}. Use Windows Storage settings if access is denied.");
        }
    }
}
