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
    TooFresh
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

        if (extraForbidden != null) list.AddRange(extraForbidden);

        ForbiddenPaths = list
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Normalize)
            .Distinct()
            .ToArray();
    }

    public SafetyResult Check(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return new SafetyResult(SafetyVerdict.Forbidden, "empty path");

        var normalized = Normalize(path);

        foreach (var f in ForbiddenPaths)
        {
            if (normalized == f)
                return new SafetyResult(SafetyVerdict.Forbidden, $"forbidden path: {f}");
        }

        if (IsReparsePoint(path))
            return new SafetyResult(SafetyVerdict.ReparsePoint, "reparse point (symlink/junction)");

        if (IsTooFresh(path))
            return new SafetyResult(SafetyVerdict.TooFresh, $"created < {FreshMinutes} min ago");

        return new SafetyResult(SafetyVerdict.Safe);
    }

    internal static string Normalize(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        var p = path.Trim().TrimEnd('\\', '/');
        return p.ToLowerInvariant();
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReparsePoint) != 0;
        }
        catch { return false; }
    }

    private bool IsTooFresh(string path)
    {
        try
        {
            DateTime created = Directory.Exists(path)
                ? Directory.GetCreationTimeUtc(path)
                : File.GetCreationTimeUtc(path);

            if (created == DateTime.MinValue) return false;
            return (DateTime.UtcNow - created).TotalMinutes < FreshMinutes;
        }
        catch { return false; }
    }
}