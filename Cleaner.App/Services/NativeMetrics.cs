using System;
using System.Runtime.InteropServices;

namespace Cleaner.App.Services;

/// <summary>Загрузка процессора и памяти без внешних библиотек (kernel32).</summary>
internal static class NativeMetrics
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FileTime
    {
        public uint Low;
        public uint High;
        public ulong Value => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    private static ulong _lastIdle;
    private static ulong _lastTotal;
    private static bool _hasLast;

    /// <summary>Загрузка CPU в процентах (0–100) с момента прошлого вызова.</summary>
    public static double GetCpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;

        var idleValue = idle.Value;
        var total = kernel.Value + user.Value; // kernel уже включает idle

        if (!_hasLast)
        {
            _lastIdle = idleValue;
            _lastTotal = total;
            _hasLast = true;
            return 0;
        }

        var deltaIdle = idleValue - _lastIdle;
        var deltaTotal = total - _lastTotal;
        _lastIdle = idleValue;
        _lastTotal = total;

        if (deltaTotal == 0) return 0;
        return Math.Clamp((1.0 - (double)deltaIdle / deltaTotal) * 100.0, 0, 100);
    }

    /// <summary>Занятая и общая физическая память (байты) и процент загрузки.</summary>
    public static (ulong Used, ulong Total, double Percent) GetMemory()
    {
        var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
        if (!GlobalMemoryStatusEx(ref status) || status.TotalPhys == 0) return (0, 0, 0);

        return (status.TotalPhys - status.AvailPhys, status.TotalPhys, status.MemoryLoad);
    }
}
