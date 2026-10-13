using System.Runtime.CompilerServices;

namespace Cleaner.Core.Tests;

internal static class IsolatedTestEnvironment
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        // Applies also to direct dotnet test and Visual Studio: isolate this test process only.
        var configured = Environment.GetEnvironmentVariable("CLEANER_TEST_ROOT");
        var root = Path.GetFullPath(configured ?? Path.Combine(AppContext.BaseDirectory, "test-temp", Guid.NewGuid().ToString("N")));
        if (configured != null)
        {
            var project = new DirectoryInfo(AppContext.BaseDirectory);
            while (project != null && !File.Exists(Path.Combine(project.FullName, "Cleaner.slnx"))) project = project.Parent;
            if (project == null || !root.StartsWith(Path.Combine(project.FullName, ".test-sandbox") + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Configured test Temp must be inside the project's .test-sandbox directory.");
        }
        for (var current = root; !string.IsNullOrEmpty(current); current = Path.GetDirectoryName(current))
            if (Directory.Exists(current) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException("Test Temp must not contain junctions or symbolic links.");
        Directory.CreateDirectory(root);
        Environment.SetEnvironmentVariable("TEMP", root);
        Environment.SetEnvironmentVariable("TMP", root);
        var actual = Path.GetFullPath(Path.GetTempPath()).TrimEnd(Path.DirectorySeparatorChar);
        if (!actual.Equals(root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Test Temp isolation could not be established; tests aborted.");
    }
}
