using System;
using System.IO;
using System.Text;
using System.Text.Json;

namespace Cleaner.Cli;

/// <summary>
/// Единая точка вывода для CLI: печатает в stdout и/или пишет в лог-файл.
/// </summary>
public sealed class OutputWriter : IDisposable
{
    private readonly TextWriter _stdout;
    private readonly TextWriter? _log;
    private readonly bool _quiet;
    private readonly object _lock = new();
    private bool _disposed;

    public OutputWriter(TextWriter stdout, string? logPath, bool quiet)
    {
        _stdout = stdout ?? throw new ArgumentNullException(nameof(stdout));
        _quiet = quiet;

        if (!string.IsNullOrWhiteSpace(logPath))
        {
            try
            {
                var dir = Path.GetDirectoryName(logPath!);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var fs = new FileStream(logPath!, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                _log = new StreamWriter(fs, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
                {
                    AutoFlush = true
                };
            }
            catch
            {
                _log = null;
            }
        }
    }

    /// <summary>Обычная строка (не выводится при --quiet).</summary>
    public void Line(string text = "")
    {
        lock (_lock)
        {
            if (!_quiet) _stdout.WriteLine(text);
            _log?.WriteLine(text);
        }
    }

    /// <summary>Всегда выводится, даже при --quiet.</summary>
    public void Important(string text)
    {
        lock (_lock)
        {
            _stdout.WriteLine(text);
            _log?.WriteLine(text);
        }
    }

    /// <summary>JSON в stdout (без лог-дублирования).</summary>
    public void WriteJson(object payload)
    {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions
        {
            WriteIndented = true
        });

        lock (_lock)
        {
            _stdout.WriteLine(json);
            _log?.WriteLine(json);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try { _log?.Dispose(); } catch { }
    }
}