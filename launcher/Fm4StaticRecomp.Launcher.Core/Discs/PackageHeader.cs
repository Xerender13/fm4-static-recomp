using System.Buffers.Binary;
using System.Text;

namespace Fm4StaticRecomp.Launcher.Core.Discs;

public sealed record PackageHeader(string Magic, uint ContentType, uint MediaId, uint TitleId, string DisplayName)
{
    public const uint Fm4TitleId = 0x4D530910;
    public const uint MarketplaceContent = 0x00000002;
    public const uint GamesOnDemand = 0x00007000;
    private const int DisplayNameOffset = 0x411;
    private const int DisplayNameLength = 0x80;

    // A title update of FM4 is a LIVE package of its title too, but not marketplace content.
    public bool IsFm4Dlc => Magic is "LIVE" or "PIRS" && TitleId == Fm4TitleId && ContentType == MarketplaceContent;

    public static PackageHeader? TryRead(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return TryRead(stream);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    public static PackageHeader? TryRead(Stream stream)
    {
        var buffer = new byte[DisplayNameOffset + DisplayNameLength];
        stream.Position = 0;
        if (stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) < buffer.Length) return null;
        var magic = Encoding.ASCII.GetString(buffer, 0, 4);
        if (magic is not ("LIVE" or "PIRS" or "CON ")) return null;
        var name = Encoding.BigEndianUnicode.GetString(buffer, DisplayNameOffset, DisplayNameLength);
        return new PackageHeader(magic,
            BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0x344)),
            BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0x354)),
            BinaryPrimitives.ReadUInt32BigEndian(buffer.AsSpan(0x360)),
            name.TrimEnd('\0').Split('\0')[0]);
    }
}
