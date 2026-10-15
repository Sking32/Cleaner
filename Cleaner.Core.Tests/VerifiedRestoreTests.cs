using Cleaner.Core.Services;

namespace Cleaner.Core.Tests;

public sealed class VerifiedRestoreTests
{
    private static string Fixture()
    {
        var root = Path.Combine(Path.GetTempPath(), "VerifiedRestore_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    [Fact]
    public void RestoreLeaseDeniesAncestorRenameWithoutBlockingOtherFileCreation()
    {
        var root = Fixture(); var parent = Directory.CreateDirectory(Path.Combine(root, "source")).FullName;
        var file = Path.Combine(parent, "payload"); File.WriteAllText(file, "fixture");
        using var verified = VerifiedFile.OpenForRestore(file, isDirectory: false);
        Assert.Throws<IOException>(() => Directory.Move(parent, parent + "-moved"));
        File.WriteAllText(Path.Combine(parent, "other.txt"), "unrelated");
        verified.MoveForRestore(Path.Combine(root, "destination", "payload"));
        verified.Dispose();
        Assert.Equal("fixture", File.ReadAllText(Path.Combine(root, "destination", "payload")));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LegacyDirectoryRestoreCreatesMissingParentsAndPreservesContents(bool trailingSeparator)
    {
        var root = Fixture(); var original = Path.Combine(root, "nested", "old-folder");
        Directory.CreateDirectory(original); File.WriteAllText(Path.Combine(original, "payload.txt"), "legacy");
        var quarantine = new QuarantineService(Path.Combine(root, "session"));
        Assert.NotNull(quarantine.MoveToQuarantine(original + (trailingSeparator ? Path.DirectorySeparatorChar.ToString() : ""), "temp"));
        Directory.Delete(Path.GetDirectoryName(original)!);
        var result = quarantine.RestoreAll();
        Assert.True(result.Failed == 0, string.Join(Environment.NewLine, result.Errors));
        Assert.Equal("legacy", File.ReadAllText(Path.Combine(original, "payload.txt")));
    }

    [Theory]
    [InlineData(@"\\server\share\file")]
    [InlineData(@"\\?\C:\file")]
    [InlineData(@"C:\file:stream")]
    public void RestoreRejectsNetworkDeviceAndAlternateStreamNames(string path)
        => Assert.Throws<UnverifiableFileException>(() => VerifiedFile.OpenForRestore(path, false));

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ExistingAndMissingDestinationParentsRejectJunctions(bool missingParent)
    {
        var root = Fixture(); var source = Path.Combine(root, "payload"); File.WriteAllText(source, "retained");
        var outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        var link = Directory.CreateDirectory(Path.Combine(root, "link")).FullName;
        CreateJunction(link, outside);
        using (var verified = VerifiedFile.OpenForRestore(source, false))
            Assert.ThrowsAny<IOException>(() => verified.MoveForRestore(Path.Combine(link, missingParent ? "missing\\payload" : "payload")));
        Assert.Equal("retained", File.ReadAllText(source));
        Assert.Empty(Directory.GetFileSystemEntries(outside));
    }

    // A junction in this test's own temporary directory needs no symlink privilege.
    private static void CreateJunction(string link, string target)
    {
        var substitute = System.Text.Encoding.Unicode.GetBytes(@"\??\" + target);
        var printable = System.Text.Encoding.Unicode.GetBytes(target);
        var data = new byte[16 + substitute.Length + 2 + printable.Length + 2];
        BitConverter.GetBytes(0xA0000003u).CopyTo(data, 0);
        BitConverter.GetBytes(checked((ushort)(data.Length - 8))).CopyTo(data, 4);
        BitConverter.GetBytes(checked((ushort)substitute.Length)).CopyTo(data, 10);
        BitConverter.GetBytes(checked((ushort)(substitute.Length + 2))).CopyTo(data, 12);
        BitConverter.GetBytes(checked((ushort)printable.Length)).CopyTo(data, 14);
        substitute.CopyTo(data, 16); printable.CopyTo(data, 18 + substitute.Length);
        using var handle = CreateFile(link, 0x40000000, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        Assert.False(handle.IsInvalid);
        Assert.True(DeviceIoControl(handle, 0x900A4, data, data.Length, IntPtr.Zero, 0, out _, IntPtr.Zero),
            new System.ComponentModel.Win32Exception(System.Runtime.InteropServices.Marshal.GetLastWin32Error()).Message);
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", EntryPoint = "CreateFileW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern Microsoft.Win32.SafeHandles.SafeFileHandle CreateFile(string path, uint access, uint share, IntPtr security, uint creation, uint flags, IntPtr template);
    [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(Microsoft.Win32.SafeHandles.SafeFileHandle handle, uint code, byte[] input, int inputLength, IntPtr output, int outputLength, out int returned, IntPtr overlapped);

}
