using System.Buffers.Binary;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Fm4StaticRecomp.Launcher.Core.Platform;

public sealed partial class WindowsPlatform : IPlatform
{
    private const uint GenericWrite = 0x40000000;
    private const uint OpenExisting = 3;
    private const uint BackupSemantics = 0x02000000;
    private const uint OpenReparsePoint = 0x00200000;
    private const uint SetReparsePoint = 0x000900A4;
    private const uint MountPointTag = 0xA0000003;

    public bool SupportsDxgiPresentation => true;

    public string ExecutableName(string baseName) => baseName + ".exe";

    // A console program writes in the OEM code page (866 on a Russian PC), not the ANSI one.
    public Encoding ConsoleEncoding
    {
        get
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding((int)GetOEMCP());
        }
    }

    // A junction rather than a symbolic link: creating one needs no administrator rights or
    // developer mode. Set directly, not through cmd's mklink, which reads &, ^ and %NAME% in a
    // path as its own syntax.
    public void CreateDirectoryLink(string link, string target)
    {
        if (Path.Exists(link)) throw new IOException($"Could not link {link} to {target}: {link} already exists.");
        Directory.CreateDirectory(link);
        try
        {
            using var handle = CreateFile(link, GenericWrite, 0, 0, OpenExisting, BackupSemantics | OpenReparsePoint, 0);
            if (handle.IsInvalid) throw new Win32Exception();
            var buffer = MountPoint(Path.TrimEndingDirectorySeparator(Path.GetFullPath(target)));
            if (!DeviceIoControl(handle, SetReparsePoint, buffer, buffer.Length, 0, 0, out _, 0)) throw new Win32Exception();
        }
        catch (Win32Exception ex)
        {
            Directory.Delete(link);
            throw new IOException($"Could not link {link} to {target}: {ex.Message}", ex);
        }
    }

    public bool IsDirectoryLink(string path) =>
        Directory.Exists(path) && new DirectoryInfo(path).LinkTarget is not null;

    // REPARSE_DATA_BUFFER's MountPointReparseBuffer: the NT path, then the path shown to people.
    private static byte[] MountPoint(string target)
    {
        var substitute = Encoding.Unicode.GetBytes(@"\??\" + target);
        var print = Encoding.Unicode.GetBytes(target);
        var buffer = new byte[16 + substitute.Length + 2 + print.Length + 2];
        var span = buffer.AsSpan();
        BinaryPrimitives.WriteUInt32LittleEndian(span, MountPointTag);
        BinaryPrimitives.WriteUInt16LittleEndian(span[4..], (ushort)(buffer.Length - 8));
        BinaryPrimitives.WriteUInt16LittleEndian(span[10..], (ushort)substitute.Length);
        BinaryPrimitives.WriteUInt16LittleEndian(span[12..], (ushort)(substitute.Length + 2));
        BinaryPrimitives.WriteUInt16LittleEndian(span[14..], (ushort)print.Length);
        substitute.CopyTo(span[16..]);
        print.CopyTo(span[(16 + substitute.Length + 2)..]);
        return buffer;
    }

    [LibraryImport("kernel32.dll")]
    private static partial uint GetOEMCP();

    [LibraryImport("kernel32.dll", EntryPoint = "CreateFileW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial SafeFileHandle CreateFile(string name, uint access, uint share, nint security, uint disposition,
        uint flags, nint template);

    [LibraryImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeviceIoControl(SafeFileHandle device, uint code, byte[] input, int inputSize, nint output,
        int outputSize, out int returned, nint overlapped);
}
