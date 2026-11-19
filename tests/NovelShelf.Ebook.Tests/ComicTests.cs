using NovelShelf.Comics;
using SkiaSharp;

namespace NovelShelf.Ebook.Tests;

public sealed class ComicTests
{
    private static readonly KindleProfile Paperwhite = KindleProfile.Default;

    private static byte[] Image(int width, int height, Action<SKCanvas> draw, SKEncodedImageFormat format = SKEncodedImageFormat.Png)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.White);
            draw(canvas);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(format, 90);
        return data.ToArray();
    }

    private static SKBitmap Decode(byte[] jpeg) => SKBitmap.Decode(jpeg);

    private static (int Left, int Right) DarkColumns(SKBitmap page)
    {
        int left = page.Width, right = -1;
        var y = page.Height / 2;
        for (var x = 0; x < page.Width; x++)
        {
            if (page.GetPixel(x, y).Red < 100)
            {
                left = Math.Min(left, x);
                right = Math.Max(right, x);
            }
        }
        return (left, right);
    }

    private static double MeanLuma(SKBitmap page)
    {
        long sum = 0;
        for (var y = 0; y < page.Height; y += 8)
            for (var x = 0; x < page.Width; x += 8)
                sum += page.GetPixel(x, y).Red;
        return (double)sum / ((page.Height + 7) / 8 * ((page.Width + 7) / 8));
    }

    [Fact]
    public void Page_is_cropped_to_content_and_fitted_to_the_kindle_screen()
    {
        var page = Image(1000, 1500, c => c.DrawRect(200, 300, 600, 900, new SKPaint { Color = SKColors.Black }));

        var output = ComicPageProcessor.ProcessPage(page, new ComicPageOptions(Paperwhite));

        var jpeg = Assert.Single(output);
        using var result = Decode(jpeg);
        Assert.Equal(1236, result.Width);
        Assert.Equal(1648, result.Height);
        var (left, right) = DarkColumns(result);
        Assert.True(right - left > 1000, $"conteúdo ocupa {right - left}px de largura; sem corte seriam 659px");
    }

    [Fact]
    public void Page_is_grayscale_jpeg_for_e_ink_profiles()
    {
        var page = Image(800, 1200, c => c.DrawRect(100, 100, 600, 1000, new SKPaint { Color = SKColors.Red }));

        var jpeg = Assert.Single(ComicPageProcessor.ProcessPage(page, new ComicPageOptions(Paperwhite)));

        using var stream = new SKMemoryStream(jpeg);
        using var codec = SKCodec.Create(stream);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        Assert.Equal(SKColorType.Gray8, codec.Info.ColorType);
    }

    [Fact]
    public void Page_without_uniform_border_is_not_cropped()
    {
        var page = Image(1000, 1500, c =>
        {
            c.Clear(new SKColor(128, 128, 128));
            c.DrawRect(450, 700, 100, 100, new SKPaint { Color = SKColors.Black });
        });

        using var result = Decode(Assert.Single(ComicPageProcessor.ProcessPage(page, new ComicPageOptions(Paperwhite))));

        var (left, right) = DarkColumns(result);
        Assert.InRange(right - left, 80, 160);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Wide_page_is_split_in_reading_order(bool rightToLeft)
    {
        var spread = Image(2000, 1000, c =>
        {
            c.Clear(new SKColor(200, 200, 200));
            c.DrawRect(0, 0, 1000, 1000, new SKPaint { Color = new SKColor(40, 40, 40) });
        });

        var output = ComicPageProcessor.ProcessPage(spread, new ComicPageOptions(Paperwhite, RightToLeft: rightToLeft));

        Assert.Equal(2, output.Count);
        using var first = Decode(output[0]);
        using var second = Decode(output[1]);
        Assert.Equal(rightToLeft, MeanLuma(first) > MeanLuma(second));
    }

    [Fact]
    public void Wide_page_is_kept_whole_when_splitting_is_off()
    {
        var spread = Image(2000, 1000, c => c.DrawRect(100, 100, 1800, 800, new SKPaint { Color = SKColors.Black }));

        var output = ComicPageProcessor.ProcessPage(spread, new ComicPageOptions(Paperwhite, Layout: new ComicLayout { WidePages = WidePageMode.Keep }));

        Assert.Single(output);
    }

    [Fact]
    public void Webtoon_strip_is_sliced_into_full_screen_pages_and_blank_gaps_are_dropped()
    {
        byte[] Panels() => Image(800, 4000, c =>
        {
            for (var y = 100; y < 4000; y += 700)
                c.DrawRect(50, y, 700, 500, new SKPaint { Color = SKColors.Black });
        });
        var blank = Image(800, 4000, _ => { });
        var pages = new[] { Panels(), Panels(), blank, Panels() };

        Assert.True(ComicPageProcessor.IsWebtoon(pages));
        var output = ComicPageProcessor.ProcessChapter(pages, new ComicPageOptions(Paperwhite), TestContext.Current.CancellationToken);

        var expectedWithoutBlank = 3 * 4000 * 1236 / 800 / 1648;
        Assert.InRange(output.Count, expectedWithoutBlank, expectedWithoutBlank * 2);
        foreach (var jpeg in output)
        {
            using var slice = Decode(jpeg);
            Assert.Equal((1236, 1648), (slice.Width, slice.Height));
            Assert.True(MeanLuma(slice) < 250, "fatia em branco deveria ter sido descartada");
        }
    }

    [Fact]
    public void Regular_pages_are_not_treated_as_webtoon()
    {
        var pages = Enumerable.Range(0, 5).Select(_ => Image(900, 1350, _ => { })).ToList();

        Assert.False(ComicPageProcessor.IsWebtoon(pages));
    }

    [Fact]
    public void Manual_margins_zoom_into_the_kept_area()
    {
        var page = Image(1000, 1500, c =>
        {
            c.Clear(new SKColor(128, 128, 128));
            c.DrawRect(450, 700, 100, 100, new SKPaint { Color = SKColors.Black });
        });
        var layout = new ComicLayout { Margins = MarginMode.Manual, MarginLeft = 0.25, MarginRight = 0.25, MarginTop = 0.25, MarginBottom = 0.25 };

        using var result = Decode(Assert.Single(ComicPageProcessor.ProcessPage(page, new ComicPageOptions(Paperwhite, Layout: layout))));

        var (left, right) = DarkColumns(result);
        Assert.InRange(right - left, 200, 280);
    }

    [Fact]
    public void Wide_page_can_be_rotated_to_fill_the_screen()
    {
        var spread = Image(2000, 1000, c => c.DrawRect(0, 0, 2000, 1000, new SKPaint { Color = SKColors.Black }));
        var layout = new ComicLayout { Margins = MarginMode.Off, WidePages = WidePageMode.Rotate };

        using var result = Decode(Assert.Single(ComicPageProcessor.ProcessPage(spread, new ComicPageOptions(Paperwhite, Layout: layout))));

        var dark = Enumerable.Range(0, result.Height).Count(y => result.GetPixel(result.Width / 2, y).Red < 100);
        Assert.True(dark > 1600, $"página girada ocupa {dark}px de altura; sem girar seriam 618px");
    }

    [Fact]
    public void Mode_overrides_webtoon_detection()
    {
        var tall = Enumerable.Range(0, 2).Select(_ => Image(800, 4000, c => c.DrawRect(50, 100, 700, 3800, new SKPaint { Color = SKColors.Black }))).ToList();

        var asPages = ComicPageProcessor.ProcessChapter(tall, new ComicPageOptions(Paperwhite, Layout: new ComicLayout { Mode = ComicReadingMode.Pages }), TestContext.Current.CancellationToken);
        var asStrip = ComicPageProcessor.ProcessChapter(tall, new ComicPageOptions(Paperwhite, Layout: new ComicLayout { Mode = ComicReadingMode.Strip }), TestContext.Current.CancellationToken);

        Assert.Equal(2, asPages.Count);
        Assert.True(asStrip.Count > 4);
    }

    [Fact]
    public void Overlap_repeats_the_end_of_a_slice_at_the_top_of_the_next()
    {
        var strip = Image(800, 6000, c =>
        {
            for (var y = 0; y < 6000; y += 40)
                c.DrawRect(0, y, 800, 20, new SKPaint { Color = new SKColor((byte)(y / 40 % 200), 0, 0) });
        });
        var plain = ComicPageProcessor.ProcessStrip([strip], new ComicPageOptions(Paperwhite, Layout: new ComicLayout { CutSearch = 0 }), TestContext.Current.CancellationToken);
        var overlapped = ComicPageProcessor.ProcessStrip([strip], new ComicPageOptions(Paperwhite, Layout: new ComicLayout { CutSearch = 0, Overlap = 0.2 }), TestContext.Current.CancellationToken);

        Assert.True(overlapped.Count > plain.Count);
        using var first = Decode(overlapped[0]);
        using var second = Decode(overlapped[1]);
        var tail = first.Height - (int)(first.Height * 0.2) + 5;
        Assert.InRange(Math.Abs(first.GetPixel(400, tail).Red - second.GetPixel(400, 5).Red), 0, 6);
    }

    [Fact]
    public void Gamma_above_one_darkens_mid_tones()
    {
        var page = Image(900, 1200, c => c.Clear(new SKColor(150, 150, 150)));
        var off = new ComicLayout { Margins = MarginMode.Off };

        using var normal = Decode(Assert.Single(ComicPageProcessor.ProcessPage(page, new ComicPageOptions(Paperwhite, Layout: off))));
        using var darker = Decode(Assert.Single(ComicPageProcessor.ProcessPage(page, new ComicPageOptions(Paperwhite, Layout: off with { Gamma = 1.8 }))));

        Assert.True(MeanLuma(darker) < MeanLuma(normal) - 30);
    }

    [Fact]
    public void Layout_survives_a_json_round_trip_and_bad_values_are_clamped()
    {
        var layout = new ComicLayout { Mode = ComicReadingMode.Strip, Margins = MarginMode.Manual, MarginLeft = 0.1, Overlap = 0.15, Gamma = 1.4 };

        Assert.Equal(layout, ComicLayout.FromJson(layout.ToJson()));
        Assert.Equal(ComicLayout.Default, ComicLayout.FromJson(null));
        Assert.Equal(ComicLayout.Default, ComicLayout.FromJson("{quebrado"));
        Assert.Equal(0.4, ComicLayout.FromJson("""{"MarginLeft":9,"Gamma":99}""").MarginLeft);
    }

    [Fact]
    public void Comic_book_is_fixed_layout_kf8_with_one_image_per_page()
    {
        var page = Image(600, 900, c => c.DrawRect(50, 50, 500, 800, new SKPaint { Color = SKColors.Black }), SKEncodedImageFormat.Jpeg);
        var chapters = new[]
        {
            new ComicChapter("Capítulo 1", [page, page, page]),
            new ComicChapter("Capítulo 2", [page, page]),
        };
        var info = new ComicBookInfo("Invencível", "Robert Kirkman", "Teste", "pt-BR",
            new DateTimeOffset(2026, 11, 19, 0, 0, 0, TimeSpan.Zero), 0xBEEF, CoverJpeg: null);

        var book = ComicBookBuilder.Build(info, chapters, Paperwhite, rightToLeft: true);
        using var ms = new MemoryStream();
        book.Write(ms);
        var reader = new Kf8Reader(ms.ToArray());

        Assert.Equal("true", reader.ExthString(122));
        Assert.Equal("1236x1648", reader.ExthString(307));
        Assert.Equal("rtl", reader.ExthString(527));
        Assert.Equal("6", reader.Exth[125].Single().Aggregate(0, (n, b) => n << 8 | b).ToString());

        var parts = reader.RebuildParts();
        Assert.Equal(5, parts.Count);
        Assert.Contains("kindle:embed:0001?mime=image/jpeg", parts[0]);
        Assert.Contains("kindle:embed:0005?mime=image/jpeg", parts[4]);
        Assert.All(parts, p => Assert.Contains("<meta name=\"viewport\" content=\"width=1236, height=1648\"/>", p));

        var ncx = reader.ReadIndex(reader.NcxIndex);
        Assert.Equal(2, ncx.Count);
    }
}
