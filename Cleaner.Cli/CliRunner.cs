using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Cleaner.Core.Data;
using Cleaner.Core.Services;
using Cleaner.Core.Services.Cleanup;

namespace Cleaner.Cli;

/// <summary>
/// Собирает сервисы вручную (без DI-контейнера) и выполняет cleanup.
/// </summary>
public sealed class CliRunner
{
    private readonly OutputWriter _out;

    public CliRunner(OutputWriter output)
    {
        _out = output ?? throw new ArgumentNullException(nameof(output));
    }

    // ------------------------------------------------------------------

    public int Run(CliOptions opts)
    {
        if (opts.ShowHelp) { PrintHelp(); return 0; }

        if (opts.ListOperations) { PrintOperations(); return 0; }

        ISafetyService safety;
        IWhitelistService whitelist;
        IQuarantineService quarantine;
        try
        {
            safety = new SafetyService(freshMinutes: 5);
            whitelist = new WhitelistService();
            quarantine = new QuarantineService();
        }
        catch (Exception ex)
        {
            _out.Important($"init failed: {ex.Message}");
            return 2;
        }

        var progress = new Progress<CleanupProgress>(p =>
        {
            if (opts.Quiet || opts.Silent || opts.Json) return;
            if (p.CurrentPath != null)
                _out.Line($"  [{p.OperationKey}] {p.Stage}: {p.CurrentPath}");
        });

        var ctx = new CleanupContext(
            safety, whitelist, quarantine,
            progress: progress,
            dryRun: opts.DryRun);

        CleanupService cleanup;
        PresetsRepository presets;
        try
        {
            presets = new PresetsRepository();
            cleanup = new CleanupService(new OperationsRepository());
        }
        catch (Exception ex)
        {
            _out.Important($"data load failed: {ex.Message}");
            return 2;
        }

        var runner = new CleanupRunner(cleanup, presets);
        CleanupSessionResult result;

        try
        {
            if (opts.Preset != null)
            {
                if (!opts.Quiet && !opts.Json)
                    _out.Line($"Running preset '{opts.Preset}'{(opts.DryRun ? " (dry-run)" : "")}...");

                result = runner.RunPreset(opts.Preset, ctx);
            }
            else
            {
                if (!opts.Quiet && !opts.Json)
                    _out.Line($"Running {opts.Operations.Count} operation(s){(opts.DryRun ? " (dry-run)" : "")}...");

                result = runner.Run(opts.Operations, ctx);
            }
        }
        catch (Exception ex)
        {
            _out.Important($"cleanup failed: {ex.Message}");
            return 3;
        }

        if (opts.Json)
        {
            _out.WriteJson(new
            {
                preset = opts.Preset,
                operations = opts.Operations,
                dryRun = opts.DryRun,
                canceled = result.Canceled,
                bytesFreed = result.BytesFreed,
                filesDeleted = result.FilesDeleted,
                filesSkipped = result.FilesSkipped,
                operationsRun = result.OperationsRun,
                operationsFailed = result.OperationsFailed,
                durationMs = (long)result.Duration.TotalMilliseconds,
                outcomes = result.Outcomes.Select(o => new
                {
                    key = o.Key,
                    success = o.Success,
                    bytesFreed = o.BytesFreed,
                    filesDeleted = o.FilesDeleted,
                    filesSkipped = o.FilesSkipped,
                    errors = o.Errors
                })
            });
        }
        else if (!opts.Quiet)
        {
            _out.Line();
            _out.Line($"Total: {FormatBytes(result.BytesFreed)} " +
                      $"| deleted: {result.FilesDeleted} " +
                      $"| skipped: {result.FilesSkipped} " +
                      $"| failed ops: {result.OperationsFailed} " +
                      $"| time: {result.Duration.TotalSeconds.ToString("F1", CultureInfo.InvariantCulture)}s");
        }

        if (!string.IsNullOrWhiteSpace(opts.WebhookUrl))
        {
            var url = opts.WebhookUrl!;
            try
            {
                var ok = WebhookNotifier.SendAsync(url, result, source: "Cleaner.Cli")
                    .GetAwaiter().GetResult();
                if (!opts.Quiet && !opts.Json)
                    _out.Line(ok ? "Webhook: OK" : "Webhook: FAILED");
            }
            catch { }
        }

        if (result.Canceled) return 130;
        return result.OperationsFailed == 0 ? 0 : 1;
    }

    // ------------------------------------------------------------------

    public void PrintHelp()
    {
        _out.Important("Cleaner CLI");
        _out.Important("");
        _out.Important("Runtime:");
        _out.Important("  --preset <soft|std|deep>       run cleanup preset");
        _out.Important("  --operations <a,b,c>           run specific operations");
        _out.Important("  --list-operations              print all operation keys and exit");
        _out.Important("  --dry-run                      do not modify anything (preview)");
        _out.Important("");
        _out.Important("Output:");
        _out.Important("  --json                         JSON to stdout");
        _out.Important("  --silent                       only final line");
        _out.Important("  --quiet                        no output, only exit code");
        _out.Important("  --log <path>                   append output to file");
        _out.Important("  --webhook <url>                POST JSON result to url");
        _out.Important("");
        _out.Important("Other:");
        _out.Important("  --no-relaunch                  do not relaunch Explorer");
        _out.Important("  --help                         this help");
        _out.Important("");
        _out.Important("Tools:");
        _out.Important("  --merge-tweaks     <tweaks.json>   <actions.json>");
        _out.Important("  --merge-strings    <target.json>   <source.json>");
        _out.Important("  --merge-operations <meta.json>     <actions.json>");
    }

    private void PrintOperations()
    {
        OperationsRepository ops;
        try { ops = new OperationsRepository(); }
        catch (Exception ex)
        {
            _out.Important($"cannot load operations: {ex.Message}");
            return;
        }

        foreach (var op in ops.Operations)
        {
            _out.Important($"{op.Key,-22} {op.Handler,-24} [{op.Level}] {op.Category}");
        }
    }

    /// <summary>
    /// Форматирует байты в человекочитаемый вид.
    /// - 0 → "0 B" (без десятичных).
    /// - Остальные — "X.YZ UNIT" с InvariantCulture (точка, не запятая).
    /// </summary>
    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";

        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v.ToString("F2", CultureInfo.InvariantCulture)} {units[u]}";
    }
}