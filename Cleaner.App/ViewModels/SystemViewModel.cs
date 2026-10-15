using Cleaner.App.Localization;
using Cleaner.App.Services;
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

public partial class SystemViewModel : HelpViewModelBase
{
    protected override string HelpKey => "system";

    public string FoundText => Loc.F("ui.found_n", FilteredServices.Count);

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
    [ObservableProperty] private int _serviceFilterMode;

    public System.Collections.Generic.IReadOnlyList<string> FilterModes { get; } = new[]
    {
        Loc.T("ui.svcfilter.running"),
        Loc.T("ui.svcfilter.all"),
        Loc.T("ui.svcfilter.stopped"),
        Loc.T("ui.svcfilter.recommended"),
        Loc.T("ui.svcfilter.caution"),
        Loc.T("ui.svcfilter.system")
    };

    public StatusBarViewModel StatusBar { get; }
    [ObservableProperty] private string _statusText = Loc.T("t.gotovo");
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private int _pendingRebootCount;

    public string PendingRebootText => PendingRebootCount == 0
        ? Loc.T("t.vse_izmeneniya_primeneny")
        : Loc.F("t.trebuetsya_perezagruzka_tvikov", PendingRebootCount);

    public bool HasPendingReboot => PendingRebootCount > 0;
    public bool HasServices => FilteredServices.Count > 0;
    public bool HasNoServices => FilteredServices.Count == 0;

    public SystemViewModel(AppliedTweaksStore appliedStore, StatusBarViewModel statusBar)
    {
        _appliedStore = appliedStore ?? throw new ArgumentNullException(nameof(appliedStore));
        StatusBar = statusBar ?? throw new ArgumentNullException(nameof(statusBar));
        UserSettings.Changed += () => { StartServiceCommand.NotifyCanExecuteChanged(); StopServiceCommand.NotifyCanExecuteChanged(); RestartServiceCommand.NotifyCanExecuteChanged(); };

        WindowsVersion = GetWindowsProductName();
        WindowsBuild = GetWindowsBuild();
        MachineName = Environment.MachineName;
        UserName = Environment.UserName;
        ProcessorIdentifier = Environment.GetEnvironmentVariable("PROCESSOR_IDENTIFIER") ?? "—";
        ProcessorCountText = Environment.ProcessorCount + Loc.T("t.yader");
        TotalMemoryText = FormatBytes(GetTotalPhysicalMemory());
        DotNetVersion = Environment.Version.ToString();
        Architecture = Environment.Is64BitOperatingSystem ? "64-bit" : "32-bit";

        _ = RefreshAsync();
    }

    [RelayCommand(CanExecute = nameof(CanRun))]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        try
        {
            await LoadServicesAsync();
            UpdateRebootCount();
        }
        finally { IsBusy = false; }
    }

    private async Task LoadServicesAsync()
    {
        try
        {
            var items = await Task.Run(() =>
            {
                var result = new System.Collections.Generic.List<ServiceItem>();
                foreach (var sc in ServiceController.GetServices())
                {
                    try
                    {
                        result.Add(new ServiceItem
                        {
                            Name = sc.ServiceName,
                            DisplayName = string.IsNullOrWhiteSpace(sc.DisplayName) ? sc.ServiceName : sc.DisplayName,
                            Status = sc.Status.ToString(),
                            StartType = sc.StartType.ToString(),
                            IsRunning = sc.Status == ServiceControllerStatus.Running
                        });
                    }
                    catch (Exception ex) { CoreLog.Warn("Cannot read service: " + sc.ServiceName, ex); }
                    finally { sc.Dispose(); }
                }
                return result.OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
            });
            Services.Clear();
            foreach (var item in items) Services.Add(item);
            ApplyFilter();
        }
        catch (Exception ex) { StatusText = Loc.T("t.oshibka_pri_chtenii_sluzhb") + ex.Message; }
    }
    partial void OnServiceFilterChanged(string value) => ApplyFilter();
    partial void OnServiceFilterModeChanged(int value) => ApplyFilter();

    private void ApplyFilter()
    {
        var f = (ServiceFilter ?? "").Trim();
        var query = Services.AsEnumerable();

        query = ServiceFilterMode switch
        {
            0 => f.Length > 0 ? query : query.Where(s => s.IsRunning),
            2 => query.Where(s => !s.IsRunning),
            3 => query.Where(s => s.AdviceLevelName == "recommended"),
            4 => query.Where(s => s.AdviceLevelName == "caution"),
            5 => query.Where(s => s.AdviceLevelName == "system"),
            _ => query
        };

        if (f.Length > 0)
        {
            query = query.Where(s =>
                s.Name.Contains(f, StringComparison.OrdinalIgnoreCase) ||
                s.DisplayName.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        FilteredServices.Clear();
        foreach (var s in query.Take(500))
            FilteredServices.Add(s);

        OnPropertyChanged(nameof(HasServices));
        OnPropertyChanged(nameof(HasNoServices));
        OnPropertyChanged(nameof(FoundText));
    }

    [RelayCommand(CanExecute = nameof(CanChangeServices))]
    private async Task StartServiceAsync(ServiceItem? item)
    {
        if (item is null || !CanChangeServices() || ServiceHelper.IsProtected(item.Name)) return;
        IsBusy = true;
        try
        {
            var ok = await Task.Run(() => ServiceHelper.StartAndWait(item.Name));
            StatusText = ok
                ? Loc.F("t.zapuscheno", item.DisplayName)
                : Loc.F("t.ne_udalos_zapustit_nuzhny_prava_administrato", item.DisplayName);
            await Task.Delay(600);
            await LoadServicesAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanChangeServices))]
    private async Task StopServiceAsync(ServiceItem? item)
    {
        if (item is null || !CanChangeServices()) return;
        IsBusy = true;
        try
        {
            if (!await ConfirmServiceActionAsync(item, Loc.T("t.ostanovit"))) return;
            var ok = await Task.Run(() => ServiceHelper.StopAndWait(item.Name));
            StatusText = ok
                ? Loc.F("t.ostanovleno", item.DisplayName)
                : Loc.F("t.ne_udalos_ostanovit_nuzhny_prava_administrat", item.DisplayName);
            await Task.Delay(600);
            await LoadServicesAsync();
        }
        finally { IsBusy = false; }
    }

    [RelayCommand(CanExecute = nameof(CanChangeServices))]
    private async Task RestartServiceAsync(ServiceItem? item)
    {
        if (item is null || !CanChangeServices()) return;
        IsBusy = true;
        try
        {
            if (!await ConfirmServiceActionAsync(item, Loc.T("t.perezapustit"))) return;
            var ok = await Task.Run(() => ServiceHelper.StopAndWait(item.Name) && ServiceHelper.StartAndWait(item.Name));
            StatusText = ok
                ? Loc.F("t.perezapuscheno", item.DisplayName)
                : Loc.F("t.ne_udalos_perezapustit", item.DisplayName);
            await Task.Delay(600);
            await LoadServicesAsync();
        }
        finally { IsBusy = false; }
    }

    private Task<bool> ConfirmServiceActionAsync(ServiceItem item, string verb)
    {
        if (!UserSettings.GetExpertMode() || ServiceHelper.IsProtected(item.Name))
        { StatusText = Loc.T("services.protected"); return Task.FromResult(false); }
        return Dialogs.ConfirmAsync(Loc.T("t.podtverzhdenie_izmeneniya"), Loc.F("services.action.warning", verb, item.DisplayName, item.Name), true);
    }

    [RelayCommand]
    private async Task RebootNowAsync()
    {
        var confirmed = await Dialogs.ConfirmAsync(
            Loc.T("t.perezagruzka"),
            Loc.T("t.kompyuter_budet_perezagruzhen_cherez_30_seku"));
        if (!confirmed) return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = SystemExecutable.Resolve("shutdown"),
                Arguments = "/r /t 30 /c \"Cleaner\"",
                CreateNoWindow = true,
                UseShellExecute = false
            });
            StatusText = Loc.T("t.perezagruzka_cherez_30_sekund_otmena_shutdow");
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("t.oshibka") + ex.Message;
        }
    }

    [RelayCommand]
    private void ClearRebootFlags()
    {
        _appliedStore.ClearAllRebootPending();
        UpdateRebootCount();
        StatusText = Loc.T("t.flagi_perezagruzki_sbrosheny");
    }

    private void UpdateRebootCount()
    {
        PendingRebootCount = _appliedStore.PendingRebootCount;
        OnPropertyChanged(nameof(HasPendingReboot));
        OnPropertyChanged(nameof(PendingRebootText));
    }

    private bool CanChangeServices() => !IsBusy && UserSettings.GetExpertMode();
    private bool CanRun() => !IsBusy;
    partial void OnIsBusyChanged(bool value)
    {
        RefreshCommand.NotifyCanExecuteChanged();
        StartServiceCommand.NotifyCanExecuteChanged();
        StopServiceCommand.NotifyCanExecuteChanged();
        RestartServiceCommand.NotifyCanExecuteChanged();
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
    public bool CanStart => !IsRunning;

    private ServiceAdvice Advice => ServiceAdvisor.Advise(Name);

    /// <summary>none / recommended / caution / system — для оформления значка.</summary>
    public string AdviceLevelName => Advice.Level switch
    {
        ServiceAdviceLevel.Recommended => "recommended",
        ServiceAdviceLevel.Caution => "caution",
        ServiceAdviceLevel.System => "system",
        _ => "none"
    };

    public string BadgeText => Advice.Level switch
    {
        ServiceAdviceLevel.Recommended => Loc.T("ui.svc.badge.recommended"),
        ServiceAdviceLevel.Caution => Loc.T("ui.svc.badge.caution"),
        ServiceAdviceLevel.System => Loc.T("ui.svc.badge.system"),
        _ => ""
    };

    public string AdviceNote => Advice.NoteKey != null ? Loc.T(Advice.NoteKey) : "";

    public string StatusText => IsRunning ? Loc.T("t.rabotaet") : Loc.T("t.ostanovlena");
    public string StartTypeRu => StartType switch
    {
        "Automatic" => Loc.T("t.avto"),
        "Manual" => Loc.T("t.vruchnuyu"),
        "Disabled" => Loc.T("t.otklyuchena"),
        "Boot" => Loc.T("t.zagruzka"),
        "System" => Loc.T("t.sistema"),
        _ => StartType
    };
}
