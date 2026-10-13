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

    public bool DryRun { get; init; } = true;
    public bool ExpertMode { get; init; }
    private string? ParseError { get; init; }
    public bool Silent { get; init; }
    public bool Quiet { get; init; }
    public bool Json { get; init; }
    public bool NoRelaunch { get; init; }
    public bool ShowHelp { get; init; }
    public bool ListOperations { get; init; }

    public string? LogPath { get; init; }
    public string? WebhookUrl { get; init; }
    public string? ReportPath { get; init; }

    /// <summary>Позиционные аргументы (для merge-tools).</summary>
    public IReadOnlyList<string> Positional { get; init; } = Array.Empty<string>();

    /// <summary>Валидация после парсинга.</summary>
    public string? Validate()
    {
        if (ParseError != null) return ParseError;
        var mergeCount = (MergeTweaks ? 1 : 0) + (MergeStrings ? 1 : 0) + (MergeOperations ? 1 : 0);
        if (mergeCount > 1) return "choose exactly one merge tool";
        if (MergeTweaks || MergeStrings || MergeOperations)
        {
            if (Preset != null || Operations.Count > 0 || ShowHelp || ListOperations || !DryRun || ExpertMode || LogPath != null || WebhookUrl != null || ReportPath != null)
                return "merge tools cannot be mixed with runtime options";
            if (Positional.Count != 2)
                return "merge-tools require exactly 2 positional arguments";
            return null;
        }
        if (Positional.Count > 0) return "unexpected positional arguments";
        if (ReportPath != null && (ShowHelp || ListOperations)) return "--report requires a cleanup run";

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
        bool dryRun = false, apply = false, silent = false, quiet = false, json = false, expert = false;
        string? parseError = null;
        bool noRelaunch = false, help = false, listOps = false;
        bool mergeTweaks = false, mergeStrings = false, mergeOps = false;
        string? logPath = null, webhookUrl = null, reportPath = null;
        var positional = new List<string>();

        for (int i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (string.IsNullOrWhiteSpace(a)) continue;
            if (a is "--preset" or "--operations" or "--ops" or "--log" or "--webhook" or "--report")
            {
                if (i + 1 >= args.Length || string.IsNullOrWhiteSpace(args[i + 1]) || args[i + 1].StartsWith("--"))
                { parseError ??= "missing value for " + a; continue; }
            }

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
                case "--apply": apply = true; break;
                case "--expert": expert = true; break;
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
                case "--report":
                    if (i + 1 < args.Length) reportPath = args[++i];
                    break;

                default:
                    if (a.StartsWith("--", StringComparison.Ordinal))
                    {
                        parseError ??= "unknown option: " + a;
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
            DryRun = !apply,
            ExpertMode = expert,
            ParseError = dryRun && apply ? "--apply and --dry-run cannot be combined" : parseError,
            Silent = silent,
            Quiet = quiet,
            Json = json,
            NoRelaunch = noRelaunch,
            ShowHelp = help,
            ListOperations = listOps,
            LogPath = string.IsNullOrWhiteSpace(logPath) ? null : logPath,
            WebhookUrl = string.IsNullOrWhiteSpace(webhookUrl) ? null : webhookUrl,
            ReportPath = string.IsNullOrWhiteSpace(reportPath) ? null : reportPath,
            Positional = positional
        };
    }
}
