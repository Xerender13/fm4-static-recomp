using System.Buffers.Binary;

namespace Fm4StaticRecomp.Launcher.Tests.Fonts;

// A minimal FontCompiler vector font with the layout src\ui\hostui\vector_font.cpp reads:
// "CAFF" at 0, the "vfont" data at 0x190 (offsets below are relative to it), an empty hash
// table, the metrics and 40-byte glyph records after it, then the GPU block (vertices as four
// big-endian half floats, indices as big-endian u16).
public static class VectorFontBuilder
{
    public const int Data = 0x190;
    public const int GlyphCountField = Data + 0x34C;
    private const int HashOffset = 0x400;
    private const int MetricsSize = 0x2C;
    private const int RecordSize = 40;

    public static byte[] Build(
        IReadOnlyList<(uint CodePoint, float Advance, (float X, float Y, float U, float V)[] Vertices, ushort[] Indices)> glyphs,
        int ascent = 750, int descent = 250)
    {
        var metrics = HashOffset + 8;
        var records = metrics + MetricsSize;
        var gpu = records + RecordSize * glyphs.Count;
        var vertexCount = glyphs.Sum(g => g.Vertices.Length);
        var indexCount = glyphs.Sum(g => g.Indices.Length);
        var file = new byte[Math.Max(Data + 0x400, Data + gpu + 8 * vertexCount + 2 * indexCount)];
        var span = file.AsSpan();
        "CAFF"u8.CopyTo(span);
        "vfont\0"u8.CopyTo(span[Data..]);
        BinaryPrimitives.WriteUInt32BigEndian(span[0x60..], (uint)gpu);
        BinaryPrimitives.WriteUInt32BigEndian(span[GlyphCountField..], (uint)glyphs.Count);
        BinaryPrimitives.WriteUInt32BigEndian(span[(Data + 0x35C)..], HashOffset);
        BinaryPrimitives.WriteUInt32BigEndian(span[(Data + 0x360)..], 0);
        BinaryPrimitives.WriteUInt32BigEndian(span[(Data + HashOffset)..], (uint)metrics);
        BinaryPrimitives.WriteUInt32BigEndian(span[(Data + HashOffset + 4)..], (uint)records);
        BinaryPrimitives.WriteUInt32BigEndian(span[(Data + metrics + 8)..], (uint)(8 * vertexCount));
        BinaryPrimitives.WriteUInt32BigEndian(span[(Data + metrics + 0x10)..], (uint)indexCount);
        BinaryPrimitives.WriteInt32BigEndian(span[(Data + metrics + 0x24)..], ascent);
        BinaryPrimitives.WriteInt32BigEndian(span[(Data + metrics + 0x28)..], descent);
        int vertex = 0, index = 0;
        for (var i = 0; i < glyphs.Count; i++)
        {
            var (codePoint, advance, vertices, indices) = glyphs[i];
            var record = span[(Data + records + RecordSize * i)..];
            BinaryPrimitives.WriteUInt32BigEndian(record, codePoint);
            BinaryPrimitives.WriteSingleBigEndian(record[4..], advance);
            BinaryPrimitives.WriteUInt32BigEndian(record[12..], (uint)(8 * vertex));
            BinaryPrimitives.WriteUInt32BigEndian(record[16..], (uint)vertices.Length);
            BinaryPrimitives.WriteUInt32BigEndian(record[20..], (uint)index);
            BinaryPrimitives.WriteUInt32BigEndian(record[24..], (uint)(indices.Length / 3));
            BinaryPrimitives.WriteSingleBigEndian(record[28..], 0);
            foreach (var (x, y, u, v) in vertices)
            {
                var at = span[(Data + gpu + 8 * vertex++)..];
                BinaryPrimitives.WriteHalfBigEndian(at, (Half)x);
                BinaryPrimitives.WriteHalfBigEndian(at[2..], (Half)y);
                BinaryPrimitives.WriteHalfBigEndian(at[4..], (Half)u);
                BinaryPrimitives.WriteHalfBigEndian(at[6..], (Half)v);
            }
            foreach (var value in indices)
                BinaryPrimitives.WriteUInt16BigEndian(span[(Data + gpu + 8 * vertexCount + 2 * index++)..], value);
        }
        return file;
    }

    public static (float X, float Y, float U, float V)[] Square(float sign) =>
    [
        (sign * 0.001f, 0, 0, 1), (sign * 1, 0, 0, 1), (sign * 1, 1, 0, 1), (sign * 0.001f, 1, 0, 1),
    ];

    public static readonly ushort[] SquareIndices = [0, 1, 2, 0, 2, 3];
}
