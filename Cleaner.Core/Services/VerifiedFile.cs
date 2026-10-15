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

/// <summary>Rejects reparse traversal atomically and retains ancestors; mutations use verified handles.</summary>
public sealed class VerifiedFile : IVerifiedFile
{
    private readonly SafeFileHandle _file;
    private readonly List<SafeFileHandle> _parents = new();
    public FileIdentity Identity { get; }
    private const uint ReadData = 0x1, ReadAttributes = 0x80, DeleteAccess = 0x10000;
    private const uint UnsafeAttributes = 0x400 | 0x1000 | 0x40000 | 0x400000; // reparse, offline, recall

    public VerifiedFile(string path, bool forMutation = false, FileIdentity? expected = null)
        : this(path, forMutation, expected, false) { }

    internal static VerifiedFile OpenForRestore(string path, bool isDirectory)
        => new(path, true, null, isDirectory);

    private VerifiedFile(string path, bool forMutation, FileIdentity? expected, bool allowDirectory)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("Verified cleanup requires Windows.");
        path = LocalPath(path);
        _file = new SafeFileHandle(IntPtr.Zero, true);
        try
        {
            // Root-first locking prevents a parent junction/rename swap during open.
            LockParents(path, createMissing: false);
            _file.Dispose();
            // Attribute-only access bypasses some sharing checks. Request read access without reading
            // content so exclusively opened files are skipped already during analysis.
            _file = Open(path, ReadData | ReadAttributes | (forMutation ? DeleteAccess : 0), 1, 0x02200000);
            var data = Info(_file);
            var isDirectory = (data.Attributes & 0x10) != 0;
            if ((data.Attributes & UnsafeAttributes) != 0 || isDirectory != allowDirectory || !isDirectory && data.Links != 1)
                throw new UnverifiableFileException("Links, directories and cloud objects are excluded.");
            Identity = new(data.Volume, ((ulong)data.IdHigh << 32) | data.IdLow,
                ((long)data.SizeHigh << 32) | data.SizeLow, data.Created, data.Modified, data.Attributes);
            if (expected != null && Identity != expected) throw new FileChangedException();
        }
        catch { Dispose(); throw; }
    }

    public void Move(string target) => Move(target, createParents: false);

    internal void MoveForRestore(string target) => Move(target, createParents: true);

    private void Move(string target, bool createParents)
    {
        target = LocalPath(target);
        LockParents(target, createParents);
        Rename(target);
    }

    private static string LocalPath(string path)
    {
        path = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        if (path.StartsWith("\\\\", StringComparison.Ordinal) || path.IndexOf(':', 2) >= 0)
            throw new UnverifiableFileException("Network, device paths and alternate streams are excluded.");
        if (Path.GetPathRoot(path) == path) throw new UnverifiableFileException("A volume root is not a cleanup or restore item.");
        return path;
    }

    private void LockParents(string path, bool createMissing)
    {
        var ancestors = new Stack<string>();
        for (var parent = Path.GetDirectoryName(path); parent != null; parent = Path.GetDirectoryName(parent)) ancestors.Push(parent);
        foreach (var parent in ancestors)
        {
            // Every preceding ancestor is already held. Create one component at a time;
            // open the component itself (never follow a junction), then keep it held too.
            var handle = Open(parent, ReadData | ReadAttributes, 3, 0x02200000, createMissing); _parents.Add(handle);
            if ((Info(handle).Attributes & UnsafeAttributes) != 0) throw new UnverifiableFileException("Linked or cloud destination excluded.");
        }
    }

    private void Rename(string target)
    {
        // FILE_RENAME_INFO: BOOL, aligned HANDLE, DWORD byte count, WCHAR name.
        // A relative leaf is resolved against the retained destination directory object,
        // not through its pathname (which could acquire reparse metadata after validation).
        var text = System.Text.Encoding.Unicode.GetBytes(Path.GetFileName(target));
        var rootOffset = IntPtr.Size == 8 ? 8 : 4;
        var lengthOffset = rootOffset + IntPtr.Size;
        var nameOffset = lengthOffset + 4;
        var memory = Marshal.AllocHGlobal(nameOffset + text.Length + 2);
        try
        {
            for (var i = 0; i < nameOffset; i++) Marshal.WriteByte(memory, i, 0);
            Marshal.WriteIntPtr(memory, rootOffset, _parents[^1].DangerousGetHandle());
            Marshal.WriteInt32(memory, lengthOffset, text.Length);
            Marshal.Copy(text, 0, memory + nameOffset, text.Length);
            // Native FILE_RENAME_INFORMATION uses the retained directory and a counted leaf name.
            Marshal.WriteInt16(memory, nameOffset + text.Length, 0);
            var status = NtSetInformationFile(_file, out _, memory, (uint)(nameOffset + text.Length + 2), 10);
            if (status < 0)
            {
                var error = (int)RtlNtStatusToDosError(status);
                if (error == 17) throw new IOException("Cross-volume recovery requires manual copying; the quarantined original has been preserved.");
                Throw(error);
            }
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
    private static SafeFileHandle Open(string path, uint access, uint share, uint flags, bool createDirectory = false)
    {
        // OBJ_DONT_REPARSE makes name resolution itself fail on a replaced ancestor;
        // a path-based precheck followed by CreateFile is not sufficient.
        var name = @"\??\" + path;
        var buffer = Marshal.StringToHGlobalUni(name);
        var unicode = Marshal.AllocHGlobal(Marshal.SizeOf<UnicodeString>());
        try
        {
            Marshal.StructureToPtr(new UnicodeString { Length = checked((ushort)(name.Length * 2)), MaximumLength = checked((ushort)(name.Length * 2)), Buffer = buffer }, unicode, false);
            var attributes = new ObjectAttributes { Length = Marshal.SizeOf<ObjectAttributes>(), ObjectName = unicode, Attributes = 0x1040 };
            var status = NtCreateFile(out var handle, access | 0x100000, ref attributes, out _, IntPtr.Zero, 0,
                share, createDirectory ? 3u : 1u, 0x00204020u | (createDirectory ? 1u : 0u), IntPtr.Zero, 0);
            if (status >= 0) return handle;
            handle?.Dispose(); Throw((int)RtlNtStatusToDosError(status)); return null!;
        }
        finally { Marshal.FreeHGlobal(unicode); Marshal.FreeHGlobal(buffer); }
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
    [StructLayout(LayoutKind.Sequential)]
    private struct UnicodeString { public ushort Length, MaximumLength; public IntPtr Buffer; }
    [StructLayout(LayoutKind.Sequential)]
    private struct ObjectAttributes { public int Length; public IntPtr RootDirectory, ObjectName; public uint Attributes; public IntPtr SecurityDescriptor, SecurityQualityOfService; }
    [StructLayout(LayoutKind.Sequential)]
    private struct IoStatus { public IntPtr Status, Information; }
    [DllImport("ntdll.dll")]
    private static extern int NtCreateFile(out SafeFileHandle handle, uint access, ref ObjectAttributes attributes, out IoStatus status, IntPtr allocationSize, uint fileAttributes, uint share, uint disposition, uint options, IntPtr ea, uint eaLength);
    [DllImport("ntdll.dll")]
    private static extern uint RtlNtStatusToDosError(int status);
    [DllImport("ntdll.dll")]
    private static extern int NtSetInformationFile(SafeFileHandle handle, out IoStatus status, IntPtr information, uint size, int kind);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle handle, out Information info);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileInformationByHandle(SafeFileHandle handle, int kind, IntPtr info, uint size);
}
