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
    int FreshMinutes { get; }
    IReadOnlyCollection<string> ForbiddenPaths { get; }
}

public sealed class SafetyService : ISafetyService
{
    private readonly IReadOnlyList<string> _forbiddenTrees;
    public int FreshMinutes { get; }
    public IReadOnlyCollection<string> ForbiddenPaths { get; }

    public SafetyService(int freshMinutes = 5, IEnumerable<string>? extraForbidden = null)
    {
        FreshMinutes = freshMinutes;

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
        _forbiddenTrees = new[] { Environment.SystemDirectory,
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86) }
            .Concat(extraForbidden ?? Array.Empty<string>()).Where(p => !string.IsNullOrWhiteSpace(p)).Select(Normalize).ToArray();
    }

    public SafetyResult Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new SafetyResult(SafetyVerdict.Forbidden, "empty path");

        var normalized = Normalize(path);
        try
        {
            var root = Path.GetPathRoot(path);
            if (!string.IsNullOrEmpty(root) && normalized == Normalize(root))
                return new SafetyResult(SafetyVerdict.Forbidden, "drive or share root");
        }
        catch (Exception ex) { return new SafetyResult(SafetyVerdict.Unverifiable, ex.Message); }
        if (_forbiddenTrees.Any(root => normalized == root || normalized.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)))
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
            if ((attrs & FileAttributes.ReparsePoint) != 0)
                return new SafetyResult(SafetyVerdict.ReparsePoint, "reparse point (symlink/junction)");

            if (IsTooFresh(path))
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
