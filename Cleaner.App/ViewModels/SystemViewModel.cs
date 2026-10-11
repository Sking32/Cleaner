using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cleaner.Core.Services;
using Microsoft.Win32;

namespace Cleaner.App.ViewModels;

public partial class SystemViewModel : ObservableObject
{
    private readonly AppliedTweaksStore _appliedStore;

    // === Информация о системе ===
    public string WindowsVersion { get; }
    public string WindowsBuild { get; }
    public string MachineName { get; }
    public string UserName { get; }
    public string ProcessorIdentifier { get; }
    public string ProcessorCountText { get; }
    public string TotalMemoryText { get; }
    public string DotNetVersion { get; }
    public string Architecture { get; }

    public ObservableCollection<ServiceItem> Services { get; } = new();
    public ObservableCollection<ServiceItem> FilteredServices { get; } = new();

    [ObservableProperty] private string _serviceFilter = "";
    [ObservableProperty] private string _statusText = "Готово";
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _pendingRebootCount;

    public string PendingRebootText => PendingRebootCount == 0
        ? "Все изменения применены"
        : $"⚠ Требуется перезагрузка: {PendingRebootCount} твиков";

    public bool HasPendingReboot => PendingRebootCount > 0;
    public bool HasServices => FilteredServices.Count > 0;
    public bool HasNoServices => FilteredServices.Count == 0;

    public SystemViewModel(AppliedTweaksStore appliedStore)
    {
        _appliedStore = appliedStore ?? throw new ArgumentNullException(nameof(appliedStore));

        WindowsVersion = GetWindowsProductName();
        WindowsBuild = GetWindowsBuild();
        MachineName = Environment.MachineName;
        UserName = Environment.UserName;
        ProcessorIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "—";
        ProcessorCountText = Environment.ProcessorCount + " ядер";
        TotalMemoryText = FormatBytes(GetTotalPhysicalMemory());
        DotNetVersion = Environment.Version.ToString();
        Architecture = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";

        RefreshAll();
    }

    [RelayCommand]
    private void Refresh() => RefreshAll();

    private void RefreshAll()
    {
        LoadServices();
        UpdateRebootCount();
        StatusText = "Состояние обновлено";
    }

    private void LoadServices()
    {
        Services.Clear();
        try
        {
            var list = ServiceController.GetServices()
                .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var sc in list)
            {
                try
                {
                    Services.Add(new ServiceItem
                    {
                        Name = sc.ServiceName,
                        DisplayName = string.IsNullOrWhiteSpace(sc.DisplayName) ? sc.ServiceName : sc.DisplayName,
                        Status = sc.Status.ToString(),
                        StartType = sc.StartType.ToString(),
                        IsRunning = sc.Status == ServiceControllerStatus.Running
                    });
                }
                catch { }
                finally { sc.Dispose(); }
            }
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка при чтении служб: " + ex.Message;
        }
        ApplyFilter();
    }

    partial void OnServiceFilterChanged(string value) => ApplyFilter();

    private void ApplyFilter()
    {
        var f = (ServiceFilter ?? "").Trim();
        var query = Services.AsEnumerable();

        if (f.Length > 0)
        {
            query = query.Where(s =>
                s.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                s.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase));
        }
        else
        {
            query = query.Where(s => s.IsRunning);
        }

        FilteredServices.Clear();
        foreach (var s in query.Take(500))
            FilteredServices.Add(s);

        OnPropertyChanged(nameof(HasServices));
        OnPropertyChanged(nameof(HasNoServices));
    }

    [RelayCommand]
    private async Task StartServiceAsync(ServiceItem? item)
    {
        if (item is null) return;
        IsBusy = true;
        try
        {
            var ok = await Task.Run(() => ServiceHelper.Start(item.Name));
            StatusText = ok
                ? $"Запущено: {item.DisplayName}"
                : $"Не удалось запустить: {item.DisplayName} (нужны права администратора?)";
            await Task.Delay(600);
            LoadServices();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task StopServiceAsync(ServiceItem? item)
    {
        if (item is null) return;
        if (!ConfirmServiceAction(item, "остановить")) return;
        IsBusy = true;
        try
        {
            var ok = await Task.Run(() => ServiceHelper.Stop(item.Name));
            StatusText = ok
                ? $"Остановлено: {item.DisplayName}"
                : $"Не удалось остановить: {item.DisplayName} (нужны права администратора?)";
            await Task.Delay(600);
            LoadServices();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand]
    private async Task RestartServiceAsync(ServiceItem? item)
    {
        if (item is null) return;
        if (!ConfirmServiceAction(item, "перезапустить")) return;
        IsBusy = true;
        try
        {
            await Task.Run(() => ServiceHelper.Stop(item.Name));
            await Task.Delay(1000);
            var ok = await Task.Run(() => ServiceHelper.Start(item.Name));
            StatusText = ok
                ? $"Перезапущено: {item.DisplayName}"
                : $"Не удалось перезапустить: {item.DisplayName}";
            await Task.Delay(600);
            LoadServices();
        }
        finally { IsBusy = false; }
    }

    private bool ConfirmServiceAction(ServiceItem item, string verb)
    {
        if (ServiceHelper.IsProtected(item.Name))
        {
            StatusText = $"Служба «{item.DisplayName}» защищена: её остановка может нарушить работу Windows";
            System.Windows.MessageBox.Show(
                $"Служба «{item.DisplayName}» относится к ключевым службам Windows.\n\n" +
                "Её остановка или перезапуск могут привести к сбоям системы, поэтому программа этого не позволяет.",
                "Cleaner",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
            return false;
        }

        var answer = System.Windows.MessageBox.Show(
            $"Вы уверены, что хотите {verb} службу «{item.DisplayName}»?\n\n" +
            "Программы и функции Windows, которые от неё зависят, могут перестать работать.",
            "Подтверждение",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        return answer == System.Windows.MessageBoxResult.Yes;
    }

    [RelayCommand]
    private void RebootNow()
    {
        var answer = System.Windows.MessageBox.Show(
            "Компьютер будет перезагружен через 30 секунд.\n\nСохраните открытые документы. Продолжить?",
            "Перезагрузка",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);
        if (answer != System.Windows.MessageBoxResult.Yes) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "shutdown",
                Arguments = "/r /t 30 /c \"Cleaner: перезагрузка через 30 секунд\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            StatusText = "Перезагрузка через 30 секунд. Отмена: shutdown /a";
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка: " + ex.Message;
        }
    }

    [RelayCommand]
    private void ClearRebootFlags()
    {
        _appliedStore.ClearAllRebootPending();
        UpdateRebootCount();
        StatusText = "Флаги перезагрузки сброшены";
    }

    private void UpdateRebootCount()
    {
        PendingRebootCount = _appliedStore.PendingRebootCount;
        OnPropertyChanged(nameof(HasPendingReboot));
        OnPropertyChanged(nameof(PendingRebootText));
    }

    // ============================================================
    //  Helpers
    // ============================================================

    private static string GetWindowsProductName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var product = key?.GetValue("ProductName") as string ?? "Windows";
            var display = key?.GetValue("DisplayVersion") as string;
            return string.IsNullOrEmpty(display) ? product : $"{product} {display}";
        }
        catch { return "Windows"; }
    }

    private static string GetWindowsBuild()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            var build = key?.GetValue("CurrentBuildNumber") as string ?? "—";
            var ubr = key?.GetValue("UBR");
            return ubr is int n ? $"{build}.{n}" : build;
        }
        catch { return "—"; }
    }

    private static ulong GetTotalPhysicalMemory()
    {
        try
        {
            var status = new MEMORYSTATUSEX();
            if (GlobalMemoryStatusEx(status)) return status.ullTotalPhys;
        }
        catch (Exception logEx) { CoreLog.Warn("Система: не удалось определить параметры системы", logEx); }
        return 0;
    }

    private static string FormatBytes(ulong bytes)
    {
        if (bytes == 0) return "—";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v.ToString("F1", CultureInfo.InvariantCulture)} {units[u]}";
    }

    [StructLayout(LayoutKind.Sequential)]
    private class MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;

        public MEMORYSTATUSEX()
        {
            dwLength = (uint)Marshal.SizeOf(typeof(MEMORYSTATUSEX));
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx([In, Out] MEMORYSTATUSEX lpBuffer);
}

public sealed class ServiceItem
{
    public string Name { get; init; } = "";
    public string DisplayName { get; init; } = "";
    public string Status { get; init; } = "";
    public string StartType { get; init; } = "";
    public bool IsRunning { get; init; }

    public string StatusText => IsRunning ? "Работает" : "Остановлена";
    public string StartTypeRu => StartType switch
    {
        "Automatic" => "Авто",
        "Manual" => "Вручную",
        "Disabled" => "Отключена",
        "Boot" => "Загрузка",
        "System" => "Система",
        _ => StartType
    };
}