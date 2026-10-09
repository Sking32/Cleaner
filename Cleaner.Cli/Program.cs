using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Cleaner.Cli;

// =============================================================
//  Точка входа Cleaner.Cli.
//  Сначала — старые merge-tools (для инструментов разработки),
//  затем — runtime-режимы cleanup.
// =============================================================

var options = new JsonSerializerOptions { WriteIndented = true };

var parsed = CliOptions.Parse(args);

// --- merge-tools (совместимость с прошлым поведением) ---
if (parsed.MergeTweaks && parsed.Positional.Count == 2)
{
    MergeTweaks(parsed.Positional[0], parsed.Positional[1]);
    return;
}
if (parsed.MergeStrings && parsed.Positional.Count == 2)
{
    MergeStrings(parsed.Positional[0], parsed.Positional[1]);
    return;
}
if (parsed.MergeOperations && parsed.Positional.Count == 2)
{
    MergeOperations(parsed.Positional[0], parsed.Positional[1]);
    return;
}

// --- validation ---
var error = parsed.Validate();
if (error != null)
{
    Console.Error.WriteLine(error);
    Console.Error.WriteLine();
    new CliRunner(new OutputWriter(Console.Out, null, quiet: false)).PrintHelp();
    Environment.ExitCode = 64;   // EX_USAGE
    return;
}

// --- runtime ---
using var writer = new OutputWriter(Console.Out, parsed.LogPath, parsed.Quiet);
var runner = new CliRunner(writer);
var exitCode = runner.Run(parsed);
Environment.ExitCode = exitCode;

// =============================================================
//  merge-tools (локальные функции)
// =============================================================

void MergeTweaks(string tweaksPath, string actionsPath)
{
    if (!File.Exists(tweaksPath)) { Console.Error.WriteLine($"Not found: {tweaksPath}"); return; }
    if (!File.Exists(actionsPath)) { Console.Error.WriteLine($"Not found: {actionsPath}"); return; }

    var root = JsonNode.Parse(File.ReadAllText(tweaksPath))!;
    var actionsRoot = JsonNode.Parse(File.ReadAllText(actionsPath))!;
    var tweaks = root["tweaks"]!.AsArray();
    var patch = actionsRoot.AsObject();

    int count = 0;
    foreach (var prop in patch)
    {
        var tweak = tweaks.FirstOrDefault(t => t!["key"]!.GetValue<string>() == prop.Key);
        if (tweak is null) { Console.WriteLine($"  [SKIP] {prop.Key}"); continue; }

        var spec = prop.Value!.AsObject();
        tweak["apply"] = spec["apply"]!.DeepClone();
        tweak["unapply"] = spec["unapply"]!.DeepClone();
        tweak["state"] = spec["state"]!.DeepClone();
        Console.WriteLine($"  [OK] {prop.Key}");
        count++;
    }

    File.WriteAllText(tweaksPath, root.ToJsonString(options));
    Console.WriteLine();
    Console.WriteLine($"Merged {count} tweaks into {tweaksPath}");
}

void MergeStrings(string targetPath, string sourcePath)
{
    if (!File.Exists(targetPath)) { Console.Error.WriteLine($"Not found: {targetPath}"); return; }
    if (!File.Exists(sourcePath)) { Console.Error.WriteLine($"Not found: {sourcePath}"); return; }

    var target = JsonNode.Parse(File.ReadAllText(targetPath))!.AsObject();
    var source = JsonNode.Parse(File.ReadAllText(sourcePath))!.AsObject();

    int added = 0, updated = 0;
    foreach (var prop in source)
    {
        if (target.ContainsKey(prop.Key))
        {
            var oldVal = target[prop.Key]?.GetValue<string>() ?? "";
            var newVal = prop.Value?.GetValue<string>() ?? "";
            if (oldVal != newVal)
            {
                target[prop.Key] = prop.Value?.DeepClone();
                updated++;
            }
        }
        else
        {
            target[prop.Key] = prop.Value?.DeepClone();
            added++;
        }
    }

    File.WriteAllText(targetPath, target.ToJsonString(options));
    Console.WriteLine($"Added: {added}, updated: {updated}, total keys: {target.Count}");
}

void MergeOperations(string metaPath, string actionsPath)
{
    if (!File.Exists(metaPath)) { Console.Error.WriteLine($"Not found: {metaPath}"); return; }
    if (!File.Exists(actionsPath)) { Console.Error.WriteLine($"Not found: {actionsPath}"); return; }

    var meta = JsonNode.Parse(File.ReadAllText(metaPath))!;
    var entries = meta["entries"]!.AsArray();
    var patch = JsonNode.Parse(File.ReadAllText(actionsPath))!.AsObject();

    int count = 0, skipped = 0;
    foreach (var entry in entries)
    {
        if (entry!["kind"]!.GetValue<string>() != "operation") continue;
        var key = entry["key"]!.GetValue<string>();

        if (!patch.ContainsKey(key)) { skipped++; continue; }

        var spec = patch[key]!.AsObject();
        foreach (var field in spec)
        {
            if (field.Value != null)
                entry[field.Key] = field.Value.DeepClone();
        }

        count++;
    }

    File.WriteAllText(metaPath, meta.ToJsonString(options));
    Console.WriteLine();
    Console.WriteLine($"Merged {count} operations, skipped {skipped} (no actions provided)");
}