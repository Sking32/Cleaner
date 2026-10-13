using System.Reflection;

namespace Cleaner.App.Services;

public static class AppVersion
{
    public static string Current => typeof(AppVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
        .InformationalVersion.Split('+')[0] ?? typeof(AppVersion).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";
}
