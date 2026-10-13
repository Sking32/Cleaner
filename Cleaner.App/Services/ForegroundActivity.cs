using System.Runtime.InteropServices;

namespace Cleaner.App.Services;

internal static class ForegroundActivity
{
    // Conservative: pause for any full-screen foreground app, without guessing whether it is a game.
    public static bool IsFullScreen()
    {
        if (!OperatingSystem.IsWindows()) return true;
        try
        {
            var window = GetForegroundWindow();
            if (window == IntPtr.Zero) return false;
            if (window == GetShellWindow() || window == GetDesktopWindow()) return false;
            if (!GetWindowRect(window, out var bounds)) return true;
            var monitor = MonitorFromWindow(window, 2);
            var info = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
            if (!GetMonitorInfo(monitor, ref info)) return true;
            return bounds.Left <= info.Monitor.Left && bounds.Top <= info.Monitor.Top &&
                bounds.Right >= info.Monitor.Right && bounds.Bottom >= info.Monitor.Bottom;
        }
        catch { return true; }
    }
    [StructLayout(LayoutKind.Sequential)] private struct Rect { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MonitorInfo { public int Size; public Rect Monitor, Work; public uint Flags; }
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetShellWindow();
    [DllImport("user32.dll")] private static extern IntPtr GetDesktopWindow();
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr window, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")] private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
