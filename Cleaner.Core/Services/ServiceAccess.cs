using Cleaner.Core.Registry;

namespace Cleaner.Core.Services;

/// <summary>Startup changes do not force-stop running services. Protected services remain immutable.</summary>
public interface IServiceAccess
{
    string? ReadStartup(string name);
    bool SetStartup(string name, string startup);
}

public sealed class WindowsServiceAccess(IRegistryAccess registry, ICommandExecutor commands) : IServiceAccess
{
    public string? ReadStartup(string name)
    {
        if (!ValidName(name)) return null;
        var type = ServiceHelper.GetStartType(name);
        return type == "Automatic" && registry.GetValue("HKLM", @"SYSTEM\CurrentControlSet\Services\" + name, "DelayedAutoStart") is int delayed && delayed == 1
            ? "AutomaticDelayed" : type;
    }
    public bool SetStartup(string name, string startup)
    {
        if (!ValidName(name) || ServiceHelper.IsProtected(name)) return false;
        var value = startup switch { "Automatic" => "auto", "AutomaticDelayed" => "delayed-auto", "Manual" => "demand", "Disabled" => "disabled", _ => null };
        return value != null && commands.Run("sc.exe", $"config \"{name}\" start= {value}", 15000).Success;
    }
    public static bool ValidName(string name) => !string.IsNullOrWhiteSpace(name) && name.All(c => char.IsAsciiLetterOrDigit(c) || c is '_' or '-' or '.');
}
