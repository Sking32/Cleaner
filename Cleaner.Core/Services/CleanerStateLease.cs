namespace Cleaner.Core.Services;

/// <summary>Prevents GUI and CLI processes from overwriting each other's recovery state.</summary>
public sealed class CleanerStateLease : IDisposable
{
    private readonly Mutex _mutex;
    public CleanerStateLease()
    {
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Cannot determine user identity.");
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(identity)));
        _mutex = new Mutex(false, @"Local\Cleaner.State." + hash);
        bool acquired;
        try { acquired = _mutex.WaitOne(0); }
        catch (AbandonedMutexException) { acquired = true; }
        if (!acquired) { _mutex.Dispose(); throw new IOException("Another Cleaner process is using recovery data. Close it before continuing."); }
    }
    public void Dispose() { _mutex.ReleaseMutex(); _mutex.Dispose(); }
}
