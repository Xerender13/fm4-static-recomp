using System.Buffers.Binary;

namespace Fm4StaticRecomp.Launcher.Core.Fonts;

public sealed record Glyph(uint CodePoint, float Advance, float OffsetX, int FirstVertex, int VertexCount, int FirstIndex,
    int TriangleCount);

// `Left` and `Top` place the top-left texel relative to the pen on the baseline, y down.
public sealed record GlyphBitmap(int Width, int Height, int Left, int Top, byte[] Alpha)
{
    public static readonly GlyphBitmap Empty = new(0, 0, 0, 0, []);
}

// A FontCompiler vector font (`*_vector_aa.dt`, CAFF asset "vfont") from the disc's Fonts.zip:
// a port of src\ui\hostui\vector_font.cpp, kept to the same float arithmetic so the
// launcher's headings look like the game's.
public sealed class VectorFont
{
    private const int DataOffset = 0x190;
    private const int GlyphRecordSize = 40;

    private readonly List<Glyph> _glyphs = [];
    private readonly Dictionary<uint, int> _byCodePoint = [];
    private ushort[] _rawVertices = [];
    private ushort[] _indices = [];

    private VectorFont() { }

    public float Ascent { get; private set; } = 0.75f;
    public float Descent { get; private set; } = 0.25f;
    public int GlyphCount => _glyphs.Count;

    public static VectorFont? Parse(ReadOnlySpan<byte> data)
    {
        if (data.Length < DataOffset + 0x400 || !data[..4].SequenceEqual("CAFF"u8) ||
            !data.Slice(DataOffset, 6).SequenceEqual("vfont\0"u8))
            return null;
        long size = data.Length;
        var gpu = DataOffset + (long)Be32(data, 0x60);
        var glyphCount = (long)Be32(data, DataOffset + 0x34C);
        var hashOffset = (long)Be32(data, DataOffset + 0x35C);
        var hashSize = (long)Be32(data, DataOffset + 0x360);
        var afterHash = DataOffset + hashOffset + 4 * hashSize;
        if (gpu > size || afterHash + 8 > gpu || glyphCount > 0x10000) return null;
        var metrics = DataOffset + (long)Be32(data, (int)afterHash);
        var records = DataOffset + (long)Be32(data, (int)afterHash + 4);
        if (metrics + 0x2C > gpu || records + glyphCount * GlyphRecordSize > gpu) return null;
        var vertexBytes = (long)Be32(data, (int)metrics + 8);
        var indexCount = (long)Be32(data, (int)metrics + 0x10);
        if (vertexBytes % 8 != 0 || vertexBytes > size - gpu || indexCount * 2 > size - gpu - vertexBytes) return null;

        var font = new VectorFont();
        var ascent = (int)Be32(data, (int)metrics + 0x24);
        var descent = (int)Be32(data, (int)metrics + 0x28);
        if (ascent > 0 && descent > 0)
        {
            font.Ascent = (float)ascent / (ascent + descent);
            font.Descent = (float)descent / (ascent + descent);
        }
        font._rawVertices = new ushort[vertexBytes / 2];
        for (var i = 0; i < font._rawVertices.Length; i++)
            font._rawVertices[i] = BinaryPrimitives.ReadUInt16BigEndian(data[(int)(gpu + 2 * i)..]);
        font._indices = new ushort[indexCount];
        for (var i = 0; i < indexCount; i++)
            font._indices[i] = BinaryPrimitives.ReadUInt16BigEndian(data[(int)(gpu + vertexBytes + 2 * i)..]);
        for (var i = 0; i < glyphCount; i++)
        {
            var record = (int)(records + i * GlyphRecordSize);
            var codePoint = Be32(data, record);
            var advance = BinaryPrimitives.ReadSingleBigEndian(data[(record + 4)..]);
            var vertexOffset = Be32(data, record + 12);
            var vertexCount = (long)Be32(data, record + 16);
            var firstIndex = (long)Be32(data, record + 20);
            var triangleCount = (long)Be32(data, record + 24);
            var offsetX = BinaryPrimitives.ReadSingleBigEndian(data[(record + 28)..]);
            var firstVertex = (long)(vertexOffset / 8);
            if (vertexOffset % 8 != 0 || firstVertex + vertexCount > vertexBytes / 8 ||
                firstIndex + 3 * triangleCount > indexCount || !float.IsFinite(advance) || !float.IsFinite(offsetX))
                return null;
            for (var t = 0L; t < 3 * triangleCount; t++)
                if (font._indices[firstIndex + t] >= vertexCount) return null;
            font._byCodePoint.TryAdd(codePoint, font._glyphs.Count);
            font._glyphs.Add(new Glyph(codePoint, advance, offsetX, (int)firstVertex, (int)vertexCount, (int)firstIndex,
                (int)triangleCount));
        }
        return font;
    }

    public Glyph? Find(uint codePoint) => _byCodePoint.TryGetValue(codePoint, out var i) ? _glyphs[i] : null;

    // Coverage of `glyph` at `pixelsPerEm`, sampled at pixel centres, by the coverage rule the
    // title's VectorFont shader applies.
    public GlyphBitmap Rasterize(Glyph glyph, float pixelsPerEm)
    {
        if (glyph.TriangleCount == 0 || !(pixelsPerEm > 0.0f)) return GlyphBitmap.Empty;
        // The vertex shader places a vertex at (|x| + offset, y); the sign of x marks triangles
        // inside (+) or outside (-) the outline, and z, w are the curve coordinates (u, v).
        var vertices = new Vertex[glyph.VertexCount];
        float minX = float.MaxValue, maxX = -float.MaxValue, minY = float.MaxValue, maxY = -float.MaxValue;
        for (var i = 0; i < glyph.VertexCount; i++)
        {
            var raw = 4 * (glyph.FirstVertex + i);
            var x = Half(_rawVertices[raw]);
            var vertex = new Vertex(
                (MathF.Abs(x) + glyph.OffsetX) * pixelsPerEm,
                -Half(_rawVertices[raw + 1]) * pixelsPerEm,
                Half(_rawVertices[raw + 2]),
                Half(_rawVertices[raw + 3]),
                x > 0.0f ? 1.0f : x < 0.0f ? -1.0f : 0.0f);
            vertices[i] = vertex;
            minX = MathF.Min(minX, vertex.X);
            maxX = MathF.Max(maxX, vertex.X);
            minY = MathF.Min(minY, vertex.Y);
            maxY = MathF.Max(maxY, vertex.Y);
        }
        var left = (int)MathF.Floor(minX) - 1;
        var top = (int)MathF.Floor(minY) - 1;
        var width = (int)MathF.Ceiling(maxX) - left + 2;
        var height = (int)MathF.Ceiling(maxY) - top + 2;
        if (width <= 0 || height <= 0 || width > 4096 || height > 4096) return GlyphBitmap.Empty;
        var coverage = new float[width * height];

        for (var t = 0; t < glyph.TriangleCount; t++)
        {
            var a = vertices[_indices[glyph.FirstIndex + 3 * t]];
            var b = vertices[_indices[glyph.FirstIndex + 3 * t + 1]];
            var c = vertices[_indices[glyph.FirstIndex + 3 * t + 2]];
            var area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
            if (area == 0.0f) continue;
            var inverseArea = 1.0f / area;
            // Screen-space gradients of u and v, constant over the triangle; the title takes
            // them per 2x2 quad with the same result on a plane.
            void Gradient(float q0, float q1, float q2, out float gx, out float gy)
            {
                gx = ((q1 - q0) * (c.Y - a.Y) - (q2 - q0) * (b.Y - a.Y)) * inverseArea;
                gy = ((q2 - q0) * (b.X - a.X) - (q1 - q0) * (c.X - a.X)) * inverseArea;
            }
            Gradient(a.U, b.U, c.U, out var ux, out var uy);
            Gradient(a.V, b.V, c.V, out var vx, out var vy);
            var x0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))) - left);
            var x1 = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))) - left);
            var y0 = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))) - top);
            var y1 = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))) - top);
            for (var py = y0; py <= y1; py++)
            {
                var y = (float)(py + top) + 0.5f;
                for (var px = x0; px <= x1; px++)
                {
                    var x = (float)(px + left) + 0.5f;
                    var w0 = ((b.X - x) * (c.Y - y) - (c.X - x) * (b.Y - y)) * inverseArea;
                    var w1 = ((c.X - x) * (a.Y - y) - (a.X - x) * (c.Y - y)) * inverseArea;
                    var w2 = 1.0f - w0 - w1;
                    if (w0 < 0.0f || w1 < 0.0f || w2 < 0.0f) continue;
                    var u = w0 * a.U + w1 * b.U + w2 * c.U;
                    var v = w0 * a.V + w1 * b.V + w2 * c.V;
                    var s = w0 * a.S + w1 * b.S + w2 * c.S;
                    // f = u^2 - v is the implicit curve; f * s over its gradient length is the
                    // signed distance to the edge in pixels.
                    var gx = s * (2.0f * u * ux - vx);
                    var gy = s * (2.0f * u * uy - vy);
                    var gradientLength = MathF.Sqrt(gx * gx + gy * gy);
                    var signedValue = (u * u - v) * s;
                    // Where u and v are constant (deep inside or far outside) the gradient is zero
                    // and only the sign of f * s matters.
                    var distance = gradientLength > 0.0f ? signedValue / gradientLength
                        : signedValue < 0.0f ? -float.MaxValue
                        : signedValue > 0.0f ? float.MaxValue
                        : 0.0f;
                    var alpha = Math.Clamp(0.5f - distance, 0.0f, 1.0f);
                    if (alpha > 0.0f)
                    {
                        ref var value = ref coverage[py * width + px];
                        value = alpha + value * (1.0f - alpha);
                    }
                }
            }
        }

        int trimLeft = width, trimRight = -1, trimTop = height, trimBottom = -1;
        for (var py = 0; py < height; py++)
        for (var px = 0; px < width; px++)
        {
            if (coverage[py * width + px] < 0.5f / 255.0f) continue;
            trimLeft = Math.Min(trimLeft, px);
            trimRight = Math.Max(trimRight, px);
            trimTop = Math.Min(trimTop, py);
            trimBottom = Math.Max(trimBottom, py);
        }
        if (trimRight < 0) return GlyphBitmap.Empty;
        var bitmapWidth = trimRight - trimLeft + 1;
        var bitmapHeight = trimBottom - trimTop + 1;
        var alphaBytes = new byte[bitmapWidth * bitmapHeight];
        for (var py = 0; py < bitmapHeight; py++)
        for (var px = 0; px < bitmapWidth; px++)
            alphaBytes[py * bitmapWidth + px] =
                (byte)MathF.Round(coverage[(py + trimTop) * width + px + trimLeft] * 255.0f, MidpointRounding.AwayFromZero);
        return new GlyphBitmap(bitmapWidth, bitmapHeight, left + trimLeft, top + trimTop, alphaBytes);
    }

    private static uint Be32(ReadOnlySpan<byte> data, int at) => BinaryPrimitives.ReadUInt32BigEndian(data[at..]);

    private static float Half(ushort bits) => (float)BitConverter.UInt16BitsToHalf(bits);

    private readonly record struct Vertex(float X, float Y, float U, float V, float S);
}
