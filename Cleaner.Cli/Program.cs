using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

if (args.Length == 3 && args[0] == "--merge-tweaks")
{
    MergeTweaks(args[1], args[2]);
    return;
}

Console.WriteLine("Cleaner CLI");
Console.WriteLine();
Console.WriteLine("Usage:");
Console.WriteLine("  --merge-tweaks <tweaks.json> <actions.json>");
Console.WriteLine();
Console.WriteLine("Planned commands:");
Console.WriteLine("  --preset <soft|std|deep|<name>>");
Console.WriteLine("  --silent, --json, --quiet, --webhook <url>, --log");

static void MergeTweaks(string tweaksPath, string actionsPath)
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
        var key = prop.Key;
        var spec = prop.Value!.AsObject();

        var tweak = tweaks.FirstOrDefault(t => t!["key"]!.GetValue<string>() == key);
        if (tweak is null)
        {
            Console.WriteLine($"  [SKIP] {key} — not found in tweaks.json");
            continue;
        }

        tweak["apply"] = spec["apply"]!.DeepClone();
        tweak["unapply"] = spec["unapply"]!.DeepClone();
        tweak["state"] = spec["state"]!.DeepClone();
        Console.WriteLine($"  [OK] {key}");
        count++;
    }

    var options = new JsonSerializerOptions { WriteIndented = true };
    File.WriteAllText(tweaksPath, root.ToJsonString(options));

    Console.WriteLine();
    Console.WriteLine($"Merged {count} tweaks into {tweaksPath}");
}