using System;
using Cleaner.Core.Data;

namespace Cleaner.Core.Services.Cleanup.Handlers;

/// <summary>
/// Handler "Command": запускает внешний процесс. Не удаляет файлы
/// сам — ответственный за это процесс.
///
/// В op.Command записана строка вида "ipconfig /flushdns",
/// "netsh winsock reset" и т.п. Если это PowerShell-команда
/// (начинается с глагола вроде Clear-, Delete-, Optimize-) —
/// выполняем через powershell.exe -NoProfile -Command.
/// </summary>
public sealed class CommandHandler : ICleanupHandler
{
    public string Name => "Command";

    public CleanupOperationResult Run(OperationEntry op, CleanupContext ctx)
    {
        if (string.IsNullOrWhiteSpace(op.Command))
            return CleanupOperationResult.Ok(message: "no command");

        if (ctx.DryRun)
        {
            ctx.Report(op.Key, Name, op.Command, 0, 0, "preview");
            return CleanupOperationResult.Ok(message: "dry-run");
        }

        var cmd = op.Command!;
        var (fileName, arguments) = SplitCommand(cmd);

        try
        {
            var result = CommandRunner.Run(fileName, arguments, timeoutMs: 600_000);
            ctx.Report(op.Key, Name, cmd, 0, 0, result.Success ? "done" : "failed");

            if (result.Success)
                return CleanupOperationResult.Ok(message: cmd);

            return CleanupOperationResult.Fail(
                $"exit={result.ExitCode}: {result.StdErr.Trim()}".Trim());
        }
        catch (Exception ex)
        {
            return CleanupOperationResult.Fail($"{cmd}: {ex.Message}");
        }
    }

    // ------------------------------------------------------------------

    /// <summary>
    /// Делит строку команды на executable + args.
    /// Если команда похожа на PowerShell (глагол + дефис),
    /// заворачиваем её в powershell.exe.
    /// </summary>
    public static (string fileName, string arguments) SplitCommand(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.Length == 0) return ("cmd.exe", "/c echo nothing");

        // PowerShell-глаголы: Clear-, Delete-, Remove-, Optimize-, Get-, Set-, New-, etc.
        if (LooksLikePowerShell(trimmed))
        {
            var escaped = trimmed.Replace("\"", "\\\"");
            return ("powershell.exe",
                    $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{escaped}\"");
        }

        // Иначе: первый токен — exe, остальное — args.
        int space = trimmed.IndexOf(' ');
        if (space < 0) return (trimmed, "");
        return (trimmed.Substring(0, space), trimmed.Substring(space + 1));
    }

    private static bool LooksLikePowerShell(string s)
    {
        // "Clear-RecycleBin", "Delete-DeliveryOptimizationCache", "Optimize-..."
        // Или явное "powershell " / "pwsh "
        if (s.StartsWith("powershell", StringComparison.OrdinalIgnoreCase)) return true;
        if (s.StartsWith("pwsh", StringComparison.OrdinalIgnoreCase)) return true;

        int dash = s.IndexOf('-');
        if (dash <= 0 || dash > 20) return false;

        var verb = s.Substring(0, dash);
        // Глагол должен быть буквенным и начинаться с заглавной
        if (verb.Length == 0 || !char.IsUpper(verb[0])) return false;
        foreach (var c in verb)
            if (!char.IsLetter(c)) return false;

        return true;
    }
}