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
public interface ICommandExecutor
{
    CommandResult Run(string fileName, string? arguments, int timeoutMs = 60000,
        CancellationToken cancellationToken = default);
}

public sealed class SystemCommandExecutor : ICommandExecutor
{
    public CommandResult Run(string fileName, string? arguments, int timeoutMs = 60000,
        CancellationToken cancellationToken = default)
        => CommandRunner.Run(fileName, arguments, timeoutMs, cancellationToken);
}

public static class CommandRunner
{
    public static CommandResult Run(string fileName, string? arguments, int timeoutMs = 60000,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            return new CommandResult(-1, "", "fileName is required");

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
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

            var stdout = p.StandardOutput.ReadToEndAsync();
            var stderr = p.StandardError.ReadToEndAsync();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(timeoutMs);
            try { p.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult(); }
            catch (OperationCanceledException)
            {
                try { p.Kill(entireProcessTree: true); } catch { }
                try { p.WaitForExit(5000); } catch { }
                cancellationToken.ThrowIfCancellationRequested();
                return new CommandResult(-1, stdout.IsCompletedSuccessfully ? stdout.Result : "", "Process timed out.");
            }
            // Descendants must not keep redirected pipes open indefinitely.
            if (!Task.WhenAll(stdout, stderr).Wait(TimeSpan.FromSeconds(5)))
                return new CommandResult(-1, "", "Output capture timed out.");
            return new CommandResult(p.ExitCode, stdout.Result, stderr.Result);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            return new CommandResult(-1, "", ex.Message);
        }
    }
}
