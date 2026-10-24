using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Media.TextFormatting;
using Avalonia.Platform;
using Fm4StaticRecomp.Launcher.Core.Fonts;

namespace Fm4StaticRecomp.Launcher.Controls;

// A heading in capitals drawn with the disc's Swiss 721 Bold Condensed once the built game has
// exported it (spec 10); Barlow Condensed SemiBold until then. The disc's fonts are never
// shipped, so they come from the player's own build.
public sealed class VectorHeading : Control
{
    public static readonly StyledProperty<string> TextProperty =
        AvaloniaProperty.Register<VectorHeading, string>(nameof(Text), "");
    public static readonly StyledProperty<double> FontSizeProperty =
        AvaloniaProperty.Register<VectorHeading, double>(nameof(FontSize), 46);
    public static readonly StyledProperty<IBrush?> ForegroundProperty =
        AvaloniaProperty.Register<VectorHeading, IBrush?>(nameof(Foreground), Brushes.White);

    private static readonly Dictionary<(uint CodePoint, float Size), GlyphBitmap> Glyphs = [];
    private static UiFonts? _fonts;
    private static event Action? FontsChanged;

    private WriteableBitmap? _bitmap;
    private Size _bitmapSize;
    private TextLayout? _layout;

    static VectorHeading()
    {
        AffectsMeasure<VectorHeading>(TextProperty, FontSizeProperty);
        AffectsRender<VectorHeading>(ForegroundProperty);
    }

    public static UiFonts? Fonts
    {
        get => _fonts;
        set
        {
            if (ReferenceEquals(_fonts, value)) return;
            _fonts = value;
            Glyphs.Clear();
            FontsChanged?.Invoke();
        }
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public double FontSize
    {
        get => GetValue(FontSizeProperty);
        set => SetValue(FontSizeProperty, value);
    }

    public IBrush? Foreground
    {
        get => GetValue(ForegroundProperty);
        set => SetValue(ForegroundProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        FontsChanged += Refresh;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        FontsChanged -= Refresh;
        base.OnDetachedFromVisualTree(e);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty || change.Property == FontSizeProperty || change.Property == ForegroundProperty)
            Discard();
    }

    private void Refresh()
    {
        Discard();
        InvalidateMeasure();
        InvalidateVisual();
    }

    private void Discard()
    {
        _bitmap?.Dispose();
        _bitmap = null;
        _layout = null;
    }

    private string Caps => Text.ToUpperInvariant();

    protected override Size MeasureOverride(Size availableSize)
    {
        if (_fonts is { } fonts)
        {
            var font = fonts.Heading;
            var width = Caps.Sum(c => Advance(font, c)) * FontSize;
            return new Size(Math.Ceiling(width), Math.Ceiling((font.Ascent + font.Descent) * FontSize));
        }
        var layout = Layout();
        return new Size(layout.WidthIncludingTrailingWhitespace, layout.Height);
    }

    public override void Render(DrawingContext context)
    {
        if (_fonts is null)
        {
            Layout().Draw(context, new Point(0, 0));
            return;
        }
        var scale = TopLevel.GetTopLevel(this)?.RenderScaling ?? 1.0;
        _bitmap ??= Compose(_fonts.Heading, scale, out _bitmapSize);
        if (_bitmap is not null) context.DrawImage(_bitmap, new Rect(_bitmap.Size), new Rect(_bitmapSize));
    }

    private TextLayout Layout()
    {
        var family = Application.Current?.FindResource("Body") as FontFamily ?? FontFamily.Default;
        return _layout ??= new TextLayout(Caps, new Typeface(family, FontStyle.Normal, FontWeight.SemiBold), FontSize, Foreground);
    }

    private static float Advance(VectorFont font, char c) => font.Find(c)?.Advance ?? (c == ' ' ? 0.25f : 0.5f);

    // The glyphs at device pixels, placed by their advances on one baseline and tinted with the
    // foreground colour (premultiplied BGRA).
    private WriteableBitmap? Compose(VectorFont font, double scale, out Size logicalSize)
    {
        var pixelsPerEm = (float)(FontSize * scale);
        var baseline = (int)MathF.Ceiling(font.Ascent * pixelsPerEm);
        var height = baseline + (int)MathF.Ceiling(font.Descent * pixelsPerEm);
        var placed = new List<(int X, GlyphBitmap Bitmap)>();
        var pen = 0f;
        var width = 0;
        foreach (var c in Caps)
        {
            if (font.Find(c) is { } glyph)
            {
                var key = (CodePoint: (uint)c, Size: pixelsPerEm);
                if (!Glyphs.TryGetValue(key, out var bitmap)) Glyphs[key] = bitmap = font.Rasterize(glyph, pixelsPerEm);
                var x = (int)MathF.Round(pen);
                placed.Add((x, bitmap));
                width = Math.Max(width, x + bitmap.Left + bitmap.Width);
            }
            pen += Advance(font, c) * pixelsPerEm;
        }
        width = Math.Max(width, (int)MathF.Ceiling(pen));
        logicalSize = new Size(width / scale, height / scale);
        if (width <= 0 || height <= 0) return null;
        var alpha = new byte[width * height];
        foreach (var (x, bitmap) in placed)
            for (var row = 0; row < bitmap.Height; row++)
            {
                var y = baseline + bitmap.Top + row;
                if (y < 0 || y >= height) continue;
                for (var column = 0; column < bitmap.Width; column++)
                {
                    var px = x + bitmap.Left + column;
                    if (px < 0 || px >= width) continue;
                    var index = y * width + px;
                    alpha[index] = Math.Max(alpha[index], bitmap.Alpha[row * bitmap.Width + column]);
                }
            }
        var colour = (Foreground as ISolidColorBrush)?.Color ?? Colors.White;
        var pixels = new byte[width * height * 4];
        for (var i = 0; i < alpha.Length; i++)
        {
            var a = alpha[i] * colour.A / 255;
            pixels[4 * i] = (byte)(colour.B * a / 255);
            pixels[4 * i + 1] = (byte)(colour.G * a / 255);
            pixels[4 * i + 2] = (byte)(colour.R * a / 255);
            pixels[4 * i + 3] = (byte)a;
        }
        // At 96 DPI the bitmap's size is its pixel size, drawn into the logical rectangle: a
        // source rectangle in DIPs of a high-DPI bitmap crops it.
        var result = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using var buffer = result.Lock();
        for (var row = 0; row < height; row++)
            Marshal.Copy(pixels, row * width * 4, buffer.Address + row * buffer.RowBytes, width * 4);
        return result;
    }
}
