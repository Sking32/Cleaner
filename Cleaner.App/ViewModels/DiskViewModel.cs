using Cleaner.App.Localization;
using Cleaner.App.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Cleaner.Core.Services;

namespace Cleaner.App.ViewModels;

public partial class DiskViewModel : HelpViewModelBase
{
    protected override string HelpKey => "disk";

    private CancellationTokenSource? _cts;
    private CancellationTokenSource? _treeCts;
    private readonly IQuarantineService _quarantine;
    private readonly ISafetyService _safety;
    private readonly IWhitelistService _whitelist;
    private string? _scannedRoot;
    [ObservableProperty] private bool _isMutating;
    public bool CanSelectDrive => !IsScanning && !IsMutating;
    public bool HasShadowText => !string.IsNullOrWhiteSpace(ShadowText);
    partial void OnShadowTextChanged(string value) => OnPropertyChanged(nameof(HasShadowText));
    partial void OnIsMutatingChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectDrive));
        ScanCommand.NotifyCanExecuteChanged();
        QuarantineFileCommand.NotifyCanExecuteChanged();
        ListShadowsCommand.NotifyCanExecuteChanged();
        DeleteOldShadowsCommand.NotifyCanExecuteChanged();
    }
    private readonly List<LargeFileItem> _allLargeFiles = new();
    private IReadOnlyList<DiskTreeNode> _rootNodes = Array.Empty<DiskTreeNode>();

    public ObservableCollection<BreadcrumbItem> Breadcrumbs { get; } = new();

    public IReadOnlyList<string> FileFilters { get; } = new[]
    {
        Loc.T("ui.disk.filter.all"),
        "> 100 MB",
        "> 1 GB"
    };

    [ObservableProperty] private IReadOnlyList<DiskTreeNode> _treemapNodes = Array.Empty<DiskTreeNode>();
    [ObservableProperty] private bool _isTreemapBusy;
    [ObservableProperty] private DiskTreeNode? _mapSelection;
    public string MapSelectionText => MapSelection == null ? Loc.T("disk.map.selection.hint")
        : MapSelection.Name + " · " + FormatBytes(MapSelection.SizeBytes) + "\n" + MapSelection.Path;
    partial void OnMapSelectionChanged(DiskTreeNode? value) => OnPropertyChanged(nameof(MapSelectionText));
    [RelayCommand(CanExecute = nameof(CanOpenMapLocation))]
    private void OpenMapLocation(DiskTreeNode? node)
    {
        if (!CanOpenMapLocation(node)) return;
        try
        {
            if (!System.IO.File.Exists(node!.Path) && !Directory.Exists(node.Path))
            { StatusText = Loc.T("disk.map.missing"); return; }
            Process.Start(new ProcessStartInfo("explorer.exe") { Arguments = $"/select,\"{node.Path}\"", UseShellExecute = true });
        }
        catch (Exception ex) { StatusText = Loc.T("t.oshibka") + ex.Message; }
    }
    private bool CanOpenMapLocation(DiskTreeNode? node) => !string.IsNullOrWhiteSpace(node?.Path);
    [ObservableProperty] private int _fileFilterIndex;

    public bool HasTreemap => TreemapNodes.Count > 0;
    public bool HasNoTreemap => !HasTreemap;

    partial void OnTreemapNodesChanged(IReadOnlyList<DiskTreeNode> value)
    {
        OnPropertyChanged(nameof(HasTreemap));
        OnPropertyChanged(nameof(HasNoTreemap));
    }

    partial void OnFileFilterIndexChanged(int value) => RefillLargeFiles();

    public ObservableCollection<DriveItem> Drives { get; } = new();
    public ObservableCollection<FolderItem> TopFolders { get; } = new();
    public ObservableCollection<LargeFileItem> LargeFiles { get; } = new();

    [ObservableProperty] private DriveItem? _selectedDrive;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusText = Loc.T("t.vyberite_disk_i_nazhmite_analiz");
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private int _topFilesCount = 30;
    [ObservableProperty] private string _shadowText = "";

    public bool HasResults => TopFolders.Count > 0 || LargeFiles.Count > 0;
    public bool HasNoResults => !HasResults;

    public DiskViewModel(IQuarantineService quarantine, ISafetyService safety, IWhitelistService whitelist)
    {
        _quarantine = quarantine ?? throw new ArgumentNullException(nameof(quarantine));
        _safety = safety;
        _whitelist = whitelist;
        LoadDrives();
    }

    private void LoadDrives()
    {
        Drives.Clear();
        try
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                if (!d.IsReady) continue;
                if (d.DriveType != DriveType.Fixed && d.DriveType != DriveType.Removable) continue;

                Drives.Add(new DriveItem
                {
                    Root = d.RootDirectory.FullName,
                    Label = string.IsNullOrWhiteSpace(d.VolumeLabel) ? "" : $" {d.VolumeLabel}",
                    Type = d.DriveType,
                    TotalBytes = d.TotalSize,
                    FreeBytes = d.TotalFreeSpace
                });
            }
        }
        catch (Exception logEx) { CoreLog.Warn("Диск: ошибка", logEx); }

        SelectedDrive = Drives.FirstOrDefault();
    }

    [RelayCommand(CanExecute = nameof(CanScan))]
    private async Task ScanAsync()
    {
        if (SelectedDrive is null || !CanSelectDrive) return;
        var drive = SelectedDrive;
        _treeCts?.Cancel();
        _scannedRoot = drive.Root;

        _cts = new CancellationTokenSource();
        IsScanning = true;
        TopFolders.Clear();
        LargeFiles.Clear();
        _allLargeFiles.Clear();
        Breadcrumbs.Clear();
        _rootNodes = Array.Empty<DiskTreeNode>();
        TreemapNodes = Array.Empty<DiskTreeNode>();
        SummaryText = "";
        ProgressText = Loc.T("t.skaniruyu");

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasNoResults));
        ScanCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ProgressText = Loc.F("t.faylov", p.FilesProcessed, FormatBytes(p.BytesScanned), Shorten(p.CurrentPath));
            });

            var result = await DiskAnalyzer.ScanAsync(
                drive.Root, TopFilesCount, progress, _cts.Token);

            foreach (var f in result.Folders)
                TopFolders.Add(new FolderItem(f, drive.Root));

            _allLargeFiles.AddRange(result.LargeFiles.Select(f => new LargeFileItem(f)));
            RefillLargeFiles();

            _rootNodes = result.Folders
                .Where(f => f.SizeBytes > 0)
                .Select(f => new DiskTreeNode(
                    f.Name,
                    string.IsNullOrEmpty(f.Path) ? drive.Root : f.Path,
                    f.SizeBytes,
                    !string.IsNullOrEmpty(f.Path)))
                .ToList();
            ShowRoot();

            SummaryText = Loc.F("t.faylov_2", FormatBytes(result.TotalBytes), result.FilesProcessed);
            StatusText = Loc.T("t.analiz_zavershen");
            ProgressText = "";

            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(HasNoResults));
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.T("t.analiz_otmenen");
            ProgressText = "";
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("t.oshibka") + ex.Message;
            ProgressText = "";
        }
        finally
        {
            IsScanning = false;
            _cts?.Dispose();
            _cts = null;
            ScanCommand.NotifyCanExecuteChanged();
            CancelCommand.NotifyCanExecuteChanged();
        }
    }

    private bool CanScan() => CanSelectDrive && SelectedDrive is not null;
    private bool CanMutate() => CanSelectDrive;

    // ===================== Карта диска =====================

    private void ShowRoot()
    {
        Breadcrumbs.Clear();
        if (_scannedRoot is null) return;

        Breadcrumbs.Add(new BreadcrumbItem(_scannedRoot.TrimEnd('\\'), _scannedRoot));
        TreemapNodes = MergeSmall(_rootNodes);
    }

    [RelayCommand]
    private async Task OpenTreemapNodeAsync(DiskTreeNode? node)
    {
        if (node is null || !node.IsDirectory) return;
        await LoadFolderAsync(node.Path, node.Name, push: true);
    }

    [RelayCommand]
    private async Task NavigateBreadcrumbAsync(BreadcrumbItem? item)
    {
        if (item is null) return;

        var index = Breadcrumbs.IndexOf(item);
        if (index < 0) return;

        while (Breadcrumbs.Count > index + 1)
            Breadcrumbs.RemoveAt(Breadcrumbs.Count - 1);

        if (index == 0)
        {
            _treeCts?.Cancel();
            IsTreemapBusy = false;
            TreemapNodes = MergeSmall(_rootNodes);
            return;
        }

        await LoadFolderAsync(item.Path, null, push: false);
    }

    private async Task LoadFolderAsync(string path, string? name, bool push)
    {
        _treeCts?.Cancel();
        _treeCts?.Dispose();
        _treeCts = new CancellationTokenSource();
        var token = _treeCts.Token;

        IsTreemapBusy = true;
        try
        {
            var children = await DiskTree.GetChildrenAsync(path, token);
            if (token.IsCancellationRequested) return;

            if (push && name != null)
                Breadcrumbs.Add(new BreadcrumbItem("› " + name, path));

            TreemapNodes = MergeSmall(children);
        }
        catch (OperationCanceledException)
        {
            // переход отменён новым переходом
        }
        catch (Exception ex)
        {
            CoreLog.Warn("Карта диска: ошибка перехода", ex);
            StatusText = Loc.T("t.oshibka") + ex.Message;
        }
        finally
        {
            if (_treeCts?.Token == token) IsTreemapBusy = false;
        }
    }

    /// <summary>Очень мелкие элементы сливаются в один блок, чтобы карта оставалась читаемой.</summary>
    private static IReadOnlyList<DiskTreeNode> MergeSmall(IReadOnlyList<DiskTreeNode> nodes)
    {
        if (nodes.Count == 0) return nodes;

        var total = nodes.Sum(n => n.SizeBytes);
        var threshold = total * 0.004;

        var big = nodes.Where(n => n.SizeBytes >= threshold).Take(60).ToList();
        var restSize = nodes.Sum(n => n.SizeBytes) - big.Sum(n => n.SizeBytes);

        if (restSize > 0)
            big.Add(new DiskTreeNode(Loc.T("ui.disk.other"), "", restSize, false));

        return big.OrderByDescending(n => n.SizeBytes).ToList();
    }

    // ===================== Крупные файлы =====================

    private void RefillLargeFiles()
    {
        var minBytes = FileFilterIndex switch
        {
            1 => 100L * 1024 * 1024,
            2 => 1024L * 1024 * 1024,
            _ => 0L
        };

        LargeFiles.Clear();
        foreach (var f in _allLargeFiles.Where(x => x.SizeBytes >= minBytes))
            LargeFiles.Add(f);

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasNoResults));
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task QuarantineFileAsync(LargeFileItem? item)
    {
        if (item is null || !CanMutate()) return;
        IsMutating = true;
        try
        {
        var verdict = _safety.Check(item.Path);
        // Large-file analysis can include installed programs and Windows binaries.
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var programs = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var programsX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        bool Under(string root) => !string.IsNullOrEmpty(root) && Path.GetFullPath(item.Path).StartsWith(root.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase);
        if (!verdict.IsSafe || _whitelist.IsWhitelisted(item.Path) || Under(windows) || Under(programs) || Under(programsX86))
        { StatusText = Loc.T("disk.protected"); return; }

        var confirmed = await Dialogs.ConfirmAsync(
            Loc.T("ui.disk.quarantine.title"),
            Loc.F("ui.disk.quarantine.msg", item.Path, item.SizeText),
            danger: true);
        if (!confirmed) return;

        var entry = await Task.Run(() => _quarantine.MoveToQuarantine(item.Path, "disk"));
        if (entry is null)
        {
            StatusText = Loc.F("ui.disk.quarantine.failed", item.Path);
            return;
        }

        _allLargeFiles.Remove(item);
        LargeFiles.Remove(item);
        StatusText = Loc.F("ui.disk.quarantine.done", item.Path);
        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasNoResults));
        }
        catch (Exception ex) { StatusText = Loc.T("t.oshibka") + ex.Message; }
        finally { IsMutating = false; }
    }

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
    }

    private bool CanCancel() => IsScanning;

    partial void OnIsScanningChanged(bool value)
    {
        OnPropertyChanged(nameof(CanSelectDrive));
        QuarantineFileCommand.NotifyCanExecuteChanged();
        ListShadowsCommand.NotifyCanExecuteChanged();
        DeleteOldShadowsCommand.NotifyCanExecuteChanged();
        ScanCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDriveChanged(DriveItem? value)
    {
        ScanCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task ListShadowsAsync()
    {
        if (!CanMutate()) return;
        IsMutating = true;
        StatusText = Loc.T("t.zaprashivayu_tenevye_kopii");
        try
        {
            var result = await Task.Run(() => RunProcess("vssadmin.exe", "list shadows", 30000));
            ShadowText = string.IsNullOrWhiteSpace(result) ? Loc.T("t.net_dannyh") : result;
            StatusText = Loc.T("t.tenevye_kopii_polucheny");
        }
        catch (Exception ex)
        {
            ShadowText = Loc.T("t.oshibka") + ex.Message;
            StatusText = Loc.T("t.oshibka_2");
        }
        finally { IsMutating = false; }
    }

    [RelayCommand(CanExecute = nameof(CanMutate))]
    private async Task DeleteOldShadowsAsync()
    {
        if (!CanMutate()) return;
        IsMutating = true;
        StatusText = Loc.T("t.udalyayu_starye_tenevye_kopii");
        try
        {
            if (!await Dialogs.ConfirmAsync("Cleaner", Loc.T("disk.shadows.confirm"), danger: true)) return;
            if (!await SessionRestorePoint.EnsureAsync(Loc.T("t.pered_ochistkoy"))) return;
            var context = new Cleaner.Core.Services.Cleanup.CleanupContext(_safety, _whitelist, _quarantine);
            var result = await Task.Run(() => new Cleaner.Core.Services.Cleanup.Handlers.ShadowCopyHandler().Run(
                new Cleaner.Core.Data.OperationEntry { Key = "ShadowCopy" }, context));
            StatusText = result.Success ? Loc.T("t.gotovo") : Loc.T("t.oshibka") + string.Join("; ", result.Errors);
            ShadowText = result.Message ?? string.Join("; ", result.Errors);
        }
        catch (Exception ex)
        {
            StatusText = Loc.T("t.oshibka") + ex.Message;
        }
        finally { IsMutating = false; }
    }

    [RelayCommand]
    private void OpenFolder(FolderItem? item)
    {
        if (item is null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{item.Path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception logEx) { CoreLog.Warn("Диск: ошибка", logEx); }
    }

    [RelayCommand]
    private void OpenFileLocation(LargeFileItem? item)
    {
        if (item is null) return;
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{item.Path}\"",
                UseShellExecute = true
            });
        }
        catch (Exception logEx) { CoreLog.Warn("Диск: ошибка", logEx); }
    }

    private static string RunProcess(string fileName, string arguments, int timeoutMs)
    {
        var result = CommandRunner.Run(fileName, arguments, timeoutMs);
        if (!result.Success) throw new InvalidOperationException(result.Combined.Trim());
        return result.Combined;
    }
    private static string Shorten(string path)
    {
        if (string.IsNullOrEmpty(path)) return "";
        return path.Length <= 60 ? path : "..." + path.Substring(path.Length - 57);
    }

    private static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v.ToString("F2", CultureInfo.InvariantCulture)} {units[u]}";
    }
}

public sealed class DriveItem
{
    public string Root { get; init; } = "";
    public string Label { get; init; } = "";
    public DriveType Type { get; init; }
    public long TotalBytes { get; init; }
    public long FreeBytes { get; init; }

    public double UsedPercent => TotalBytes > 0 ? (TotalBytes - FreeBytes) * 100.0 / TotalBytes : 0;

    public string Display => Loc.F("t.svobodno_iz", Root.TrimEnd('\\'), Label, FormatSize(FreeBytes), FormatSize(TotalBytes));
    public string TypeText => Type == DriveType.Removable ? Loc.T("t.semnyy") : Loc.T("t.lokalnyy");

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v.ToString("F1", CultureInfo.InvariantCulture)} {units[u]}";
    }
}

public sealed class FolderItem
{
    public string Name { get; }
    public string Path { get; }
    public string SizeText { get; }
    public long SizeBytes { get; }

    public FolderItem(FolderSizeEntry info, string root)
    {
        Name = info.Name;
        Path = string.IsNullOrEmpty(info.Path) ? root : info.Path;
        SizeBytes = info.SizeBytes;
        SizeText = FormatSize(info.SizeBytes);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v.ToString("F2", CultureInfo.InvariantCulture)} {units[u]}";
    }
}

public sealed class LargeFileItem
{
    public string Path { get; }
    public string SizeText { get; }
    public long SizeBytes { get; }

    public LargeFileItem(LargeFileEntry info)
    {
        Path = info.Path;
        SizeBytes = info.SizeBytes;
        SizeText = FormatSize(info.SizeBytes);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1) { v /= 1024; u++; }
        return $"{v.ToString("F2", CultureInfo.InvariantCulture)} {units[u]}";
    }
}

public sealed record BreadcrumbItem(string Title, string Path);
