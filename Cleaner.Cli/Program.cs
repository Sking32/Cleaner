using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

var options = new JsonSerializerOptions { WriteIndented = true };

if (args.Length >= 1)
{
    switch (args[0])
    {
        case "--merge-tweaks" when args.Length == 3:
            MergeTweaks(args[1], args[2]);
            return;

        case "--merge-strings" when args.Length == 3:
            MergeStrings(args[1], args[2]);
            return;

        case "--merge-operations" when args.Length == 3:
            MergeOperations(args[1], args[2]);
            return;
    }
}

Console.WriteLine("Cleaner CLI");
Console.WriteLine();
Console.WriteLine("Tools:");
Console.WriteLine("  --merge-tweaks     <tweaks.json>     <actions.json>");
Console.WriteLine("  --merge-strings    <target.json>     <source.json>");
Console.WriteLine("  --merge-operations <meta.json>       <actions.json>");
Console.WriteLine();
Console.WriteLine("Planned runtime commands:");
Console.WriteLine("  --preset <soft|std|deep|<name>>");
Console.WriteLine("  --silent, --json, --quiet, --webhook <url>, --log");

// ---------------------------------------------------------------

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