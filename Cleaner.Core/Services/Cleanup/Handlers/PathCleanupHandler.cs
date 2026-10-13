using System;
using System.Collections.Generic;
using System.IO;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "PathCleanup": удаляет всё содержимое указанных папок
/// (сами папки остаются). Использует SafetyService, WhitelistService,
/// QuarantineService.
/// </summary>
public sealed class PathCleanupHandler : ICleanupHandler
{
    public string Name => "PathCleanup";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        var paths = PathExpander.ExpandAll(op.Paths);
        if (paths.Count == 0)
            return CleanupOperationResult.Ok(message: "no paths");

        long bytes = 0;
        int deleted = 0, skipped = 0;
        var errors = new List<string>();

        foreach (var rawPath in paths)
        {
            if (ctx.IsCancellationRequested) break;

            var path = rawPath;
            var pathCheck = ctx.Safety.Check(path);
            if (!pathCheck.IsSafe || ctx.Whitelist.IsWhitelisted(path))
            { ctx.RecordSkip(op.Key, path, !pathCheck.IsSafe ? "safety" : "whitelist"); skipped++; continue; }
            if (!Directory.Exists(path) && !File.Exists(path))
                continue;

            if (File.Exists(path))
            {
                // Если в paths попал отдельный файл (например, MEMORY.DMP).
                var single = ProcessSingleFile(path, op.Key, ctx, errors);
                bytes += single.bytes;
                deleted += single.deleted;
                skipped += single.skipped;
                continue;
            }

            string[] entries;
            try { entries = Directory.GetFileSystemEntries(path); }
            catch (Exception ex) { errors.Add(path + ": " + ex.Message); skipped++; continue; }
            foreach (var entry in entries)
            {
                if (ctx.IsCancellationRequested) break;

                if (Directory.Exists(entry))
                {
                    var dirResult = ProcessDirectory(entry, op.Key, ctx, errors, deleteSelf: true);
                    bytes += dirResult.bytes;
                    deleted += dirResult.deleted;
                    skipped += dirResult.skipped;
                }
                else
                {
                    var fileResult = ProcessSingleFile(entry, op.Key, ctx, errors);
                    bytes += fileResult.bytes;
                    deleted += fileResult.deleted;
                    skipped += fileResult.skipped;
                }
            }
        }

        ctx.Report(op.Key, Name, null, deleted + skipped, bytes, "done");

        return new CleanupOperationResult
        {
            Success = errors.Count == 0,
            BytesFreed = !ctx.DryRun && ctx.FileMode == CleanupFileMode.PermanentDelete ? bytes : 0, PreviewBytes = ctx.DryRun ? bytes : 0, BytesQuarantined = !ctx.DryRun && ctx.FileMode == CleanupFileMode.Quarantine ? bytes : 0,
            FilesDeleted = ctx.FileMode == CleanupFileMode.PermanentDelete ? deleted : 0, FilesQuarantined = ctx.FileMode == CleanupFileMode.Quarantine ? deleted : 0,
            FilesSkipped = skipped,
            Errors = errors
        };
    }

    // ------------------------------------------------------------------
    //  HELPERS (internal for reuse in MultiPathCleanupHandler)
    // ------------------------------------------------------------------

    internal static IEnumerable<string> EnumerateSafeChildren(string directory)
    {
        // Materialize inside the caller's exception boundary: enumeration is lazy.
        return Directory.GetFileSystemEntries(directory);
    }

    internal static (long bytes, int deleted, int skipped) ProcessSingleFile(
        string path,
        string opKey,
        CleanupContext ctx,
        List<string> errors)
    {
        var check = ctx.Safety.Check(path);
        if (!check.IsSafe)
        {
            ctx.RecordSkip(opKey, path, "safety");
            return (0, 0, 1);
        }
        if (ctx.Whitelist.IsWhitelisted(path))
        {
            ctx.RecordSkip(opKey, path, "whitelist");
            return (0, 0, 1);
        }

        if (ctx.IsCancellationRequested) return (0, 0, 0);
        try
        {
            if (!File.Exists(path)) { ctx.RecordSkip(opKey, path, "missing"); return (0, 0, 1); }
            if (ctx.MinimumFileAge > TimeSpan.Zero &&
                (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < ctx.MinimumFileAge ||
                 DateTime.UtcNow - File.GetCreationTimeUtc(path) < ctx.MinimumFileAge))
            { ctx.RecordSkip(opKey, path, "too_recent"); return (0, 0, 1); }
        }
        catch (Exception ex) { ctx.RecordSkip(opKey, path, FileFailure.Classify(ex), ex.Message); return (0, 0, 1); }

        long size = TryGetFileSize(path);

        if (ctx.DryRun)
        {
            ctx.RecordPreviewFile(opKey, path, size);
            ctx.Report(opKey, "PathCleanup", path, 0, 0, "preview");
            return (size, 0, 0);
        }

        try
        {
            if (ctx.IsCancellationRequested) return (0, 0, 0);
            if (ctx.FileMode == CleanupFileMode.PermanentDelete)
            {
                // No forced unlock, ACL changes, recursive delete or quarantine fallback.
                if (!ctx.Safety.Check(path).IsSafe || ctx.Whitelist.IsWhitelisted(path))
                { ctx.RecordSkip(opKey, path, "safety"); return (0, 0, 1); }
                File.Delete(path);
                ctx.Report(opKey, "PathCleanup", path, 1, size, "deleted");
                return (size, 1, 0);
            }
            var result = ctx.Quarantine.MoveToQuarantineDetailed(path, "Cleanup");
            if (result.Entry != null)
            {
                ctx.Report(opKey, "PathCleanup", path, 1, 0, "quarantine");
                return (size, 1, 0);
            }
            var reason = result.Reason ?? "quarantine_failed";
            ctx.RecordSkip(opKey, path, reason, result.Detail);
            if (!FileFailure.IsExpectedSkip(reason)) errors.Add(path + ": " + reason + (result.Detail == null ? "" : " — " + result.Detail));
        }
        catch (Exception ex)
        {
            var reason = FileFailure.Classify(ex);
            ctx.RecordSkip(opKey, path, reason, ex.Message);
            if (!FileFailure.IsExpectedSkip(reason)) errors.Add(path + ": " + ex.Message);
        }
        return (0, 0, 1);
    }

    internal static (long bytes, int deleted, int skipped) ProcessDirectory(
        string directory,
        string opKey,
        CleanupContext ctx,
        List<string> errors,
        bool deleteSelf = true)
    {
        long totalBytes = 0;
        int totalDeleted = 0, totalSkipped = 0;

        // Безопасность всей папки
        var dirCheck = ctx.Safety.Check(directory);
        if (!dirCheck.IsSafe || ctx.Whitelist.IsWhitelisted(directory))
        {
            ctx.RecordSkip(opKey, directory, !dirCheck.IsSafe ? "safety" : "whitelist");
            // Внутрь не лезем.
            return (0, 0, 1);
        }

        string[] children;
        try { children = Directory.GetFileSystemEntries(directory); }
        catch (Exception ex) { errors.Add(directory + ": " + ex.Message); return (0, 0, 1); }
        foreach (var child in children)
        {
            if (ctx.IsCancellationRequested) break;

            if (Directory.Exists(child))
            {
                var sub = ProcessDirectory(child, opKey, ctx, errors, deleteSelf: true);
                totalBytes += sub.bytes;
                totalDeleted += sub.deleted;
                totalSkipped += sub.skipped;
            }
            else
            {
                var f = ProcessSingleFile(child, opKey, ctx, errors);
                totalBytes += f.bytes;
                totalDeleted += f.deleted;
                totalSkipped += f.skipped;
            }
        }

        if (deleteSelf && !ctx.DryRun && !ctx.IsCancellationRequested && ctx.MinimumFileAge == TimeSpan.Zero)
        {
            try { Directory.Delete(directory, recursive: false); }
            catch { /* папка непустая из-за скипов — ок */ }
        }

        return (totalBytes, totalDeleted, totalSkipped);
    }

    internal static long TryGetFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
