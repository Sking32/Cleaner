using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Cleaner.Core.Services;

internal sealed class WhitelistDocument
{
    [JsonPropertyName("schemaVersion")] public int SchemaVersion { get; set; } = 1;
    [JsonPropertyName("rules")] public List<string> Rules { get; set; } = new();
}

public interface IWhitelistService
{
    /// <summary>Путь к whitelist.json.</summary>
    string FilePath { get; }

    /// <summary>Текущие правила (в порядке добавления).</summary>
    IReadOnlyList<string> Rules { get; }

    /// <summary>Разрешён ли путь правилом белого списка.</summary>
    bool IsWhitelisted(string path);

    /// <summary>Добавить правило. Возвращает false, если такое уже есть.</summary>
    bool Add(string rule);

    /// <summary>Удалить правило (точное совпадение строки).</summary>
    bool Remove(string rule);

    /// <summary>Удалить все правила.</summary>
    void Clear();
}

/// <summary>
/// Белый список исключений для очистки.
/// Хранится в %LOCALAPPDATA%\Cleaner\whitelist.json в виде массива строк.
///
/// Поддерживаются правила трёх видов:
///   1) Полный путь (case-insensitive):  C:\Users\Sking\Documents\important.docx
///   2) Glob с * и ?:                     *.log,  temp_??.tmp,  C:\Projects\**
///   3) Regex с префиксом re:             re:^.*\.bak$
/// </summary>
public sealed class WhitelistService : IWhitelistService
{
    private const string RegexPrefix = "re:";

    private static readonly JsonSerializerOptions ReadOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true
    };

    private static readonly JsonSerializerOptions WriteOpts = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        WriteIndented = true
    };

    private readonly object _lock = new();
    private readonly List<string> _rules = new();

    public string FilePath { get; }

    public IReadOnlyList<string> Rules
    {
        get { lock (_lock) return _rules.ToArray(); }
    }

    public WhitelistService(string? filePath = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData))
                localAppData = Path.Combine(Path.GetTempPath(), "Cleaner");

            var dir = Path.Combine(localAppData, "Cleaner");
            Directory.CreateDirectory(dir);
            filePath = Path.Combine(dir, "whitelist.json");
        }
        else
        {
            var parent = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);
        }

        FilePath = filePath;
        Load();
    }

    // ------------------------------------------------------------------
    //  QUERY
    // ------------------------------------------------------------------

    public bool IsWhitelisted(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;

        var normalized = NormalizePath(path);
        if (normalized.Length == 0) return false;

        string leaf;
        try { leaf = Path.GetFileName(normalized.TrimEnd('\\', '/')); }
        catch { leaf = ""; }

        List<string> snapshot;
        lock (_lock) snapshot = _rules.ToList();

        foreach (var rule in snapshot)
        {
            if (MatchesRule(rule, normalized, leaf))
                return true;
        }
        return false;
    }

    // ------------------------------------------------------------------
    //  EDIT
    // ------------------------------------------------------------------

    public bool Add(string rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return false;
        var trimmed = rule.Trim();

        lock (_lock)
        {
            if (_rules.Contains(trimmed, StringComparer.OrdinalIgnoreCase))
                return false;
            _rules.Add(trimmed);
            Save();
            return true;
        }
    }

    public bool Remove(string rule)
    {
        if (string.IsNullOrWhiteSpace(rule)) return false;

        lock (_lock)
        {
            var idx = _rules.FindIndex(r => string.Equals(r, rule, StringComparison.OrdinalIgnoreCase));
            if (idx < 0) return false;
            _rules.RemoveAt(idx);
            Save();
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            _rules.Clear();
            Save();
        }
    }

    // ------------------------------------------------------------------
    //  MATCHING
    // ------------------------------------------------------------------

    private static bool MatchesRule(string rule, string normalizedPath, string leaf)
    {
        if (string.IsNullOrWhiteSpace(rule)) return false;
        var r = rule.Trim();

        // 1) Regex
        if (r.StartsWith(RegexPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var pattern = r.Substring(RegexPrefix.Length);
            if (pattern.Length == 0) return false;
            try
            {
                return Regex.IsMatch(
                    normalizedPath,
                    pattern,
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100));
            }
            catch { return false; }
        }

        // 2) Glob (есть * или ?)
        if (r.IndexOf('*') >= 0 || r.IndexOf('?') >= 0)
        {
            try
            {
                var regex = GlobToRegex(r);
                return regex.IsMatch(normalizedPath)
                       || (leaf.Length > 0 && regex.IsMatch(leaf));
            }
            catch { return false; }
        }

        // 3) Точный путь
        var normalizedRule = NormalizePath(r);
        if (normalizedRule.Length == 0) return false;
        return string.Equals(normalizedRule, normalizedPath, StringComparison.OrdinalIgnoreCase);
    }

    private static Regex GlobToRegex(string glob)
    {
        var sb = new StringBuilder(glob.Length * 2 + 2);
        sb.Append('^');
        foreach (var c in glob)
        {
            switch (c)
            {
                case '*': sb.Append(".*"); break;
                case '?': sb.Append('.'); break;
                default: sb.Append(Regex.Escape(c.ToString())); break;
            }
        }
        sb.Append('$');
        return new Regex(
            sb.ToString(),
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
            TimeSpan.FromMilliseconds(100));
    }

    private static string NormalizePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";

        var p = path.Trim();
        try
        {
            // Разворачиваем в полный путь (это приводит / к \ на Windows).
            if (Path.IsPathRooted(p))
                p = Path.GetFullPath(p);
        }
        catch { /* оставляем как есть */ }

        p = p.TrimEnd('\\', '/');
        return p.ToLowerInvariant();
    }

    // ------------------------------------------------------------------
    //  PERSISTENCE
    // ------------------------------------------------------------------

    private void Load()
    {
        try
        {
            if (!File.Exists(FilePath)) return;
            var json = File.ReadAllText(FilePath);
            if (string.IsNullOrWhiteSpace(json)) return;

            var doc = JsonSerializer.Deserialize<WhitelistDocument>(json, ReadOpts);
            if (doc?.Rules == null) return;

            lock (_lock)
            {
                _rules.Clear();
                foreach (var r in doc.Rules)
                {
                    if (!string.IsNullOrWhiteSpace(r))
                        _rules.Add(r.Trim());
                }
            }
        }
        catch { }
    }

    private void Save()
    {
        try
        {
            var doc = new WhitelistDocument
            {
                SchemaVersion = 1,
                Rules = _rules.ToList()
            };
            var json = JsonSerializer.Serialize(doc, WriteOpts);

            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, json);
            File.Copy(tmp, FilePath, overwrite: true);
            File.Delete(tmp);
        }
        catch { }
    }
}