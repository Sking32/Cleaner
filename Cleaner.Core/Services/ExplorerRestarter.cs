using System;
using System.Diagnostics;
using System.Threading;

namespace Cleaner.Core.Services;

/// <summary>
/// Перезапускает explorer.exe (нужно после твиков категории win11).
/// </summary>
public static class ExplorerRestarter
{
    public static void Restart(int waitMsAfterKill = 1000)
    {
        try
        {
            foreach (var p in Process.GetProcessesByName("explorer"))
            {
                try { p.Kill(); } catch { }
            }
        }
        catch { }

        try { Thread.Sleep(waitMsAfterKill); } catch { }

        try { Process.Start("explorer.exe"); } catch { }
    }
}