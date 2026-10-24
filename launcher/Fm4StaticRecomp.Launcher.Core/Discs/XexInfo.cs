using System.Buffers.Binary;

namespace Fm4StaticRecomp.Launcher.Core.Discs;

public static class XexInfo
{
    private const uint ExecutionInfoKey = 0x00040006;
    private const int HeaderCountOffset = 0x14;
    private const int HeadersOffset = 0x18;
    private const uint MaxHeaders = 0x1000;

    public static string? MediaId(Stream xex)
    {
        var magic = Read(xex, 0, 4);
        if (magic is null || !magic.AsSpan().SequenceEqual("XEX2"u8)) return null;
        var count = Read(xex, HeaderCountOffset, 4);
        if (count is null) return null;
        var headers = BinaryPrimitives.ReadUInt32BigEndian(count);
        if (headers > MaxHeaders) return null;
        var pairs = Read(xex, HeadersOffset, (int)headers * 8);
        if (pairs is null) return null;
        for (var i = 0; i < headers; i++)
        {
            if (BinaryPrimitives.ReadUInt32BigEndian(pairs.AsSpan(i * 8)) != ExecutionInfoKey) continue;
            var info = Read(xex, BinaryPrimitives.ReadUInt32BigEndian(pairs.AsSpan(i * 8 + 4)), 4);
            return info is null ? null : BinaryPrimitives.ReadUInt32BigEndian(info).ToString("X8");
        }
        return null;
    }

    private static byte[]? Read(Stream stream, long offset, int count)
    {
        if (offset + count > stream.Length) return null;
        var buffer = new byte[count];
        stream.Position = offset;
        return stream.ReadAtLeast(buffer, count, throwOnEndOfStream: false) == count ? buffer : null;
    }
}
