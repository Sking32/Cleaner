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

public partial class DiskViewModel : ObservableObject
{
    private CancellationTokenSource? _cts;

    public ObservableCollection<DriveItem> Drives { get; } = new();
    public ObservableCollection<FolderItem> TopFolders { get; } = new();
    public ObservableCollection<LargeFileItem> LargeFiles { get; } = new();

    [ObservableProperty] private DriveItem? _selectedDrive;
    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusText = "Выберите диск и нажмите «Анализ»";
    [ObservableProperty] private string _progressText = "";
    [ObservableProperty] private string _summaryText = "";
    [ObservableProperty] private int _topFilesCount = 30;
    [ObservableProperty] private string _shadowText = "";

    public bool HasResults => TopFolders.Count > 0 || LargeFiles.Count > 0;
    public bool HasNoResults => !HasResults;

    public DiskViewModel()
    {
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
        if (SelectedDrive is null) return;

        _cts = new CancellationTokenSource();
        IsScanning = true;
        TopFolders.Clear();
        LargeFiles.Clear();
        SummaryText = "";
        ProgressText = "Сканирую...";

        OnPropertyChanged(nameof(HasResults));
        OnPropertyChanged(nameof(HasNoResults));
        ScanCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();

        try
        {
            var progress = new Progress<ScanProgress>(p =>
            {
                ProgressText = $"Файлов: {p.FilesProcessed} • {FormatBytes(p.BytesScanned)} • {Shorten(p.CurrentPath)}";
            });

            var result = await DiskAnalyzer.ScanAsync(
                SelectedDrive.Root, TopFilesCount, progress, _cts.Token);

            foreach (var f in result.Folders)
                TopFolders.Add(new FolderItem(f, SelectedDrive.Root));

            foreach (var f in result.LargeFiles)
                LargeFiles.Add(new LargeFileItem(f));

            SummaryText = $"{FormatBytes(result.TotalBytes)} • файлов: {result.FilesProcessed}";
            StatusText = "Анализ завершён";
            ProgressText = "";

            OnPropertyChanged(nameof(HasResults));
            OnPropertyChanged(nameof(HasNoResults));
        }
        catch (OperationCanceledException)
        {
            StatusText = "Анализ отменён";
            ProgressText = "";
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка: " + ex.Message;
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

    private bool CanScan() => !IsScanning && SelectedDrive is not null;

    [RelayCommand(CanExecute = nameof(CanCancel))]
    private void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
    }

    private bool CanCancel() => IsScanning;

    partial void OnIsScanningChanged(bool value)
    {
        ScanCommand.NotifyCanExecuteChanged();
        CancelCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedDriveChanged(DriveItem? value)
    {
        ScanCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task ListShadowsAsync()
    {
        StatusText = "Запрашиваю теневые копии...";
        try
        {
            var result = await Task.Run(() => RunProcess("vssadmin.exe", "list shadows", 30000));
            ShadowText = string.IsNullOrWhiteSpace(result) ? "(нет данных)" : result;
            StatusText = "Теневые копии получены";
        }
        catch (Exception ex)
        {
            ShadowText = "Ошибка: " + ex.Message;
            StatusText = "Ошибка";
        }
    }

    [RelayCommand]
    private async Task DeleteOldShadowsAsync()
    {
        StatusText = "Удаляю старые теневые копии...";
        try
        {
            var result = await Task.Run(() => RunProcess("vssadmin.exe", "delete shadows /for=C: /oldest", 300000));
            StatusText = "Готово (требуются права администратора для полного результата)";
            ShadowText = result;
        }
        catch (Exception ex)
        {
            StatusText = "Ошибка: " + ex.Message;
        }
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
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            using var p = Process.Start(psi);
            if (p == null) return "";
            var output = p.StandardOutput.ReadToEnd();
            var error = p.StandardError.ReadToEnd();
            p.WaitForExit(timeoutMs);
            return string.IsNullOrWhiteSpace(output) ? error : output;
        }
        catch (Exception ex)
        {
            return "Ошибка: " + ex.Message;
        }
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

    public string Display => $"{Root.TrimEnd('\\')}{Label}  —  {FormatSize(FreeBytes)} свободно из {FormatSize(TotalBytes)}";
    public string TypeText => Type == DriveType.Removable ? "Съёмный" : "Локальный";

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

    public LargeFileItem(LargeFileEntry info)
    {
        Path = info.Path;
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