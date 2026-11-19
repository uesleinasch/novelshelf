using NovelShelf.Comics;
using SkiaSharp;

namespace NovelShelf.Ebook.Tests;

public sealed class CoverImageTests
{
    private static byte[] Encode(SKEncodedImageFormat format, int width = 40, int height = 60, SKColor? color = null)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(color ?? SKColors.DarkRed);
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

    [Fact]
    public void Volume_badge_darkens_the_bottom_and_keeps_the_top_and_size()
    {
        var cover = Encode(SKEncodedImageFormat.Jpeg, 600, 900, SKColors.LightSkyBlue);

        var badged = CoverImage.WithVolumeBadge(cover, 3);

        Assert.True(CoverImage.IsJpeg(badged));
        using var decoded = SKBitmap.Decode(badged);
        Assert.Equal((600, 900), (decoded.Width, decoded.Height));
        Assert.True(decoded.GetPixel(20, 880).Red < 60);
        Assert.True(decoded.GetPixel(300, 100).Blue > 200);
        var white = 0;
        for (var x = 150; x < 450; x++)
            for (var y = 790; y < 890; y++)
                if (decoded.GetPixel(x, y).Red > 200)
                    white++;
        Assert.True(white > 500, $"texto do volume não apareceu ({white} pixels claros)");

        if (Environment.GetEnvironmentVariable("NOVELSHELF_DUMP_DIR") is { Length: > 0 } dump)
            File.WriteAllBytes(Path.Combine(dump, "capa-volume-exemplo.jpg"), badged);
    }

    [Fact]
    public void Volume_badge_leaves_invalid_images_alone()
    {
        byte[] garbage = [1, 2, 3, 4, 5];

        Assert.Same(garbage, CoverImage.WithVolumeBadge(garbage, 1));
    }
}
