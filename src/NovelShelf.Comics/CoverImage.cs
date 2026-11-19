using SkiaSharp;

namespace NovelShelf.Comics;

public static class CoverImage
{
    public static bool IsJpeg(ReadOnlySpan<byte> bytes) => bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8;

    public static byte[]? ToJpeg(byte[] bytes, int quality = 90)
    {
        if (IsJpeg(bytes))
            return bytes;
        return Raster.Decode(bytes, color: true)?.EncodeJpeg(quality);
    }

    public static byte[] WithVolumeBadge(byte[] cover, int volume, int quality = 90)
    {
        using var stream = new SKMemoryStream(cover);
        using var codec = SKCodec.Create(stream);
        if (codec is null)
            return cover;
        using var bitmap = SKBitmap.Decode(codec);
        if (bitmap is null || bitmap.Width == 0 || bitmap.Height == 0)
            return cover;

        using var surface = SKSurface.Create(new SKImageInfo(bitmap.Width, bitmap.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.White);
        canvas.DrawBitmap(bitmap, 0, 0);

        var band = Math.Max(24f, bitmap.Height * 0.14f);
        using (var shade = new SKPaint { Color = new SKColor(0, 0, 0, 225) })
            canvas.DrawRect(0, bitmap.Height - band, bitmap.Width, band, shade);
        using (var line = new SKPaint { Color = SKColors.White, StrokeWidth = Math.Max(2f, band * 0.05f) })
            canvas.DrawLine(0, bitmap.Height - band, bitmap.Width, bitmap.Height - band, line);

        var label = $"VOL. {volume:D2}";
        using var typeface = SKTypeface.FromFamilyName(null, SKFontStyle.Bold) ?? SKTypeface.Default;
        using var font = new SKFont(typeface, band * 0.62f);
        var width = font.MeasureText(label);
        if (width > bitmap.Width * 0.9f)
            font.Size *= bitmap.Width * 0.9f / width;
        using var ink = new SKPaint { Color = SKColors.White, IsAntialias = true };
        var baseline = bitmap.Height - band / 2 - (font.Metrics.Ascent + font.Metrics.Descent) / 2;
        canvas.DrawText(label, bitmap.Width / 2f, baseline, SKTextAlign.Center, font, ink);

        using var image = surface.Snapshot();
        using var encoded = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return encoded?.ToArray() ?? cover;
    }
}
