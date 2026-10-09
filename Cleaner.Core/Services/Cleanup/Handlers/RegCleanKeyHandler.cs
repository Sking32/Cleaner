using System;
using Cleaner.Core.Data;
using Cleaner.Core.Registry;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "RegCleanKey": удаляет значения в указанной ветке реестра.
/// Сама ветка остаётся. op.RegistryKey имеет вид "HKCU:\Software\...".
/// </summary>
public sealed class RegCleanKeyHandler : ICleanupHandler
{
    public string Name => "RegCleanKey";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        if (string.IsNullOrWhiteSpace(op.RegistryKey))
            return CleanupOperationResult.Ok(message: "no registryKey");

        var (hive, subKey) = ParseRegistryPath(op.RegistryKey!);
        if (string.IsNullOrEmpty(hive) || string.IsNullOrEmpty(subKey))
            return CleanupOperationResult.Fail($"invalid registry path: {op.RegistryKey}");

        if (!RegistryHelper.KeyExists(hive, subKey))
            return CleanupOperationResult.Ok(message: "key not found");

        if (ctx.DryRun)
        {
            ctx.Report(op.Key, Name, op.RegistryKey, 0, 0, "preview");
            return CleanupOperationResult.Ok(message: "dry-run");
        }

        try
        {
            var names = RegistryHelper.GetValueNames(hive, subKey);
            int deleted = 0;
            foreach (var name in names)
            {
                if (ctx.IsCancellationRequested) break;
                if (string.IsNullOrEmpty(name)) continue;   // (Default) не трогаем
                if (RegistryHelper.DeleteValue(hive, subKey, name)) deleted++;
            }

            ctx.Report(op.Key, Name, op.RegistryKey, deleted, 0, "done");
            return CleanupOperationResult.Ok(message: $"deleted {deleted} values");
        }
        catch (Exception ex)
        {
            return CleanupOperationResult.Fail($"{op.RegistryKey}: {ex.Message}");
        }
    }

    /// <summary>Разбирает "HKCU:\Software\X" → ("HKCU", "Software\\X").</summary>
    public static (string hive, string subKey) ParseRegistryPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return ("", "");

        var idx = path.IndexOf(':');
        if (idx <= 0) return ("", "");

        var hive = path.Substring(0, idx).Trim();
        var rest = path.Substring(idx + 1).TrimStart('\\', '/');
        if (hive.Length == 0 || rest.Length == 0) return ("", "");

        // Внутри RegistryHelper используется формат с одинарными backslash.
        return (hive, rest.Replace('/', '\\'));
    }
}