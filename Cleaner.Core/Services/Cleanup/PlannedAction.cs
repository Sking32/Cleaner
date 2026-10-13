using System.Text.Json;
using Cleaner.Core.Data;
using Cleaner.Core.Registry;
using Cleaner.Core.Services.Cleanup.Handlers;

namespace Cleaner.Core.Services.Cleanup;

internal sealed record PlannedAction(string Key, string Definition, string? RegistryState)
{
    internal static PlannedAction Capture(OperationEntry operation, IRegistryAccess registry) =>
        new(operation.Key, JsonSerializer.Serialize(operation), Snapshot(operation, registry));
    internal bool Matches(OperationEntry operation, IRegistryAccess registry) =>
        Definition == JsonSerializer.Serialize(operation) && RegistryState == Snapshot(operation, registry);

    private static string? Snapshot(OperationEntry operation, IRegistryAccess registry)
    {
        var path = operation.Handler == "RegCleanRunMRU" ? @"HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\RunMRU" : operation.RegistryKey;
        if (operation.Handler is not ("RegCleanKey" or "RegCleanRunMRU") || string.IsNullOrWhiteSpace(path)) return null;
        var (hive, key) = RegCleanKeyHandler.ParseRegistryPath(path);
        return JsonSerializer.Serialize(new { Exists = registry.KeyExists(hive, key), Values = registry.GetValueNames(hive, key)
            .OrderBy(n => n, StringComparer.Ordinal).Select(name => new { name, kind = registry.GetValueKind(hive, key, name), value = registry.GetValue(hive, key, name) }).ToArray() });
    }
}
