using System.Buffers.Binary;
using Fm4StaticRecomp.Launcher.Core.Fonts;

namespace Fm4StaticRecomp.Launcher.Tests.Fonts;

public class VectorFontTests
{
    private static byte[] OneSquare(float sign) =>
        VectorFontBuilder.Build([(0x41, 0.6f, VectorFontBuilder.Square(sign), VectorFontBuilder.SquareIndices)]);

    [Fact]
    public void Parses_metrics_and_glyphs()
    {
        var font = VectorFont.Parse(VectorFontBuilder.Build(
            [(0x41, 0.6f, VectorFontBuilder.Square(1), VectorFontBuilder.SquareIndices),
             (0x42, 0.55f, VectorFontBuilder.Square(1), VectorFontBuilder.SquareIndices)], ascent: 800, descent: 200))!;
        Assert.Equal(2, font.GlyphCount);
        Assert.Equal(0.8f, font.Ascent);
        Assert.Equal(0.2f, font.Descent);
        Assert.Equal(0.6f, font.Find(0x41)!.Advance);
        Assert.Equal(2, font.Find(0x42)!.TriangleCount);
        Assert.Null(font.Find(0x43));
    }

    [Fact]
    public void A_square_of_inside_triangles_is_fully_covered()
    {
        var font = VectorFont.Parse(OneSquare(1))!;
        var bitmap = font.Rasterize(font.Find(0x41)!, 32);
        Assert.InRange(bitmap.Width, 31, 33);
        Assert.InRange(bitmap.Height, 31, 33);
        Assert.InRange(bitmap.Top, -33, -31);
        for (var y = 2; y < bitmap.Height - 2; y++)
        for (var x = 2; x < bitmap.Width - 2; x++)
            Assert.Equal(255, bitmap.Alpha[y * bitmap.Width + x]);
    }

    [Fact]
    public void Outside_triangles_cover_nothing()
    {
        var font = VectorFont.Parse(OneSquare(-1))!;
        Assert.Equal(0, font.Rasterize(font.Find(0x41)!, 32).Width);
    }

    [Fact]
    public void Rejects_broken_files()
    {
        var wrongMagic = OneSquare(1);
        wrongMagic[0] = (byte)'X';
        Assert.Null(VectorFont.Parse(wrongMagic));

        var badIndex = VectorFontBuilder.Build([(0x41, 0.6f, VectorFontBuilder.Square(1), [0, 1, 4])]);
        Assert.Null(VectorFont.Parse(badIndex));

        var pastGpu = OneSquare(1);
        BinaryPrimitives.WriteUInt32BigEndian(pastGpu.AsSpan(VectorFontBuilder.GlyphCountField), 100);
        Assert.Null(VectorFont.Parse(pastGpu));

        Assert.Null(VectorFont.Parse(new byte[16]));
    }

    [Fact]
    public void Parses_the_disc_s_fonts()
    {
        var folder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, @"..\..\..\..\..\.local\ui-fonts"));
        if (!File.Exists(Path.Combine(folder, "a_vector_aa.dt"))) return;
        var fonts = UiFonts.Load(folder)!;
        Assert.True(fonts.Heading.GlyphCount > 90);
        var w = fonts.Heading.Rasterize(fonts.Heading.Find('W')!, 48);
        var i = fonts.Heading.Rasterize(fonts.Heading.Find('I')!, 48);
        Assert.True(w.Width > i.Width);
        Assert.True(i.Width > 0 && i.Height > 0);
    }

    [Fact]
    public void Ui_fonts_are_null_when_a_file_is_missing()
    {
        using var temp = new TempDir();
        temp.File("a_vector_aa.dt", OneSquare(1));
        Assert.Null(UiFonts.Load(temp.Path));
        temp.File("b_vector_aa.dt", OneSquare(1));
        Assert.NotNull(UiFonts.Load(temp.Path));
    }
}
