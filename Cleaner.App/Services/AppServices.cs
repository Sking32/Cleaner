using System;

namespace Cleaner.App.Services;

public static class AppServices
{
    public static IServiceProvider? Provider { get; set; }

    public static T GetRequired<T>() where T : notnull
    {
        if (Provider is null)
            throw new InvalidOperationException("AppServices.Provider is not initialized");

        var svc = Provider.GetService(typeof(T));
        if (svc is T t) return t;

        throw new InvalidOperationException($"Service not found: {typeof(T).FullName}");
    }
}