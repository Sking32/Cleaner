using System.Text.RegularExpressions;
using System.Xml.Linq;
using Cleaner.Core.Models;

namespace Cleaner.Core.Services;

public sealed record CommandRestore(string FileName, string Arguments);

/// <summary>Snapshots for the supported system commands. Unknown mutations fail closed.</summary>
internal static class CommandBackup
{
    public static CommandRestore Desired(TweakAction action)
    {
        var file = Path.GetFileName(action.FileName ?? "").ToLowerInvariant();
        var args = action.Arguments ?? "";
        if (file == "powercfg.exe") args = args.ToLowerInvariant().Replace("scheme_min", "8c5e7fda-e8bf-4a96-9a85-a6e23a8c635c")
            .Replace("scheme_max", "a1841308-3541-4fab-bc81-f71556f20b4a").Replace("scheme_balanced", "381b4222-f694-41f0-9685-ff5bb260df2e");
        var desired = new CommandRestore(file, args);
        if (!IsAllowed(desired)) throw new NotSupportedException("Command state cannot be verified.");
        return desired;
    }
    private static readonly Regex GuidPattern = new(@"[0-9a-fA-F]{8}-(?:[0-9a-fA-F]{4}-){3}[0-9a-fA-F]{12}");

    public static CommandRestore Capture(TweakAction action, ICommandExecutor commands)
    {
        var exe = Path.GetFileName(action.FileName ?? "").ToLowerInvariant();
        var args = action.Arguments ?? "";
        string Read(string file, string arguments)
        {
            var r = commands.Run(file, arguments, 30000);
            if (!r.Success) throw new IOException("Cannot capture original command state: " + r.Combined.Trim());
            return r.StdOut.Trim();
        }
        string Ps(string script) => Read("powershell.exe",
            "-NoProfile -NonInteractive -EncodedCommand " + Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes("$ErrorActionPreference='Stop'; " + script)));

        if (exe == "powercfg.exe" && Regex.IsMatch(args, @"^/setactive ([0-9a-fA-F-]{36}|scheme_min|scheme_max|scheme_balanced)$", RegexOptions.IgnoreCase))
        {
            var match = GuidPattern.Match(Read("powercfg.exe", "/getactivescheme"));
            if (!match.Success) throw new IOException("Cannot read active power plan.");
            return new("powercfg.exe", "/setactive " + match.Value);
        }
        if (exe == "wevtutil.exe" && Regex.IsMatch(args, @"^sl (Application|System) /ms:\d+$", RegexOptions.IgnoreCase))
        {
            var log = args.Split(' ')[1];
            var xml = XDocument.Parse(Read("wevtutil.exe", $"gl {log} /f:xml"));
            var size = xml.Descendants().FirstOrDefault(x => x.Name.LocalName == "maxSize")?.Value;
            if (!long.TryParse(size, out var bytes) || bytes < 1048576) throw new IOException("Invalid original event log size.");
            return new("wevtutil.exe", $"sl {log} /ms:{bytes}");
        }
        if (exe == "netsh.exe" && Regex.IsMatch(args, @"^int tcp set global autotuninglevel=(disabled|highlyrestricted|restricted|normal|experimental)$", RegexOptions.IgnoreCase))
        {
            var level = Ps("(Get-NetTCPSetting -SettingName Internet).AutoTuningLevelLocal.ToString()").ToLowerInvariant();
            var restore = new CommandRestore("netsh.exe", "int tcp set global autotuninglevel=" + level);
            if (!IsAllowed(restore)) throw new IOException("Unsupported TCP autotuning state: " + level);
            return restore;
        }
        if (exe == "dism.exe" && Regex.IsMatch(args, @"^/Online /Set-ReservedStorageState /State:(Enabled|Disabled)$", RegexOptions.IgnoreCase))
        {
            var state = Ps("(Get-WindowsReservedStorageState).ReservedStorageState.ToString()");
            if (state is not ("Enabled" or "Disabled")) throw new IOException("Cannot determine reserved storage state.");
            return new("dism.exe", "/Online /Set-ReservedStorageState /State:" + state);
        }
        if (exe == "schtasks.exe" && IsAllowed(new CommandRestore("schtasks.exe", args)))
        {
            const string task = @"\Microsoft\Windows\Defrag\ScheduledDefrag";
            var xml = XDocument.Parse(Read("schtasks.exe", $"/Query /TN \"{task}\" /XML"));
            var settings = xml.Root?.Elements().FirstOrDefault(x => x.Name.LocalName == "Settings");
            var enabled = settings?.Elements().FirstOrDefault(x => x.Name.LocalName == "Enabled")?.Value ?? "true";
            if (!bool.TryParse(enabled, out var value)) throw new IOException("Cannot determine scheduled task state.");
            return new("schtasks.exe", $"/Change /TN \"{task}\" /{(value ? "Enable" : "Disable")}");
        }
        throw new NotSupportedException("No safe original-state backup for command: " + exe);
    }

    public static bool IsAllowed(CommandRestore r) => r.FileName.ToLowerInvariant() switch
    {
        "powercfg.exe" => Regex.IsMatch(r.Arguments, @"^/setactive [0-9a-fA-F-]{36}$") && Guid.TryParse(r.Arguments[11..], out _),
        "wevtutil.exe" => Regex.IsMatch(r.Arguments, @"^sl (Application|System) /ms:\d+$"),
        "netsh.exe" => Regex.IsMatch(r.Arguments, @"^int tcp set global autotuninglevel=(disabled|highlyrestricted|restricted|normal|experimental)$"),
        "dism.exe" => Regex.IsMatch(r.Arguments, @"^/Online /Set-ReservedStorageState /State:(Enabled|Disabled)$"),
        "schtasks.exe" => r.Arguments == "/Change /TN \"\\Microsoft\\Windows\\Defrag\\ScheduledDefrag\" /Enable"
            || r.Arguments == "/Change /TN \"\\Microsoft\\Windows\\Defrag\\ScheduledDefrag\" /Disable",
        _ => false
    };
}
