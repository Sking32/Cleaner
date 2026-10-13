using Cleaner.Core.Registry;
using Microsoft.Win32;

namespace Cleaner.Core.Services;

public interface IStartupRegistration
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

/// <summary>Only this application's current-user Run value, changed on explicit request.</summary>
public sealed class StartupRegistration : IStartupRegistration
{
    internal const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    internal const string ValueName = "Cleaner.App";
    private readonly IRegistryAccess _registry;
    private readonly string _executable;
    public StartupRegistration(IRegistryAccess registry, string executable)
    { _registry = registry; _executable = executable; }
    public bool IsEnabled => _registry.GetValue("HKCU", Key, ValueName) is string value && value == Command;
    private string Command => "\"" + _executable + "\" --startup";
    public void SetEnabled(bool enabled)
    {
        if (!enabled)
        {
            _registry.DeleteValue("HKCU", Key, ValueName);
            if (_registry.GetValue("HKCU", Key, ValueName) != null) throw new IOException("startup.registration.failed");
            return;
        }
        if (!Path.IsPathFullyQualified(_executable) || !Path.GetFileName(_executable).Equals("Cleaner.App.exe", StringComparison.OrdinalIgnoreCase)
            || _executable.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0 || Command.Length > 260)
            throw new InvalidOperationException("startup.registration.invalid");
        _registry.SetValue("HKCU", Key, ValueName, Command, RegistryValueKind.String, createKeyIfMissing: true);
        if (!IsEnabled) throw new IOException("startup.registration.failed");
    }
}
