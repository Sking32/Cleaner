using System.Windows;

namespace Cleaner.App.Services;

public static class WindowLayout
{
    // Pure geometry: preferences cannot place the window outside the current primary work area.
    public static WindowPlacement Fit(WindowPlacement? saved, Rect workArea)
    {
        if (workArea.IsEmpty || !double.IsFinite(workArea.Width) || !double.IsFinite(workArea.Height) || workArea.Width <= 0 || workArea.Height <= 0)
            throw new ArgumentException("A finite work area is required.", nameof(workArea));
        var minWidth = Math.Min(1000, workArea.Width);
        var minHeight = Math.Min(640, workArea.Height);
        var width = saved != null && double.IsFinite(saved.Width) ? saved.Width : 1360;
        var height = saved != null && double.IsFinite(saved.Height) ? saved.Height : 860;
        width = Math.Clamp(width, minWidth, workArea.Width);
        height = Math.Clamp(height, minHeight, workArea.Height);
        var left = saved?.Left is double x && double.IsFinite(x) ? x : workArea.Left + (workArea.Width - width) / 2;
        var top = saved?.Top is double y && double.IsFinite(y) ? y : workArea.Top + (workArea.Height - height) / 2;
        return new WindowPlacement { Width = width, Height = height, Left = Math.Clamp(left, workArea.Left, workArea.Right - width),
            Top = Math.Clamp(top, workArea.Top, workArea.Bottom - height), Maximized = saved?.Maximized == true };
    }
}
