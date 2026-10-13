using System.Text;

namespace Cleaner.Core.Services;

/// <summary>Durable replacement; failures propagate to the caller.</summary>
public static class AtomicFile
{
    public static void Write(string path, string content)
        => Write(path, content, (source, target) => File.Replace(source, target, null), Thread.Sleep);

    internal static void Write(string path, string content, Action<string, string> replace, Action<int> wait)
    {
        var full = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        var temp = full + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var preserveTemp = false;
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                var bytes = new UTF8Encoding(false).GetBytes(content);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            if (File.Exists(full))
            {
                for (var attempt = 0; ; attempt++)
                {
                    try { replace(temp, full); break; }
                    catch (IOException ex) when (attempt < 4 && (ex.HResult & 0xffff) is 32 or 33 or 1175 && File.Exists(temp) && File.Exists(full))
                    {
                        // Sharing/locking conflicts and ERROR_UNABLE_TO_REMOVE_REPLACED retain both names.
                        // Do not retry ambiguous partial replacements (1176/1177) or change permissions.
                        wait(40 << attempt);
                    }
                    catch (IOException ex) when ((ex.HResult & 0xffff) is 1176 or 1177 || !File.Exists(full))
                    {
                        // ReplaceFile may remove/rename the original before failing. Keep the flushed replacement for recovery.
                        preserveTemp = true;
                        throw new IOException("Atomic replacement failed; recovery file preserved: " + temp, ex);
                    }
                }
            }
            else File.Move(temp, full);
        }
        finally
        {
            if (!preserveTemp && File.Exists(temp))
            {
                try { File.Delete(temp); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { CoreLog.Warn("Atomic write temporary file retained: " + temp, ex); }
            }
        }
    }
}
