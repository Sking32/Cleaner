using System;
using System.Collections.Generic;
using System.Linq;

namespace Cleaner.Cli;

/// <summary>
/// Разобранные аргументы командной строки Cleaner.Cli.
/// </summary>
public sealed class CliOptions
{
    // --- merge-tools (существующее) ---
    public bool MergeTweaks { get; init; }
    public bool MergeStrings { get; init; }
    public bool MergeOperations { get; init; }

    // --- runtime ---
    public string? Preset { get; init; }
    public IReadOnlyList<string> Operations { get; init; } = Array.Empty<string>();

    public bool DryRun { get; init; }
    public bool Silent { get; init; }
    public bool Quiet { get; init; }
    public bool Json { get; init; }
    public bool NoRelaunch { get; init; }
    public bool ShowHelp { get; init; }
    public bool ListOperations { get; init; }

    public string? LogPath { get; init; }
    public string? WebhookUrl { get; init; }

    /// <summary>Позиционные аргументы (для merge-tools).</summary>
    public IReadOnlyList<string> Positional { get; init; } = Array.Empty<string>();

    /// <summary>Валидация после парсинга.</summary>
    public string? Validate()
    {
        if (MergeTweaks || MergeStrings || MergeOperations)
        {
            if (Positional.Count != 2)
                return "merge-tools require exactly 2 positional arguments";
            return null;
        }

        if (Preset != null && Operations.Count > 0)
            return "--preset and --operations cannot be used together";

        if (!ShowHelp && !ListOperations && Preset == null && Operations.Count == 0)
            return "nothing to do: specify --preset, --operations, --list-operations or --help";

        return null;
    }

    // ------------------------------------------------------------------

    /// <summary>Парсит argv в CliOptions. Не бросает — неверные значения складывает в ошибки.</summary>
    public static CliOptions Parse(string[] args)
    {
        if (args == null) return new CliOptions { ShowHelp = true };

        string? preset = null;
        var operations = new List<string>();
        bool dryRun = false, silent = false, quiet = false, json = false;
        bool noRelaunch = false, help = false, listOps = false;
        bool mergeTweaks = false, mergeStrings = false, mergeOps = false;
        string? logPath = null, webhookUrl = null;
        var positional = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (string.IsNullOrWhiteSpace(a)) continue;

            switch (a)
            {
                case "--merge-tweaks": mergeTweaks = true; break;
                case "--merge-strings": mergeStrings = true; break;
                case "--merge-operations": mergeOps = true; break;

                case "--preset":
                    if (i + 1 < args.Length) preset = args[++i];
                    else preset = "";
                    break;

                case "--operations":
                case "--ops":
                    if (i + 1 < args.Length)
                    {
                        var raw = args[++i] ?? "";
                        foreach (var k in raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries))
                        {
                            var t = k.Trim();
                            if (t.Length > 0) operations.Add(t);
                        }
                    }
                    break;

                case "--dry-run": dryRun = true; break;
                case "--silent": silent = true; break;
                case "--quiet": quiet = true; break;
                case "--json": json = true; break;
                case "--no-relaunch": noRelaunch = true; break;
                case "--help":
                case "-h":
                case "/?": help = true; break;
                case "--list-operations": listOps = true; break;

                case "--log":
                    if (i + 1 < args.Length) logPath = args[++i];
                    break;

                case "--webhook":
                    if (i + 1 < args.Length) webhookUrl = args[++i];
                    break;

                default:
                    if (a.StartsWith("--", StringComparison.Ordinal))
                    {
                        // Неизвестный флаг — молча игнорируем,
                        // чтобы не пугать пользователя лишними ошибками.
                    }
                    else
                    {
                        positional.Add(a);
                    }
                    break;
            }
        }

        return new CliOptions
        {
            MergeTweaks = mergeTweaks,
            MergeStrings = mergeStrings,
            MergeOperations = mergeOps,
            Preset = string.IsNullOrWhiteSpace(preset) ? null : preset,
            Operations = operations,
            DryRun = dryRun,
            Silent = silent,
            Quiet = quiet,
            Json = json,
            NoRelaunch = noRelaunch,
            ShowHelp = help,
            ListOperations = listOps,
            LogPath = string.IsNullOrWhiteSpace(logPath) ? null : logPath,
            WebhookUrl = string.IsNullOrWhiteSpace(webhookUrl) ? null : webhookUrl,
            Positional = positional
        };
    }
}