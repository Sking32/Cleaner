using System.Runtime.InteropServices;

namespace Cleaner.Core.Services;

public static class KnownFolders
{
    public static string Downloads => Resolve(new Guid("374DE290-123F-4565-9164-39C4925E467B"));
    public static IEnumerable<string> PersonalDirectories()
    {
        foreach (var folder in new[] { Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos })
        {
            var path = Environment.GetFolderPath(folder); if (!string.IsNullOrWhiteSpace(path)) yield return path;
        }
        foreach (var id in new[] { new Guid("374DE290-123F-4565-9164-39C4925E467B"), new Guid("4C5C32FF-BB9D-43B0-BF5A-27D7A8E70C92") })
        { var path = Resolve(id); if (!string.IsNullOrWhiteSpace(path)) yield return path; }
    }
    private static string Resolve(Guid id)
    {
        if (!OperatingSystem.IsWindows()) return "";
        IntPtr memory = IntPtr.Zero;
        try { return SHGetKnownFolderPath(ref id, 0x4000, IntPtr.Zero, out memory) == 0 ? Marshal.PtrToStringUni(memory) ?? "" : ""; }
        finally { if (memory != IntPtr.Zero) Marshal.FreeCoTaskMem(memory); }
    }
    [DllImport("shell32.dll")] private static extern int SHGetKnownFolderPath(ref Guid folder, uint flags, IntPtr token, out IntPtr path);
}
