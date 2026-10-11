using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cleaner.Core.Services;

public sealed record FolderSizeEntry(string Name, string Path, long SizeBytes);

public sealed record LargeFileEntry(string Path, long SizeBytes);

public readonly record struct ScanProgress(int FilesProcessed, string CurrentPath, long BytesScanned);

public sealed class FolderScanResult
{
    public long TotalBytes { get; }
    public int FilesProcessed { get; }
    public IReadOnlyList<FolderSizeEntry> Folders { get; }
    public IReadOnlyList<LargeFileEntry> LargeFiles { get; }

    public FolderScanResult(long totalBytes, int filesProcessed,
        IReadOnlyList<FolderSizeEntry> folders, IReadOnlyList<LargeFileEntry> largeFiles)
    {
        TotalBytes = totalBytes;
        FilesProcessed = filesProcessed;
        Folders = folders;
        LargeFiles = largeFiles;
    }
}

public static class DiskAnalyzer
{
    public static Task<FolderScanResult> ScanAsync(
        string rootPath,
        int topFiles,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        return Task.Run(() => ScanCore(rootPath, topFiles, progress, ct), ct);
    }

    private static FolderScanResult ScanCore(
        string rootPath,
        int topFiles,
        IProgress<ScanProgress>? progress,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
            return new FolderScanResult(0, 0, Array.Empty<FolderSizeEntry>(), Array.Empty<LargeFileEntry>());

        var folderSizes = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        var folderPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        long totalBytes = 0;
        int filesProcessed = 0;

        var heap = new PriorityQueue<LargeFileEntry, long>();

        try
        {
            foreach (var f in Directory.EnumerateFiles(rootPath))
            {
                ct.ThrowIfCancellationRequested();
                TryCountFile(f, "(файлы в корне)", f, folderSizes, folderPaths,
                    ref totalBytes, ref filesProcessed, topFiles, heap);
            }
        }
        catch { }

        var stack = new Stack<(string path, string topLevel)>();
        try
        {
            foreach (var sub in Directory.EnumerateDirectories(rootPath))
            {
                if (IsReparsePoint(sub)) continue;
                var name = Path.GetFileName(sub);
                folderSizes[name] = 0;
                folderPaths[name] = sub;
                stack.Push((sub, name));
            }
        }
        catch { }

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var (path, top) = stack.Pop();

            try
            {
                foreach (var file in Directory.EnumerateFiles(path))
                {
                    ct.ThrowIfCancellationRequested();
                    TryCountFile(file, top, file, folderSizes, folderPaths,
                        ref totalBytes, ref filesProcessed, topFiles, heap);

                    if (filesProcessed % 200 == 0)
                        progress?.Report(new ScanProgress(filesProcessed, file, totalBytes));
                }
            }
            catch { }

            try
            {
                foreach (var sub in Directory.EnumerateDirectories(path))
                {
                    if (IsReparsePoint(sub)) continue;
                    stack.Push((sub, top));
                }
            }
            catch { }
        }

        progress?.Report(new ScanProgress(filesProcessed, rootPath, totalBytes));

        var folders = folderSizes
            .Where(kv => kv.Value > 0)
            .Select(kv => new FolderSizeEntry(
                kv.Key,
                folderPaths.TryGetValue(kv.Key, out var p) ? p : rootPath,
                kv.Value))
            .OrderByDescending(f => f.SizeBytes)
            .ToList();

        var largeFiles = heap.UnorderedItems
            .Select(x => x.Element)
            .OrderByDescending(f => f.SizeBytes)
            .ToList();

        return new FolderScanResult(totalBytes, filesProcessed, folders, largeFiles);
    }

    private static void TryCountFile(
        string file,
        string topLevel,
        string originalPath,
        Dictionary<string, long> folderSizes,
        Dictionary<string, string> folderPaths,
        ref long totalBytes,
        ref int filesProcessed,
        int topFiles,
        PriorityQueue<LargeFileEntry, long> heap)
    {
        long size;
        try { size = new FileInfo(file).Length; } catch { return; }

        folderSizes[topLevel] = folderSizes.TryGetValue(topLevel, out var v) ? v + size : size;
        totalBytes += size;
        filesProcessed++;

        if (topFiles <= 0) return;

        if (heap.Count < topFiles)
        {
            heap.Enqueue(new LargeFileEntry(originalPath, size), size);
        }
        else if (heap.TryPeek(out _, out var min) && size > min)
        {
            heap.Dequeue();
            heap.Enqueue(new LargeFileEntry(originalPath, size), size);
        }
    }

    private static bool IsReparsePoint(string path)
    {
        try
        {
            var attrs = File.GetAttributes(path);
            return (attrs & FileAttributes.ReparsePoint) != 0;
        }
        catch { return true; }
    }
}