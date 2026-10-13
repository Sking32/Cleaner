using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using Cleaner.App.Localization;
using Cleaner.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Cleaner.App.ViewModels;

/// <summary>Живая строка состояния: CPU, ОЗУ (с мини-графиками) и свободное место на системном диске.</summary>
public partial class StatusBarViewModel : ObservableObject
{
    private const int Samples = 40;
    private const double GraphWidth = 64;
    private const double GraphHeight = 16;

    private readonly DispatcherTimer _timer;
    private readonly List<double> _cpu = new();
    private readonly List<double> _ram = new();

    [ObservableProperty] private string _cpuText = "";
    [ObservableProperty] private string _ramText = "";
    [ObservableProperty] private string _diskText = "";
    [ObservableProperty] private string _cpuPercentText = "";
    [ObservableProperty] private string _ramPercentText = "";
    [ObservableProperty] private string _ramDetailText = "";
    [ObservableProperty] private string _diskFreeText = "";
    [ObservableProperty] private string _diskLabelText = "";
    [ObservableProperty] private PointCollection _cpuPoints = new();
    [ObservableProperty] private PointCollection _ramPoints = new();

    public StatusBarViewModel()
    {
        Refresh();

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();
    }

    private void Refresh()
    {
        try
        {
            var cpu = NativeMetrics.GetCpuPercent();
            var (used, total, percent) = NativeMetrics.GetMemory();

            Push(_cpu, cpu);
            Push(_ram, percent);

            CpuText = Loc.F("ui.status.cpu", (int)Math.Round(cpu));
            CpuPercentText = (int)Math.Round(cpu) + "%";
            RamPercentText = (int)Math.Round(percent) + "%";
            RamDetailText = FormatGb(used) + " / " + FormatGb(total) + " GB";
            RamText = Loc.F("ui.status.ram", FormatGb(used), FormatGb(total));
            CpuPoints = Build(_cpu);
            RamPoints = Build(_ram);

            var root = Path.GetPathRoot(Environment.SystemDirectory);
            if (!string.IsNullOrEmpty(root))
            {
                var drive = new DriveInfo(root);
                DiskText = Loc.F("ui.status.disk", FormatGb((ulong)drive.AvailableFreeSpace), root.TrimEnd('\\'));
                DiskFreeText = FormatGb((ulong)drive.AvailableFreeSpace) + " GB";
                DiskLabelText = root.TrimEnd('\\');
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn("Строка состояния: ошибка обновления: " + ex.Message);
        }
    }

    private static void Push(List<double> list, double value)
    {
        list.Add(value);
        if (list.Count > Samples) list.RemoveAt(0);
    }

    private static PointCollection Build(List<double> values)
    {
        var points = new PointCollection();
        var step = GraphWidth / (Samples - 1);
        var offset = Samples - values.Count;

        for (var i = 0; i < values.Count; i++)
        {
            var v = Math.Clamp(values[i], 0, 100);
            points.Add(new Point((offset + i) * step, GraphHeight - v / 100.0 * GraphHeight));
        }

        return points;
    }

    private static string FormatGb(ulong bytes) => (bytes / 1073741824.0).ToString("0.0");
}
