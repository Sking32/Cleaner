using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Cleaner.Core.Services;

public sealed record DiskTreeNode(string Name, string Path, long SizeBytes, bool IsDirectory);

/// <summary>Размеры содержимого одной папки (для карты диска и перехода вглубь).</summary>
public static class DiskTree
{
    public static Task<IReadOnlyList<DiskTreeNode>> GetChildrenAsync(string path, CancellationToken ct)
        => Task.Run(() => GetChildren(path, ct), ct);

    public static IReadOnlyList<DiskTreeNode> GetChildren(string path, CancellationToken ct)
    {
        var result = new List<DiskTreeNode>();
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path)) return result;

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = false,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            var root = new DirectoryInfo(path);

            foreach (var file in root.EnumerateFiles("*", options))
            {
                ct.ThrowIfCancellationRequested();
                if (file.Length > 0)
                    result.Add(new DiskTreeNode(file.Name, file.FullName, file.Length, false));
            }

            foreach (var dir in root.EnumerateDirectories("*", options))
            {
                ct.ThrowIfCancellationRequested();
                var size = DirectorySize(dir, ct);
                if (size > 0)
                    result.Add(new DiskTreeNode(dir.Name, dir.FullName, size, true));
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CoreLog.Warn("Карта диска: не удалось прочитать папку " + path, ex);
        }

        return result.OrderByDescending(n => n.SizeBytes).ToList();
    }

    private static long DirectorySize(DirectoryInfo dir, CancellationToken ct)
    {
        long total = 0;
        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            AttributesToSkip = FileAttributes.ReparsePoint
        };

        try
        {
            foreach (var file in dir.EnumerateFiles("*", options))
            {
                ct.ThrowIfCancellationRequested();
                total += file.Length;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            CoreLog.Warn("Карта диска: ошибка подсчёта размера " + dir.FullName, ex);
        }

        return total;
    }
}
