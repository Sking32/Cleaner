using System;
using System.IO;
using System.Text;

namespace Cleaner.App.Services;

/// <summary>
/// Простой файловый журнал: %LOCALAPPDATA%\Cleaner\logs\cleaner-ГГГГ-ММ-ДД.log.
/// Никогда не бросает исключений — журнал не должен ронять программу.
/// </summary>
public static class AppLog
{
    private static readonly object Gate = new();

    public static string LogDirectory { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "Cleaner", "logs");

    public static void Info(string message) => Write("INFO", message, null);
    public static void Warn(string message) => Write("WARN", message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    /// <summary>Удаляет журналы старше указанного числа дней.</summary>
    public static void PruneOld(int keepDays = 14)
    {
        try
        {
            if (!Directory.Exists(LogDirectory)) return;
            var limit = DateTime.Now.AddDays(-keepDays);
            foreach (var file in Directory.EnumerateFiles(LogDirectory, "cleaner-*.log"))
            {
                try
                {
                    if (File.GetLastWriteTime(file) < limit) File.Delete(file);
                }
                catch { /* не критично */ }
            }
        }
        catch { /* не критично */ }
    }

    private static void Write(string level, string message, Exception? ex)
    {
        try
        {
            lock (Gate)
            {
                Directory.CreateDirectory(LogDirectory);
                var file = Path.Combine(LogDirectory, $"cleaner-{DateTime.Now:yyyy-MM-dd}.log");
                var sb = new StringBuilder();
                sb.Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(" [").Append(level).Append("] ").Append(message);
                if (ex != null) sb.AppendLine().Append(ex);
                sb.AppendLine();
                File.AppendAllText(file, sb.ToString(), Encoding.UTF8);
            }
        }
        catch
        {
            // Журнал необязателен.
        }
    }
}
