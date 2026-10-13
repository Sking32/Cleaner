using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Cleaner.Core.Services;

public sealed record FileIdentity(uint Volume, ulong Id, long Length, long Created, long Modified, uint Attributes);
public sealed class FileChangedException : IOException { public FileChangedException() : base("File changed after analysis.") { } }
public sealed class UnverifiableFileException(string message) : IOException(message);
public interface IVerifiedFile : IDisposable
{
    FileIdentity Identity { get; }
    void Move(string target);
    void Delete();
}
public interface IFileVerifier { IVerifiedFile Open(string path, bool forMutation = false, FileIdentity? expected = null); }
public sealed class WindowsFileVerifier : IFileVerifier
{
    public IVerifiedFile Open(string path, bool forMutation = false, FileIdentity? expected = null) => new VerifiedFile(path, forMutation, expected);
}

/// <summary>Locks the file and its ancestors against rename; mutations use the verified handle.</summary>
public sealed class VerifiedFile : IVerifiedFile
{
    private readonly SafeFileHandle _file;
    private readonly List<SafeFileHandle> _parents = new();
    public FileIdentity Identity { get; }
    private const uint ReadData = 0x1, ReadAttributes = 0x80, DeleteAccess = 0x10000;
    private const uint UnsafeAttributes = 0x400 | 0x1000 | 0x40000 | 0x400000; // reparse, offline, recall

    public VerifiedFile(string path, bool forMutation = false, FileIdentity? expected = null)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Verified cleanup requires Windows.");
        path = Path.GetFullPath(path);
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0)
            throw new UnverifiableFileException("Network, device paths and alternate streams are excluded.");
        _file = new SafeFileHandle(IntPtr.Zero, true);
        try
        {
            // Root-first locking prevents a parent junction/rename swap during open.
            var ancestors = new Stack<string>();
            for (var parent = Path.GetDirectoryName(path); parent != null; parent = Path.GetDirectoryName(parent)) ancestors.Push(parent);
            foreach (var parent in ancestors)
            {
                var handle = Open(parent, ReadAttributes, 3, 0x02200000);
                _parents.Add(handle);
                var info = Info(handle);
                if ((info.Attributes & UnsafeAttributes) != 0) throw new UnverifiableFileException("Linked or cloud ancestor excluded.");
            }
            _file.Dispose();
            // Attribute-only access bypasses some sharing checks. Request read access without reading
            // content so exclusively opened files are skipped already during analysis.
            _file = Open(path, ReadData | ReadAttributes | (forMutation ? DeleteAccess : 0), 1, 0x00200000);
            var data = Info(_file);
            if ((data.Attributes & (UnsafeAttributes | 0x10)) != 0 || data.Links != 1)
                throw new UnverifiableFileException("Links, directories and cloud objects are excluded.");
            Identity = new(data.Volume, ((ulong)data.IdHigh << 32) | data.IdLow,
                ((long)data.SizeHigh << 32) | data.SizeLow, data.Created, data.Modified, data.Attributes);
            if (expected != null && Identity != expected) throw new FileChangedException();
        }
        catch { Dispose(); throw; }
    }

    public void Move(string target)
    {
        target = Path.GetFullPath(target);
        var ancestors = new Stack<string>();
        for (var parent = Path.GetDirectoryName(target); parent != null; parent = Path.GetDirectoryName(parent)) ancestors.Push(parent);
        foreach (var parent in ancestors)
        {
            var handle = Open(parent, ReadAttributes, 3, 0x02200000); _parents.Add(handle);
            if ((Info(handle).Attributes & UnsafeAttributes) != 0) throw new UnverifiableFileException("Linked or cloud destination excluded.");
        }
        // FILE_RENAME_INFO: BOOL, aligned HANDLE, DWORD byte count, WCHAR name.
        var text = System.Text.Encoding.Unicode.GetBytes(@"\\?\" + target);
        var rootOffset = IntPtr.Size == 8 ? 8 : 4;
        var lengthOffset = rootOffset + IntPtr.Size;
        var nameOffset = lengthOffset + 4;
        var memory = Marshal.AllocHGlobal(nameOffset + text.Length + 2);
        try
        {
            for (var i = 0; i < nameOffset; i++) Marshal.WriteByte(memory, i, 0);
            Marshal.WriteInt32(memory, lengthOffset, text.Length);
            Marshal.Copy(text, 0, memory + nameOffset, text.Length);
            // Win32 resolves the DOS path before sending the counted name to NTFS; terminate its buffer explicitly.
            Marshal.WriteInt16(memory, nameOffset + text.Length, 0);
            Check(SetFileInformationByHandle(_file, 3, memory, (uint)(nameOffset + text.Length + 2)));
        }
        finally { Marshal.FreeHGlobal(memory); }
    }

    public void Delete()
    {
        var memory = Marshal.AllocHGlobal(1);
        try { Marshal.WriteByte(memory, 1); Check(SetFileInformationByHandle(_file, 4, memory, 1)); }
        finally { Marshal.FreeHGlobal(memory); }
    }

    public void Dispose() { _file.Dispose(); foreach (var parent in _parents) parent.Dispose(); }
    private static SafeFileHandle Open(string path, uint access, uint share, uint flags)
    {
        var handle = CreateFile(@"\\?\" + path, access, share, IntPtr.Zero, 3, flags, IntPtr.Zero);
        if (!handle.IsInvalid) return handle;
        var error = Marshal.GetLastWin32Error(); handle.Dispose(); Throw(error); return handle;
    }
    private static Information Info(SafeFileHandle handle) { Check(GetFileInformationByHandle(handle, out var data)); return data; }
    private static void Check(bool ok) { if (!ok) Throw(Marshal.GetLastWin32Error()); }
    private static void Throw(int error)
    {
        if (error is 2 or 3) throw new FileNotFoundException(new Win32Exception(error).Message);
        if (error == 5) throw new UnauthorizedAccessException(new Win32Exception(error).Message);
        throw new IOException(new Win32Exception(error).Message, unchecked((int)(0x80070000u | (uint)error)));
    }
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct Information
    {
        public uint Attributes;
        public long Created, Accessed, Modified;
        public uint Volume, SizeHigh, SizeLow, Links, IdHigh, IdLow;
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Information info);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, IntPtr info, uint size);
}
