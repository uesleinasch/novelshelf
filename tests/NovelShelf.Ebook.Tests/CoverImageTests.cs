using NovelShelf.Comics;
using SkiaSharp;

namespace NovelShelf.Ebook.Tests;

public sealed class CoverImageTests
{
    private static byte[] Encode(SKEncodedImageFormat format)
    {
        using var bitmap = new SKBitmap(40, 60);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(SKColors.DarkRed);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    [Fact]
    public void ToJpeg_converts_webp_covers()
    {
        var jpeg = CoverImage.ToJpeg(Encode(SKEncodedImageFormat.Webp));

        Assert.NotNull(jpeg);
        Assert.True(CoverImage.IsJpeg(jpeg));
        using var decoded = SKBitmap.Decode(jpeg);
        Assert.Equal(40, decoded.Width);
        Assert.Equal(60, decoded.Height);
    }

    [Fact]
    public void ToJpeg_keeps_jpeg_bytes_and_rejects_garbage()
    {
        var original = Encode(SKEncodedImageFormat.Jpeg);

        Assert.Same(original, CoverImage.ToJpeg(original));
        Assert.Null(CoverImage.ToJpeg([1, 2, 3, 4, 5]));
    }
}
