using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cleaner.Core.Services;

public enum SafetyVerdict
{
    Safe,
    Forbidden,
    ReparsePoint,
    TooFresh,
    /// <summary>Не удалось проверить файл (нет доступа, ошибка ввода-вывода) — трогать нельзя.</summary>
    Unverifiable
}

public sealed record SafetyResult(SafetyVerdict Verdict, string? Reason = null)
{
    public bool IsSafe => Verdict == SafetyVerdict.Safe;
}

public interface ISafetyService
{
    SafetyResult Check(string path);
    SafetyResult CheckForCleanup(string path, string operationKey) => Check(path);
    int FreshMinutes { get; }
    IReadOnlyCollection<string> ForbiddenPaths { get; }
}

public sealed class SafetyService : ISafetyService
{
    private readonly IReadOnlyList<string> _forbiddenTrees;
    private readonly IReadOnlyList<string> _explicitTrees;
    private readonly IReadOnlyList<string> _personalTrees;
    private readonly bool _protectPersonalFiles;
    private static readonly HashSet<string> PersonalExtensions = new(StringComparer.OrdinalIgnoreCase)
    { ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".pdf", ".sav", ".save", ".ess", ".skse",
      ".ini", ".cfg", ".config", ".json", ".yaml", ".yml", ".toml", ".db", ".db3", ".sqlite", ".sqlite3", ".kdbx", ".key", ".pem", ".pfx", ".p12" };
    public int FreshMinutes { get; }
    public IReadOnlyCollection<string> ForbiddenPaths { get; }

    public SafetyService(int freshMinutes = 5, IEnumerable<string>? extraForbidden = null, bool protectPersonalFolders = true)
    {
        FreshMinutes = freshMinutes;
        _protectPersonalFiles = protectPersonalFolders;

        var userProfile = Environment.GetEnvironmentVariable("USERPROFILE") ?? @"C:\Users\Default";
        var localAppData = Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Path.Combine(userProfile, "AppData", "Local");
        var appData = Environment.GetEnvironmentVariable("APPDATA") ?? Path.Combine(userProfile, "AppData", "Roaming");
        var programData = Environment.GetEnvironmentVariable("ProgramData") ?? @"C:\ProgramData";

        var list = new List<string>
        {
            @"C:\",
            @"C:\Windows",
            @"C:\Windows\System32",
            @"C:\Program Files",
            @"C:\Program Files (x86)",
            programData,
            @"C:\Users",
            userProfile,
            Path.Combine(userProfile, "Desktop"),
            Path.Combine(userProfile, "Documents"),
            Path.Combine(userProfile, "Downloads"),
            Path.Combine(userProfile, "Pictures"),
            Path.Combine(userProfile, "Videos"),
            Path.Combine(userProfile, "Music"),
            Path.Combine(userProfile, "AppData"),
            localAppData,
            appData
        };
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        list.AddRange(new[] { Path.GetPathRoot(windows) ?? "", windows, Environment.SystemDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) });

        if (extraForbidden != null) list.AddRange(extraForbidden);

        ForbiddenPaths = list
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Normalize)
            .Distinct()
            .ToArray();
        var runtimeCaches = new[] { Path.Combine(Path.GetTempPath(), ".net"), Path.Combine(localAppData, "Temp", ".net"),
            Path.Combine(windows, "Temp", ".net"),
            Environment.GetEnvironmentVariable("DOTNET_BUNDLE_EXTRACT_BASE_DIR") ?? "" }.Where(Path.IsPathFullyQualified);
        _explicitTrees = (extraForbidden ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(Normalize).ToArray();
        _personalTrees = protectPersonalFolders ? KnownFolders.PersonalDirectories().Select(Normalize).ToArray() : [];
        _forbiddenTrees = new[] { Environment.SystemDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }
            .Concat(protectPersonalFolders ? _personalTrees.Concat(runtimeCaches) : Array.Empty<string>())
            .Concat(extraForbidden ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(Normalize).ToArray();
    }

    public SafetyResult Check(string path)
        => CheckCore(path, protectContents: true, checkAge: true);

    public SafetyResult CheckForCleanup(string path, string operationKey)
    {
        var temp = operationKey is "temp_user" or "temp_sys" && IsTemporaryPath(path);
        var cache = IsManagedCachePath(path, operationKey);
        // Scope is still restricted by the catalog and the captured plan. System/personal trees,
        // runtime extraction, links, exclusions and unverifiable objects remain protected.
        return CheckCore(path, protectContents: !temp && !cache, checkAge: !temp, operationKey);
    }

    private static bool IsTemporaryPath(string path)
    {
        var roots = new[] { Path.GetTempPath(), Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Temp"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Temp") };
        var full = Normalize(path);
        return roots.Where(Path.IsPathFullyQualified).Select(Normalize).Any(root => full == root || full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsManagedCachePath(string path, string operationKey)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        string[] roots = operationKey switch {
            "thumbs" => [Path.Combine(local, "Microsoft", "Windows", "Explorer")],
            "fontcache" => [Path.Combine(local, "FontCache"), Path.Combine(windows, "ServiceProfiles", "LocalService", "AppData", "Local", "FontCache")],
            "notifications" => [Path.Combine(local, "Microsoft", "Windows", "Notifications")],
            "activity_history" => [Path.Combine(local, "ConnectedDevicesPlatform")],
            "office" => [Path.Combine(local, "Microsoft", "Office", "16.0", "OfficeFileCache")], _ => [] };
        var full = Normalize(path);
        return roots.Where(Path.IsPathFullyQualified).Select(Normalize).Any(root => full == root || full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsSteamCachePath(string path, string operationKey)
    {
        if (operationKey is not ("steam" or "steam_shader")) return false;
        var suffix = operationKey == "steam" ? @"Steam\appcache\httpcache" : @"Steam\steamapps\shadercache";
        var full = Normalize(path);
        return new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(p => Normalize(Path.Combine(p, suffix)))
            .Any(root => full == root || full.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase));
    }

    private SafetyResult CheckCore(string path, bool protectContents, bool checkAge, string? operationKey = null)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new SafetyResult(SafetyVerdict.Forbidden, "empty path");

        var normalized = Normalize(path);
        if (_explicitTrees.Any(root => normalized == root || normalized.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)))
            return new SafetyResult(SafetyVerdict.Forbidden, "explicitly excluded tree");
        try
        {
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root) && normalized == Normalize(root))
                return new SafetyResult(SafetyVerdict.Forbidden, "drive or share root");
        }
        catch (Exception ex) { return new SafetyResult(SafetyVerdict.Unverifiable, ex.Message); }
        if (_forbiddenTrees.Any(root => (normalized == root || normalized.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) &&
            !(operationKey == "downloads_smart" && _personalTrees.Contains(root) && IsPartialDownload(path)) &&
            !(operationKey != null && IsSteamCachePath(path, operationKey) &&
                new[] { Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }.Any(folder => !string.IsNullOrWhiteSpace(folder) && Normalize(folder) == root))))
            return new SafetyResult(SafetyVerdict.Forbidden, "protected system or excluded tree");

        foreach (var f in ForbiddenPaths)
        {
            if (normalized == f)
                return new SafetyResult(SafetyVerdict.Forbidden, $"forbidden path: {f}");
        }

        try
        {
            var ancestor = Path.GetFullPath(path);
            while (!string.IsNullOrEmpty(ancestor))
            {
                try
                {
                    if ((File.GetAttributes(ancestor) & FileAttributes.ReparsePoint) != 0)
                        return new SafetyResult(SafetyVerdict.ReparsePoint, "reparse point in path");
                }
                catch (FileNotFoundException) { }
                catch (DirectoryNotFoundException) { }
                ancestor = Path.GetDirectoryName(ancestor);
            }
            var attrs = File.GetAttributes(path);
            if (_protectPersonalFiles && protectContents && (attrs & FileAttributes.Directory) == 0 && PersonalExtensions.Contains(Path.GetExtension(path)))
                return new SafetyResult(SafetyVerdict.Forbidden, "document, game save, configuration or credential file");
            if ((attrs & FileAttributes.ReparsePoint) != 0)
                return new SafetyResult(SafetyVerdict.ReparsePoint, "reparse point (symlink/junction)");
            if (((uint)attrs & (0x1000 | 0x40000 | 0x400000)) != 0)
                return new SafetyResult(SafetyVerdict.Unverifiable, "cloud or offline object");

            if (checkAge && IsTooFresh(path))
                return new SafetyResult(SafetyVerdict.TooFresh, $"created < {FreshMinutes} min ago");

            return new SafetyResult(SafetyVerdict.Safe);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            // Объекта уже нет — удалять нечего, опасности нет.
            return new SafetyResult(SafetyVerdict.Safe);
        }
        catch (Exception ex)
        {
            // Не смогли проверить — при сомнении не трогаем.
            return new SafetyResult(SafetyVerdict.Unverifiable, $"cannot verify: {ex.GetType().Name}");
        }
    }

    private static bool IsPartialDownload(string path) => !string.IsNullOrWhiteSpace(KnownFolders.Downloads) &&
        Normalize(Path.GetDirectoryName(path) ?? "") == Normalize(KnownFolders.Downloads) &&
        new[] { ".tmp", ".part", ".crdownload", ".download" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Приводит путь к каноническому виду для сравнения: убирает префикс \\?\,
    /// разворачивает "..", "." и "/" через Path.GetFullPath, срезает хвостовой слэш,
    /// приводит к нижнему регистру.
    /// </summary>
    internal static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";

        var p = path.Trim();

        if (p.StartsWith(@"\\?\", StringComparison.Ordinal))
            p = p.Substring(4);

        // "C:" без слэша — это текущая папка на диске, а не корень; не разворачиваем.
        var isBareDrive = p.Length == 2 && p[1] == ':';
        if (!isBareDrive)
        {
            try { p = Path.GetFullPath(p); }
            catch { /* оставляем как есть */ }
        }

        p = p.TrimEnd('\\', '/');
        return p.ToLowerInvariant();
    }

    private bool IsTooFresh(string path)
    {
        DateTime created = Directory.Exists(path)
            ? Directory.GetCreationTimeUtc(path)
            : File.GetCreationTimeUtc(path);

        if (created == DateTime.MinValue) return false;
        return (DateTime.UtcNow - created).TotalMinutes < FreshMinutes;
    }
}
