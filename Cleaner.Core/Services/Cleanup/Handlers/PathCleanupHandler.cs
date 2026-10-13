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
            var pathCheck = ctx.CheckSafety(path, op.Key);
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

            IEnumerable<string> entries;
            try { entries = Directory.EnumerateFileSystemEntries(path); }
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
        if (!ctx.MatchesRule(opKey, path)) return (0, 0, 0);
        var check = ctx.CheckSafety(path, opKey);
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
            var age = ctx.MinimumAgeFor(opKey);
            if (age > TimeSpan.Zero &&
                (DateTime.UtcNow - File.GetLastWriteTimeUtc(path) < age ||
                 DateTime.UtcNow - File.GetCreationTimeUtc(path) < age))
            { ctx.RecordSkip(opKey, path, "too_recent"); return (0, 0, 1); }
        }
        catch (Exception ex) { ctx.RecordSkip(opKey, path, FileFailure.Classify(ex), ex.Message); return (0, 0, 1); }

        long size = TryGetFileSize(path);

        if (ctx.DryRun)
        {
            if (!ctx.TryRecordPreviewFile(opKey, path, size)) return (0, 0, 1);
            if (ctx.CapturePlan) size = ctx.PlannedFiles[^1].Identity.Length;
            ctx.Report(opKey, "PathCleanup", path, 0, 0, "preview");
            return (size, 0, 0);
        }

        try
        {
            if (ctx.IsCancellationRequested) return (0, 0, 0);
            if (ctx.FileMode == CleanupFileMode.PermanentDelete)
            {
                // No forced unlock, ACL changes, recursive delete or quarantine fallback.
                if (!ctx.CheckSafety(path, opKey).IsSafe || ctx.Whitelist.IsWhitelisted(path))
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
        string directory, string opKey, CleanupContext ctx, List<string> errors, bool deleteSelf = true)
    {
        long bytes = 0; int deleted = 0, skipped = 0;
        var pending = new Stack<(string Path, bool Visited, bool DeleteSelf)>();
        pending.Push((directory, false, deleteSelf));
        while (pending.Count > 0 && !ctx.IsCancellationRequested)
        {
            var item = pending.Pop();
            var check = ctx.CheckSafety(item.Path, opKey);
            if (!check.IsSafe || ctx.Whitelist.IsWhitelisted(item.Path))
            { ctx.RecordSkip(opKey, item.Path, !check.IsSafe ? "safety" : "whitelist"); skipped++; continue; }
            if (item.Visited)
            {
                if (item.DeleteSelf && !ctx.DryRun && ctx.MinimumFileAge == TimeSpan.Zero)
                    try { Directory.Delete(item.Path, recursive: false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
                continue;
            }
            pending.Push((item.Path, true, item.DeleteSelf));
            try
            {
                foreach (var child in Directory.EnumerateFileSystemEntries(item.Path))
                {
                    if (ctx.IsCancellationRequested) break;
                    if (Directory.Exists(child)) pending.Push((child, false, true));
                    else
                    {
                        var file = ProcessSingleFile(child, opKey, ctx, errors);
                        bytes += file.bytes; deleted += file.deleted; skipped += file.skipped;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            { errors.Add(item.Path + ": " + ex.Message); skipped++; }
        }
        return (bytes, deleted, skipped);
    }

    internal static long TryGetFileSize(string path)
    {
        try { return new FileInfo(path).Length; }
        catch { return 0; }
    }
}
