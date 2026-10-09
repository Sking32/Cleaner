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
    Remove
}

public sealed class BackupEntry
{
    [JsonPropertyName("path")] public string Path { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("oldValue")] public JsonElement? OldValue { get; set; }
    [JsonPropertyName("type")] public string Type { get; set; } = "DWord";
    [JsonPropertyName("category")] public string Category { get; set; } = "General";
    [JsonPropertyName("when")] public string When { get; set; } = "";

    [JsonIgnore] public bool IsService => Type == "Service";
}

public interface IBackupService
{
    void SaveBeforeChange(string backupKey, string hive, string subKey, string name, string category = "General");
    void SaveServiceBeforeChange(string backupKey, string serviceName, string oldStartType, string category = "General");
    void SaveRaw(string backupKey, string path, string name, object? oldValue, BackupType type, string category = "General");

    bool RestoreByKey(string backupKey);
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

    public string BackupFilePath { get; }

    public IReadOnlyCollection<string> Keys
    {
        get { lock (_lock) return _entries.Keys.ToArray(); }
    }

    public IReadOnlyDictionary<string, BackupEntry> Entries
    {
        get { lock (_lock) return new Dictionary<string, BackupEntry>(_entries); }
    }

    public BackupService(string? backupFilePath = null)
    {
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
            var oldValue = RegistryHelper.GetValue(hive, subKey, name);
            var kind = RegistryHelper.GetValueKind(hive, subKey, name);

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

            _entries[backupKey] = entry;
            Save();
        }
    }

    public void SaveServiceBeforeChange(string backupKey, string serviceName, string oldStartType, string category = "General")
    {
        if (string.IsNullOrWhiteSpace(backupKey)) throw new ArgumentException("backupKey is required");

        lock (_lock)
        {
            if (_entries.ContainsKey(backupKey)) return;

            _entries[backupKey] = new BackupEntry
            {
                Path = "",
                Name = serviceName,
                OldValue = JsonSerializer.SerializeToElement(oldStartType),
                Type = "Service",
                Category = category,
                When = DateTime.UtcNow.ToString("o")
            };
            Save();
        }
    }

    public void SaveRaw(string backupKey, string path, string name, object? oldValue, BackupType type, string category = "General")
    {
        lock (_lock)
        {
            if (_entries.ContainsKey(backupKey)) return;

            _entries[backupKey] = new BackupEntry
            {
                Path = path,
                Name = name,
                OldValue = oldValue == null ? null : JsonSerializer.SerializeToElement(oldValue),
                Type = type.ToString(),
                Category = category,
                When = DateTime.UtcNow.ToString("o")
            };
            Save();
        }
    }

    // ---------- Restore ----------

    public bool RestoreByKey(string backupKey)
    {
        BackupEntry? entry;
        lock (_lock)
        {
            if (!_entries.TryGetValue(backupKey, out entry)) return false;
        }

        var ok = RestoreEntry(entry);
        if (ok)
        {
            lock (_lock)
            {
                _entries.Remove(backupKey);
                Save();
            }
        }
        return ok;
    }

    public bool RestoreService(string serviceName)
    {
        BackupEntry? entry;
        lock (_lock)
        {
            entry = _entries.Values.FirstOrDefault(e => e.IsService && e.Name == serviceName);
            if (entry == null) return false;
        }

        var ok = RestoreEntry(entry);
        if (ok)
        {
            lock (_lock)
            {
                var key = _entries.First(kv => kv.Value == entry).Key;
                _entries.Remove(key);
                Save();
            }
        }
        return ok;
    }

    public (int Restored, int Failed) RestoreAll()
    {
        BackupEntry[] snapshot;
        lock (_lock) snapshot = _entries.Values.ToArray();

        int ok = 0, fail = 0;
        foreach (var e in snapshot)
        {
            if (RestoreEntry(e)) ok++;
            else fail++;
        }

        if (fail == 0)
        {
            lock (_lock)
            {
                _entries.Clear();
                Save();
            }
        }
        return (ok, fail);
    }

    private static bool RestoreEntry(BackupEntry e)
    {
        try
        {
            if (e.IsService)
            {
                var startType = e.OldValue?.GetString() ?? "Automatic";
                return SetServiceStartType(e.Name, startType);
            }

            if (string.IsNullOrWhiteSpace(e.Path)) return false;

            var sep = e.Path.IndexOf('\\');
            if (sep <= 0) return false;
            var hive = e.Path.Substring(0, sep);
            var subKey = e.Path.Substring(sep + 1);

            if (e.Type == BackupType.Remove.ToString())
            {
                RegistryHelper.DeleteValue(hive, subKey, e.Name);
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

            RegistryHelper.SetValue(hive, subKey, e.Name, value, MapBackupTypeToKind(kind), createKeyIfMissing: true);
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
            _entries.Clear();
            Save();
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
                    if (e.IsService) continue;
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
                    var namePart = string.IsNullOrEmpty(e.Name) ? "@" : $"\"{e.Name}\"";
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
            if (string.IsNullOrWhiteSpace(json)) return;

            var doc = JsonSerializer.Deserialize<Dictionary<string, BackupEntry>>(json);
            if (doc == null) return;

            lock (_lock)
            {
                _entries.Clear();
                foreach (var (k, v) in doc) _entries[k] = v;
            }
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            var tmp = BackupFilePath + ".tmp";
            var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tmp, json);
            File.Copy(tmp, BackupFilePath, overwrite: true);
            File.Delete(tmp);
        }
        catch { }
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
        _ => BackupType.String
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
        Enum.TryParse<BackupType>(s, ignoreCase: true, out var t) ? t : BackupType.String;

    private static bool SetServiceStartType(string serviceName, string startType)
    {
        var scArg = startType?.ToLowerInvariant() switch
        {
            "automatic" or "auto" => "auto",
            "manual" => "demand",
            "disabled" => "disabled",
            _ => "auto"
        };

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "sc.exe",
                Arguments = $"config \"{serviceName}\" start= {scArg}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null) return false;
            p.WaitForExit(5000);
            return p.ExitCode == 0;
        }
        catch { return false; }
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
            BackupType.QWord => $"hex(b):{string.Concat(BitConverter.GetBytes(e.OldValue.Value.GetInt64()).Select(b => b.ToString("x2")))}",
            BackupType.String => $"\"{EscapeRegString(e.OldValue.Value.GetString() ?? "")}\"",
            BackupType.ExpandString => $"hex(2):{string.Concat(Encoding.Unicode.GetBytes(e.OldValue.Value.GetString() + "\0").Select(b => b.ToString("x2")))}",
            BackupType.MultiString => $"hex(7):{string.Concat(Encoding.Unicode.GetBytes(string.Join("\0", e.OldValue.Value.EnumerateArray().Select(x => x.GetString())) + "\0\0").Select(b => b.ToString("x2")))}",
            BackupType.Binary => $"hex:{string.Concat(ParseBinary(e.OldValue.Value).Select(b => b.ToString("x2")))}",
            _ => "\"\""
        };
    }

    private static string EscapeRegString(string s) =>
        s.Replace("\\", "\\\\").Replace("\"", "\\\"");
}