using System;
using System.Diagnostics;
using System.Text;

namespace Cleaner.Core.Services;

public readonly record struct CommandResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
    public string Combined => StdOut + "\n" + StdErr;
}

/// <summary>
/// Запускает внешние процессы и захватывает stdout/stderr.
/// </summary>
public static class CommandRunner
{
    public static CommandResult Run(string fileName, string? arguments, int timeoutMs = 60000)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return new CommandResult(-1, "", "fileName is required");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments ?? "",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var p = Process.Start(psi);
            if (p == null) return new CommandResult(-1, "", "process did not start");

            var stdout = new StringBuilder();
            var stderr = new StringBuilder();

            p.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
            p.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

            p.BeginOutputReadLine();
            p.BeginErrorReadLine();

            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                return new CommandResult(-1, stdout.ToString(), stderr.ToString());
            }

            // Гарантированно завершаем async-чтение
            p.WaitForExit();

            return new CommandResult(p.ExitCode, stdout.ToString(), stderr.ToString());
        }
        catch (Exception ex)
        {
            return new CommandResult(-1, "", ex.Message);
        }
    }
}