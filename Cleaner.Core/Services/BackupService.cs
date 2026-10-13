using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Cleaner.Core.Registry;
using Microsoft.Win32;

namespace Cleaner.Core.Services;

/// <summary>Тип сохранённого бэкапа.</summary>
public enum BackupType
{
    DWord,
    QWord,
    String,
    ExpandString,
    MultiString,
    Binary,
    Remove,
    Command
}

public sealed class BackupEntry
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("oldValue")] public JsonElement? OldValue { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "DWord";
    [JsonPropertyName("category")] public string Category { get; set; } = "General";
    [JsonPropertyName("when")] public string When { get; set; } = "";
    [JsonPropertyName("wasRunning")] public bool? WasRunning { get; set; }
    [JsonPropertyName("keyExisted")] public bool? KeyExisted { get; set; }

    [JsonIgnore] public bool IsService => Type == "Service";
}

public interface IBackupService
{
    void SaveBeforeChange(string backupKey, string hive, string subKey, string name, string category = "General");
    void SaveServiceBeforeChange(string backupKey, string serviceName, string oldStartType, string category = "General", bool? wasRunning = null);
    void SaveRaw(string backupKey, string path, string name, object? oldValue, BackupType type, string category = "General");

    bool RestoreByKey(string backupKey, bool retainBackup = false);
    void RemoveEntries(IEnumerable<string> keys);
    bool RestoreService(string serviceName);
    (int Restored, int Failed) RestoreAll();

    IReadOnlyCollection<string> Keys { get; }
    IReadOnlyDictionary<string, BackupEntry> Entries { get; }

    void Clear();
    string? ExportRegToFile(string outputPath);

    string BackupFilePath { get; }
}

public sealed class BackupService : IBackupService
{
    private readonly Dictionary<string, BackupEntry> _entries = new();
    private readonly object _lock = new();
    private readonly IRegistryAccess _registry;
    private readonly ICommandExecutor _commands;

    public string BackupFilePath { get; }

    public IReadOnlyCollection<string> Keys
    {
        get { lock (_lock) return _entries.Keys.ToArray(); }
    }

    public IReadOnlyDictionary<string, BackupEntry> Entries
    {
        get { lock (_lock) return new Dictionary<string, BackupEntry>(_entries); }
    }

    public BackupService(string? backupFilePath = null, IRegistryAccess? registry = null, ICommandExecutor? commands = null)
    {
        _registry = registry ?? new WindowsRegistryAccess();
        _commands = commands ?? new SystemCommandExecutor();
        if (string.IsNullOrWhiteSpace(backupFilePath))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var dir = Path.Combine(localAppData, "Cleaner");
            Directory.CreateDirectory(dir);
            backupFilePath = Path.Combine(dir, "opt-backup.json");
        }
        BackupFilePath = backupFilePath;
        Load();
    }

    // ---------- Save ----------

    public void SaveBeforeChange(string backupKey, string hive, string subKey, string name, string category = "General")
    {
        if (string.IsNullOrWhiteSpace(backupKey)) throw new ArgumentException("backupKey is required");

        lock (_lock)
        {
            if (_entries.ContainsKey(backupKey)) return;

            var path = $"{hive}\\{subKey}";
            var oldValue = _registry.GetValue(hive, subKey, name);
            var kind = _registry.GetValueKind(hive, subKey, name);

            BackupEntry entry;
            if (kind == null)
            {
                entry = new BackupEntry
                {
                    Path = path,
                    Name = name,
                    Type = BackupType.Remove.ToString(),
                    Category = category,
                    When = DateTime.UtcNow.ToString("o")
                };
            }
            else
            {
                // Для Binary сериализуем в hex-строку — System.Text.Json не умеет byte[] без Base64
                JsonElement? serialized;
                var backupType = MapKind(kind.Value);

                if (backupType == BackupType.Binary && oldValue is byte[] bytes)
                {
                    var hex = string.Concat(bytes.Select(b => b.ToString("x2")));
                    serialized = JsonSerializer.SerializeToElement(hex);
                }
                else
                {
                    serialized = JsonSerializer.SerializeToElement(oldValue);
                }

                entry = new BackupEntry
                {
                    Path = path,
                    Name = name,
                    OldValue = serialized,
                    Type = backupType.ToString(),
                    Category = category,
                    When = DateTime.UtcNow.ToString("o")
                };
            }
            entry.KeyExisted = _registry.KeyExists(hive, subKey);

            PersistNew(backupKey, entry);
        }
    }

    public void SaveServiceBeforeChange(string backupKey, string serviceName, string oldStartType, string category = "General", bool? wasRunning = null)
    {
        if (string.IsNullOrWhiteSpace(backupKey)) throw new ArgumentException("backupKey is required");

        lock (_lock)
        {
            if (_entries.ContainsKey(backupKey)) return;

            PersistNew(backupKey, new BackupEntry
            {
                Path = "",
                Name = serviceName,
                OldValue = JsonSerializer.SerializeToElement(oldStartType),
                WasRunning = wasRunning,
                Type = "Service",
                Category = category,
                When = DateTime.UtcNow.ToString("o")
            });
        }
    }

    public void SaveRaw(string backupKey, string path, string name, object? oldValue, BackupType type, string category = "General")
    {
        lock (_lock)
        {
            if (_entries.ContainsKey(backupKey)) return;

            PersistNew(backupKey, new BackupEntry
            {
                Path = path,
                Name = name,
                OldValue = oldValue == null ? null : JsonSerializer.SerializeToElement(
                    type == BackupType.Binary && oldValue is byte[] bytes ? Convert.ToHexString(bytes) : oldValue),
                Type = type.ToString(),
                Category = category,
                When = DateTime.UtcNow.ToString("o")
            });
        }
    }

    // ---------- Restore ----------

    public bool RestoreByKey(string backupKey, bool retainBackup = false)
    {
        lock (_lock)
        {
            if (!_entries.TryGetValue(backupKey, out var entry)) return false;
            if (!RestoreEntry(entry)) return false;
            if (retainBackup) return true;
            _entries.Remove(backupKey);
            try { Save(); return true; }
            catch (Exception ex)
            {
                _entries[backupKey] = entry;
                CoreLog.Warn("Restored value but could not persist recovery progress", ex);
                return false;
            }
        }
    }

    public void RemoveEntries(IEnumerable<string> keys)
    {
        lock (_lock)
        {
            var saved = new Dictionary<string, BackupEntry>(_entries);
            foreach (var key in keys) _entries.Remove(key);
            try { Save(); }
            catch { _entries.Clear(); foreach (var item in saved) _entries[item.Key] = item.Value; throw; }
        }
    }

    public bool RestoreService(string serviceName)
    {
        lock (_lock)
        {
            var key = _entries.FirstOrDefault(kv => kv.Value.IsService && kv.Value.Name == serviceName).Key;
            return key != null && RestoreByKey(key);
        }
    }

    public (int Restored, int Failed) RestoreAll()
    {
        string[] snapshot;
        lock (_lock) snapshot = _entries.Keys.Reverse().ToArray();
        int ok = 0, fail = 0;
        foreach (var key in snapshot)
            if (RestoreByKey(key)) ok++; else fail++;
        return (ok, fail);
    }

    private bool RestoreEntry(BackupEntry e)
    {
        try
        {
            if (e.IsService)
            {
                var startType = e.OldValue?.GetString() ?? "Automatic";
                if (!System.Text.RegularExpressions.Regex.IsMatch(e.Name, @"^[A-Za-z0-9_.-]+$")) return false;
                if (!SetServiceStartType(e.Name, startType)) return false;
                if (e.WasRunning == null) return true;
                var script = "$ErrorActionPreference='Stop'; $s=Get-Service -Name '" + e.Name + "'; " +
                    (e.WasRunning.Value
                        ? "if($s.Status -ne 'Running'){$s.Start()}; $s.WaitForStatus('Running',[TimeSpan]::FromSeconds(20))"
                        : "if($s.Status -ne 'Stopped'){$s.Stop()}; $s.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(20))");
                return _commands.Run("powershell.exe", "-NoProfile -NonInteractive -EncodedCommand " +
                    Convert.ToBase64String(Encoding.Unicode.GetBytes(script)), 30000).Success;
            }

            if (e.Type == nameof(BackupType.Command))
            {
                var restore = e.OldValue?.Deserialize<CommandRestore>();
                return restore != null && CommandBackup.IsAllowed(restore)
                    && _commands.Run(restore.FileName, restore.Arguments, 120000).Success;
            }

            if (string.IsNullOrWhiteSpace(e.Path)) return false;

            var sep = e.Path.IndexOf('\\');
            if (sep <= 0) return false;
            var hive = e.Path.Substring(0, sep);
            var subKey = e.Path.Substring(sep + 1);

            if (e.Type == BackupType.Remove.ToString())
            {
                _registry.DeleteValue(hive, subKey, e.Name);
                if (e.KeyExisted == false && _registry.GetValueNames(hive, subKey).Count == 0 && _registry.GetSubKeyNames(hive, subKey).Count == 0)
                    if (_registry.KeyExists(hive, subKey) && !_registry.DeleteKey(hive, subKey, recursive: false)) return false;
                return true;
            }

            if (e.OldValue == null) return false;

            var kind = ParseBackupType(e.Type);
            object value = kind switch
            {
                BackupType.DWord => e.OldValue.Value.ValueKind == JsonValueKind.Number
                                            ? e.OldValue.Value.GetInt32()
                                            : int.Parse(e.OldValue.Value.GetString() ?? "0"),
                BackupType.QWord => e.OldValue.Value.ValueKind == JsonValueKind.Number
                                            ? e.OldValue.Value.GetInt64()
                                            : long.Parse(e.OldValue.Value.GetString() ?? "0"),
                BackupType.String => e.OldValue.Value.GetString() ?? "",
                BackupType.ExpandString => e.OldValue.Value.GetString() ?? "",
                BackupType.MultiString => e.OldValue.Value.ValueKind == JsonValueKind.Array
                                            ? e.OldValue.Value.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                                            : new[] { e.OldValue.Value.GetString() ?? "" },
                BackupType.Binary => ParseBinary(e.OldValue.Value),
                _ => throw new InvalidOperationException($"Unsupported backup type: {e.Type}")
            };

            _registry.SetValue(hive, subKey, e.Name, value, MapBackupTypeToKind(kind), createKeyIfMissing: true);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Разбор Binary: поддерживает hex-строку, Base64-строку и массив чисел.</summary>
    private static byte[] ParseBinary(JsonElement e)
    {
        if (e.ValueKind == JsonValueKind.String)
        {
            var s = e.GetString() ?? "";
            // hex (чётная длина, только 0-9a-f)
            if (s.Length > 0 && s.Length % 2 == 0 && s.All(c => Uri.IsHexDigit(c)))
                return Convert.FromHexString(s);
            // иначе — Base64
            return Convert.FromBase64String(s);
        }

        if (e.ValueKind == JsonValueKind.Array)
            return e.EnumerateArray().Select(x => x.GetByte()).ToArray();

        throw new InvalidOperationException($"Unexpected binary JSON kind: {e.ValueKind}");
    }

    // ---------- Clear / Export ----------

    public void Clear()
    {
        lock (_lock)
        {
            var saved = _entries.ToArray();
            _entries.Clear();
            try { Save(); } catch { foreach (var item in saved) _entries[item.Key] = item.Value; throw; }
        }
    }

    public string? ExportRegToFile(string outputPath)
    {
        try
        {
            var sb = new StringBuilder();
            sb.AppendLine("Windows Registry Editor Version 5.00");
            sb.AppendLine();

            var byPath = new Dictionary<string, List<BackupEntry>>();
            lock (_lock)
            {
                foreach (var e in _entries.Values)
                {
                    if (e.IsService || e.Type == nameof(BackupType.Command)) continue;
                    if (string.IsNullOrWhiteSpace(e.Path)) continue;
                    if (!byPath.ContainsKey(e.Path)) byPath[e.Path] = new();
                    byPath[e.Path].Add(e);
                }
            }

            foreach (var (path, list) in byPath.OrderBy(x => x.Key))
            {
                sb.AppendLine($"[{ToRegPath(path)}]");
                foreach (var e in list)
                {
                    var namePart = string.IsNullOrEmpty(e.Name) ? "@" : $"\"{EscapeRegString(e.Name)}\"";
                    if (e.Type == BackupType.Remove.ToString())
                        sb.AppendLine($"{namePart}=-");
                    else
                        sb.AppendLine($"{namePart}={FormatRegValue(e)}");
                }
                sb.AppendLine();
            }

            var unicode = new UnicodeEncoding(bigEndian: false, byteOrderMark: true);
            File.WriteAllText(outputPath, sb.ToString(), unicode);
            return outputPath;
        }
        catch { return null; }
    }

    // ---------- Persistence ----------

    private void Load()
    {
        try
        {
            if (!File.Exists(BackupFilePath)) return;
            var json = File.ReadAllText(BackupFilePath);
            if (string.IsNullOrWhiteSpace(json)) throw new InvalidDataException("Empty backup file.");

            var doc = JsonSerializer.Deserialize<Dictionary<string, BackupEntry>>(json);
            if (doc == null || doc.Any(kv => string.IsNullOrWhiteSpace(kv.Key) || kv.Value == null || kv.Value.Name == null ||
                kv.Value.Path == null || kv.Value.Type == null))
                throw new InvalidDataException("Invalid backup entries.");

            lock (_lock)
            {
                _entries.Clear();
                foreach (var (k, v) in doc) _entries[k] = v;
            }
        }
        catch (Exception logEx) { throw new IOException("Cannot read backup; existing recovery data must be preserved.", logEx); }
    }

    private void Save()
    {
        AtomicFile.Write(BackupFilePath, JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true }));
    }

    private void PersistNew(string key, BackupEntry entry)
    {
        _entries.Add(key, entry);
        try { Save(); }
        catch { _entries.Remove(key); throw; }
    }

    // ---------- Helpers ----------

    private static BackupType MapKind(RegistryValueKind kind) => kind switch
    {
        RegistryValueKind.DWord => BackupType.DWord,
        RegistryValueKind.QWord => BackupType.QWord,
        RegistryValueKind.String => BackupType.String,
        RegistryValueKind.ExpandString => BackupType.ExpandString,
        RegistryValueKind.MultiString => BackupType.MultiString,
        RegistryValueKind.Binary => BackupType.Binary,
        _ => throw new NotSupportedException("Cannot safely back up registry kind: " + kind)
    };

    private static RegistryValueKind MapBackupTypeToKind(BackupType type) => type switch
    {
        BackupType.DWord => RegistryValueKind.DWord,
        BackupType.QWord => RegistryValueKind.QWord,
        BackupType.String => RegistryValueKind.String,
        BackupType.ExpandString => RegistryValueKind.ExpandString,
        BackupType.MultiString => RegistryValueKind.MultiString,
        BackupType.Binary => RegistryValueKind.Binary,
        _ => RegistryValueKind.String
    };

    private static BackupType ParseBackupType(string s) =>
        Enum.TryParse<BackupType>(s, ignoreCase: true, out var t) && Enum.IsDefined(t)
            ? t : throw new InvalidDataException("Unsupported backup type: " + s);

    private bool SetServiceStartType(string serviceName, string startType)
    {
        var scArg = startType?.ToLowerInvariant() switch
        {
            "automatic" or "auto" => "auto",
            "automaticdelayed" => "delayed-auto",
            "manual" => "demand",
            "disabled" => "disabled",
            "boot" => "boot",
            "system" => "system",
            _ => throw new InvalidOperationException("Unknown saved service start type")
        };

        if (ServiceHelper.IsProtected(serviceName) && scArg == "disabled") return false;
        return _commands.Run("sc.exe", $"config \"{serviceName}\" start= {scArg}", 15000).Success;
    }

    private static string ToRegPath(string hiveKey)
    {
        var sep = hiveKey.IndexOf('\\');
        if (sep <= 0) return hiveKey;
        var hive = hiveKey.Substring(0, sep).ToUpperInvariant();
        var sub = hiveKey.Substring(sep + 1);

        var regHive = hive switch
        {
            "HKLM" => "HKEY_LOCAL_MACHINE",
            "HKCU" => "HKEY_CURRENT_USER",
            "HKCR" => "HKEY_CLASSES_ROOT",
            "HKU" => "HKEY_USERS",
            "HKCC" => "HKEY_CURRENT_CONFIG",
            _ => hive
        };
        return $"{regHive}\\{sub}";
    }

    private static string FormatRegValue(BackupEntry e)
    {
        var type = ParseBackupType(e.Type);
        if (e.OldValue == null) return "\"\"";
        return type switch
        {
            BackupType.DWord => $"dword:{e.OldValue.Value.GetInt32():x8}",
            BackupType.QWord => $"hex(b):{Hex(BitConverter.GetBytes(e.OldValue.Value.GetInt64()))}",
            BackupType.String => $"\"{EscapeRegString(e.OldValue.Value.GetString() ?? "")}\"",
            BackupType.ExpandString => $"hex(2):{Hex(Encoding.Unicode.GetBytes(e.OldValue.Value.GetString() + "\0"))}",
            BackupType.MultiString => $"hex(7):{Hex(Encoding.Unicode.GetBytes(string.Join("\0", e.OldValue.Value.EnumerateArray().Select(x => x.GetString())) + "\0\0"))}",
            BackupType.Binary => $"hex:{Hex(ParseBinary(e.OldValue.Value))}",
            _ => "\"\""
        };
    }

    private static string Hex(byte[] bytes) => string.Join(",", bytes.Select(b => b.ToString("x2")));

    private static string EscapeRegString(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}
