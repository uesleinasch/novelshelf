using NovelShelf.Comics;
using SkiaSharp;

namespace NovelShelf.Ebook.Tests;

public sealed class LandscapeTests
{
    private static readonly KindleProfile Basic = KindleProfile.Get("basic");

    private static readonly ComicLayout Landscape = new()
    {
        Mode = ComicReadingMode.Landscape,
        Margins = MarginMode.Off,
        Sharpen = false,
    };

    private static ComicPageOptions Options(ComicLayout layout) => new(Basic, Layout: layout);

    private static byte[] Page(int width, int height, Action<SKCanvas> draw, SKColor? background = null)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(background ?? SKColors.White);
            draw(canvas);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static SKPaint Fill(byte gray) => new() { Color = new SKColor(gray, gray, gray) };

    private static byte[] Tiers(int width, params (int Top, int Bottom)[] tiers) =>
        Page(width, tiers.Max(t => t.Bottom) + 50, c =>
        {
            foreach (var (top, bottom) in tiers)
                c.DrawRect(40, top, width - 80, bottom - top, Fill(90));
        });

    private static double MeanLuma(SKBitmap image, int top, int bottom)
    {
        long sum = 0;
        var count = 0;
        for (var y = top; y < bottom; y += 4)
            for (var x = 0; x < image.Width; x += 4, count++)
                sum += image.GetPixel(x, y).Red;
        return (double)sum / count;
    }

    [Fact]
    public void Each_tier_between_white_gutters_becomes_one_screen()
    {
        var page = Tiers(1000, (50, 450), (500, 950), (1000, 1450));

        var plan = ComicPageProcessor.PlanLandscape(page, Options(Landscape))!;

        Assert.Equal(PageCutKind.Bands, plan.Kind);
        Assert.Equal(3, plan.Screens.Count);
        Assert.All(plan.Screens.Zip([50, 500, 1000]), p => Assert.InRange(p.First.Top, p.Second - 10, p.Second));
    }

    [Fact]
    public void Black_gutters_split_tiers_too()
    {
        var page = Page(1000, 1000, c =>
        {
            c.DrawRect(40, 40, 920, 420, Fill(160));
            c.DrawRect(40, 540, 920, 420, Fill(160));
        }, SKColors.Black);

        var plan = ComicPageProcessor.PlanLandscape(page, Options(Landscape))!;

        Assert.Equal(PageCutKind.Bands, plan.Kind);
        Assert.Equal(2, plan.Screens.Count);
    }

    [Fact]
    public void Short_neighbor_tiers_share_a_screen_until_the_limit()
    {
        var page = Tiers(1000, (50, 350), (400, 700), (750, 1050), (1100, 1400));

        var merged = ComicPageProcessor.PlanLandscape(page, Options(Landscape))!;
        var apart = ComicPageProcessor.PlanLandscape(page, Options(Landscape with { MergeBands = 0 }))!;

        Assert.Equal(2, merged.Screens.Count);
        Assert.Equal(4, apart.Screens.Count);
    }

    [Fact]
    public void Tall_tier_is_cut_in_two_with_overlap()
    {
        var page = Tiers(1000, (50, 1100), (1150, 1450));

        var plan = ComicPageProcessor.PlanLandscape(page, Options(Landscape))!;

        Assert.Equal(3, plan.Screens.Count);
        var (first, second) = (plan.Screens[0], plan.Screens[1]);
        Assert.True(second.Top < first.Bottom, "as duas metades da faixa alta precisam se sobrepor");
        Assert.InRange(first.Top, 40, 50);
        Assert.InRange(second.Bottom, 1100, 1110);
    }

    [Fact]
    public void Page_without_any_gutter_is_a_splash_kept_whole()
    {
        var page = Page(1000, 1500, c => c.DrawRect(0, 0, 1000, 1500, Fill(120)));

        var plan = ComicPageProcessor.PlanLandscape(page, Options(Landscape))!;
        var screens = ComicPageProcessor.ProcessPage(page, Options(Landscape with { PageBeforeBands = true }));

        Assert.Equal(PageCutKind.Whole, plan.Kind);
        Assert.Equal(new RowRange(0, 1500), Assert.Single(plan.Screens));
        Assert.Single(screens);
    }

    [Fact]
    public void Page_with_only_a_broken_gutter_falls_back_to_equal_slices()
    {
        var page = Page(1000, 1500, c =>
        {
            c.DrawRect(0, 0, 1000, 720, Fill(120));
            c.DrawRect(0, 780, 1000, 720, Fill(120));
            c.DrawRect(450, 700, 100, 100, Fill(120));
        });

        var plan = ComicPageProcessor.PlanLandscape(page, Options(Landscape))!;

        Assert.Equal(PageCutKind.Slices, plan.Kind);
        Assert.Equal(2, plan.Screens.Count);
        Assert.Equal(0, plan.Screens[0].Top);
        Assert.Equal(1500, plan.Screens[1].Bottom);
        Assert.True(plan.Screens[1].Top < plan.Screens[0].Bottom);
    }

    [Theory]
    [InlineData(LandscapeTurn.Clockwise, true)]
    [InlineData(LandscapeTurn.CounterClockwise, false)]
    public void Screens_are_saved_already_turned_to_the_chosen_side(LandscapeTurn turn, bool darkOnTop)
    {
        var page = Page(1000, 1000, c =>
        {
            c.DrawRect(0, 50, 500, 400, Fill(0));
            c.DrawRect(500, 50, 500, 400, Fill(170));
            c.DrawRect(0, 550, 1000, 400, Fill(170));
        });

        var screens = ComicPageProcessor.ProcessPage(page, Options(Landscape with { Turn = turn }));

        Assert.Equal(2, screens.Count);
        using var first = SKBitmap.Decode(screens[0]);
        Assert.Equal((Basic.Width, Basic.Height), (first.Width, first.Height));
        var top = MeanLuma(first, 0, first.Height / 2);
        var bottom = MeanLuma(first, first.Height / 2, first.Height);
        Assert.Equal(darkOnTop, top < bottom);
    }

    [Fact]
    public void Whole_page_can_come_before_its_pieces()
    {
        var page = Tiers(1000, (50, 450), (500, 950), (1000, 1450));

        var screens = ComicPageProcessor.ProcessPage(page, Options(Landscape with { PageBeforeBands = true }));

        Assert.Equal(4, screens.Count);
    }

    [Fact]
    public void Auto_mode_never_picks_landscape()
    {
        var page = Tiers(1000, (50, 450), (500, 950), (1000, 1450));

        var screens = ComicPageProcessor.ProcessChapter([page], new ComicPageOptions(Basic, Layout: Landscape with { Mode = ComicReadingMode.Auto }),
            TestContext.Current.CancellationToken);

        Assert.Single(screens);
    }

    [Fact]
    public void Sharpen_stretches_levels_for_e_ink()
    {
        var page = Page(618, 824, c =>
        {
            c.DrawRect(0, 0, 309, 824, Fill(90));
            c.DrawRect(309, 0, 309, 824, Fill(170));
        });
        var layout = new ComicLayout { Mode = ComicReadingMode.Pages, Margins = MarginMode.Off };

        using var plain = SKBitmap.Decode(ComicPageProcessor.ProcessPage(page, new ComicPageOptions(KindleProfile.Default, Layout: layout with { Sharpen = false }))[0]);
        using var sharp = SKBitmap.Decode(ComicPageProcessor.ProcessPage(page, new ComicPageOptions(KindleProfile.Default, Layout: layout))[0]);

        var y = plain.Height / 2;
        Assert.InRange(plain.GetPixel(plain.Width / 4, y).Red, 80, 100);
        Assert.InRange(sharp.GetPixel(sharp.Width / 4, y).Red, 0, 20);
        Assert.InRange(sharp.GetPixel(sharp.Width * 3 / 4, y).Red, 235, 255);
    }

    [Fact]
    public void Landscape_settings_survive_json_and_are_clamped()
    {
        var layout = new ComicLayout
        {
            Mode = ComicReadingMode.Landscape,
            Turn = LandscapeTurn.CounterClockwise,
            TallBand = 9,
            MergeBands = -1,
            PageBeforeBands = true,
            Sharpen = false,
        };

        var loaded = ComicLayout.FromJson(layout.ToJson());

        Assert.Equal(layout.Normalized(), loaded);
        Assert.Equal(2.5, loaded.TallBand);
        Assert.Equal(0, loaded.MergeBands);
        Assert.True(ComicLayout.FromJson("""{"Mode":"Pages"}""").Sharpen);
    }
}
