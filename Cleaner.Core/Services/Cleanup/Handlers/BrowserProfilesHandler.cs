using System;
using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "BrowserProfiles": чистит кэш во всех профилях Chromium-браузера.
/// На входе — путь к "User Data" (Chrome/Edge). Проходим по профилям
/// (Default, Profile 1, ...), для каждого чистим Cache/Code Cache/GPUCache/
/// Service Worker/CacheStorage/Media Cache.
/// Пароли/закладки/историю не трогаем.
/// </summary>
public sealed class BrowserProfilesHandler : ICleanupHandler
{
    public string Name => "BrowserProfiles";

    private static readonly string[] CacheSubdirs =
    {
        "Cache",
        "Code Cache",
        "GPUCache",
        "Media Cache",
        Path.Combine("Service Worker", "CacheStorage"),
        Path.Combine("Service Worker", "ScriptCache")
    };

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        var userDataDirs = PathExpander.ExpandAll(op.Paths);
        if (userDataDirs.Count == 0)
            return CleanupOperationResult.Ok(message: "no paths");

        long bytes = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var userData in userDataDirs)
        {
            if (ctx.IsCancellationRequested) break;
            if (!Directory.Exists(userData)) continue;

            foreach (var profile in EnumerateProfiles(userData))
            {
                if (ctx.IsCancellationRequested) break;

                foreach (var sub in CacheSubdirs)
                {
                    if (ctx.IsCancellationRequested) break;
                    var target = Path.Combine(profile, sub);
                    if (!Directory.Exists(target)) continue;

                    var r = PathCleanupHandler.ProcessDirectory(
                        target, op.Key, ctx, errors, deleteSelf: false);

                    bytes += r.bytes; deleted += r.deleted; skipped += r.skipped;
                }
            }
        }

        ctx.Report(op.Key, Name, null, deleted + skipped, bytes, "done");

        return new CleanupOperationResult
        {
            Success = errors.Count == 0,
            BytesFreed = bytes,
            FilesDeleted = deleted,
            FilesSkipped = skipped,
            Errors = errors
        };
    }

    /// <summary>Профили — подпапки с файлом Preferences (исключает служебные вроде Crashpad).</summary>
    internal static IEnumerable<string> EnumerateProfiles(string userDataDir)
    {
        IEnumerable<string> subs;
        try { subs = Directory.EnumerateDirectories(userDataDir); }
        catch { yield break; }

        foreach (var dir in subs)
        {
            bool isProfile;
            try { isProfile = File.Exists(Path.Combine(dir, "Preferences")); }
            catch { isProfile = false; }
            if (isProfile) yield return dir;
        }
    }
}