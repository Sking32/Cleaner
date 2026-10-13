namespace Cleaner.Core.Services;

/// <summary>Preserve the OS reason instead of guessing from a file name.</summary>
public static class FileFailure
{
    public static string Classify(Exception exception) => exception switch
    {
        FileChangedException => "file_changed",
        UnverifiableFileException => "unverifiable",
        FileNotFoundException or DirectoryNotFoundException => "missing",
        UnauthorizedAccessException => "access_denied",
        IOException when (exception.HResult & 0xffff) is 32 or 33 => "file_in_use",
        IOException when (exception.HResult & 0xffff) is 112 or 39 => "disk_full",
        IOException when (exception.HResult & 0xffff) == 17 => "different_volume",
        _ => "file_operation_failed"
    };
    public static bool IsExpectedSkip(string reason) => reason is "file_in_use" or "access_denied" or "missing" or "file_changed" or "different_volume" or "unverifiable";
}
