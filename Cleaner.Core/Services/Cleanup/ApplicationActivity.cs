using System.Diagnostics;

namespace Cleaner.Core.Services.Cleanup;

public interface IApplicationActivity { bool IsBusy(string operationKey); }
public sealed class ApplicationActivity : IApplicationActivity
{
    private static readonly Dictionary<string, string[]> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["chrome"] = ["chrome"], ["edge"] = ["msedge"], ["firefox"] = ["firefox"], ["opera"] = ["opera"],
        ["yandex"] = ["browser"], ["teams"] = ["Teams", "ms-teams"], ["discord"] = ["Discord"],
        ["vscode"] = ["Code"], ["office"] = ["WINWORD", "EXCEL", "POWERPNT", "OUTLOOK"],
        ["steam"] = ["steam", "steamwebhelper"], ["steam_shader"] = ["steam"], ["epic"] = ["EpicGamesLauncher"],
        ["spotify"] = ["Spotify"], ["telegram"] = ["Telegram"], ["zoom"] = ["Zoom"], ["slack"] = ["slack"],
        ["notion"] = ["Notion"], ["obs_logs"] = ["obs64", "obs32"], ["onedrive"] = ["OneDrive"],
        ["wupdate"] = ["TiWorker", "TrustedInstaller", "MoUsoCoreWorker", "SetupHost", "WindowsUpdateBox"],
        ["winsxs"] = ["TiWorker", "TrustedInstaller", "SetupHost"], ["downloads_smart"] = ["chrome", "msedge", "firefox", "opera", "browser"]
    };
    public bool IsBusy(string operationKey)
    {
        if (!Names.TryGetValue(operationKey, out var names)) return false;
        try
        {
            foreach (var name in names)
            {
                var processes = Process.GetProcessesByName(name);
                try { if (processes.Length > 0) return true; }
                finally { foreach (var process in processes) process.Dispose(); }
            }
            return false;
        }
        catch { return true; }
    }
}
public sealed class NoApplicationActivity : IApplicationActivity { public bool IsBusy(string operationKey) => false; }
