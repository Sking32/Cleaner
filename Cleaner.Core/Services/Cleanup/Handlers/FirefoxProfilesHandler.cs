using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "FirefoxProfiles": чистит cache2 во всех профилях Firefox.
/// На входе paths пустой — ищем в %APPDATA%\Mozilla\Firefox\Profiles\*.
/// </summary>
public sealed class FirefoxProfilesHandler : ICleanupHandler
{
    public string Name => "FirefoxProfiles";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        var appData = System.Environment.GetFolderPath(
            System.Environment.SpecialFolder.ApplicationData);

        var profilesRoot = Path.Combine(appData, "Mozilla", "Firefox", "Profiles");
        if (!Directory.Exists(profilesRoot))
            return CleanupOperationResult.Ok(message: "no firefox profiles");

        long bytes = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();

        IEnumerable<string> profiles;
        try { profiles = Directory.EnumerateDirectories(profilesRoot); }
        catch { return CleanupOperationResult.Ok(message: "no access"); }

        foreach (var profile in profiles)
        {
            if (ctx.IsCancellationRequested) break;

            var cache2 = Path.Combine(profile, "cache2");
            if (!Directory.Exists(cache2)) continue;

            var r = PathCleanupHandler.ProcessDirectory(
                cache2, op.Key, ctx, errors, deleteSelf: false);

            bytes += r.bytes; deleted += r.deleted; skipped += r.skipped;
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
}