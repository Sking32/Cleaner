using Microsoft.Win32;

namespace Cleaner.App.Services;

internal static class ReportExport
{
    public static string? ChoosePath(string prefix)
    {
        var dialog = new SaveFileDialog
        {
            FileName = prefix + "-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".json",
            Filter = "JSON (*.json)|*.json", DefaultExt = ".json", AddExtension = true,
            OverwritePrompt = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }
}
