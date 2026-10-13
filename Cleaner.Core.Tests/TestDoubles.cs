using Cleaner.Core.Registry;
using Cleaner.Core.Services;
using Microsoft.Win32;

namespace Cleaner.Core.Tests;

// These doubles contain no calls to Windows or external processes.
internal sealed class MemoryRegistry : IRegistryAccess
{
    private readonly Dictionary<string, Dictionary<string, (object Value, RegistryValueKind Kind)>> _keys = new(StringComparer.OrdinalIgnoreCase);
    private static string Target(string hive, string key) => hive.ToUpperInvariant() + "\\" + key.TrimEnd('\\');
    public object? GetValue(string hive, string key, string name) => _keys.TryGetValue(Target(hive, key), out var values) && values.TryGetValue(name, out var item) ? item.Value : null;
    public RegistryValueKind? GetValueKind(string hive, string key, string name) => _keys.TryGetValue(Target(hive, key), out var values) && values.TryGetValue(name, out var item) ? item.Kind : null;
    public bool KeyExists(string hive, string key) => _keys.ContainsKey(Target(hive, key));
    public bool ValueExists(string hive, string key, string name) => GetValueKind(hive, key, name) != null;
    public IReadOnlyList<string> GetValueNames(string hive, string key) => _keys.TryGetValue(Target(hive, key), out var values) ? values.Keys.ToArray() : Array.Empty<string>();
    public void SetValue(string hive, string key, string name, object value, RegistryValueKind kind, bool createKeyIfMissing = false)
    {
        var target = Target(hive, key);
        if (!_keys.TryGetValue(target, out var values))
        {
            if (!createKeyIfMissing) throw new InvalidOperationException("Registry key not found");
            var parts = target.Split('\\');
            for (int i = 1; i <= parts.Length; i++) _keys.TryAdd(string.Join("\\", parts.Take(i)), new(StringComparer.OrdinalIgnoreCase));
            values = _keys[target];
        }
        values[name] = (value, kind);
    }
    public bool DeleteValue(string hive, string key, string name) => _keys.TryGetValue(Target(hive, key), out var values) && values.Remove(name);
    public bool DeleteKey(string hive, string key, bool recursive = true)
    {
        var target = Target(hive, key);
        var children = _keys.Keys.Where(k => k.StartsWith(target + "\\", StringComparison.OrdinalIgnoreCase)).ToArray();
        if (!recursive && children.Length > 0) return false;
        foreach (var child in children) _keys.Remove(child);
        return _keys.Remove(target);
    }
    public IReadOnlyList<string> GetSubKeyNames(string hive, string key)
    {
        var prefix = Target(hive, key) + "\\";
        return _keys.Keys.Where(k => k.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(k => k[prefix.Length..].Split('\\')[0]).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }
}

internal sealed class FakeCommands : ICommandExecutor
{
    public List<(string File, string Arguments)> Calls { get; } = new();
    public Func<string, string, CommandResult>? Respond { get; set; }
    public CommandResult Run(string fileName, string? arguments, int timeoutMs = 60000, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var args = arguments ?? "";
        Calls.Add((fileName, args));
        return Respond?.Invoke(fileName, args) ?? new CommandResult(
            args.Contains("exit 3") ? 3 : 0,
            args.Contains("echo ") ? args[(args.IndexOf("echo ", StringComparison.Ordinal) + 5)..] : "", "");
    }
}
