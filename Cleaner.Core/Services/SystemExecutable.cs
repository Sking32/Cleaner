namespace Cleaner.Core.Services;

/// <summary>Resolves supported Windows utilities without searching the working directory or PATH.</summary>
public static class SystemExecutable
{
    public static string Resolve(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
            throw new ArgumentException("A supported system executable name is required.", nameof(fileName));

        var name = fileName.ToLowerInvariant();
        if (name.EndsWith(".exe", StringComparison.Ordinal)) name = name[..^4];
        var system = Environment.SystemDirectory;
        var path = name switch
        {
            "cmd" or "powercfg" or "sc" or "wsreset" or "cleanmgr" or "dism" or "vssadmin"
                or "wevtutil" or "netsh" or "schtasks" or "shutdown" or "ipconfig"
                => Path.Combine(system, name + ".exe"),
            "powershell" => Path.Combine(system, "WindowsPowerShell", "v1.0", "powershell.exe"),
            "explorer" => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"),
            _ => throw new ArgumentException("Unsupported system executable: " + fileName, nameof(fileName))
        };
        if (!Path.IsPathFullyQualified(path))
            throw new InvalidOperationException("The Windows executable directory could not be resolved.");
        return path;
    }
}
