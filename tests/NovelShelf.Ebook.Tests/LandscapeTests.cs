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
    public void Manual_lines_replace_detection_for_that_page()
    {
        var page = Tiers(1000, (50, 450), (500, 950), (1000, 1450));

        var plan = ComicPageProcessor.PlanLandscape(page, Options(Landscape), new PageCuts { Rows = [0.5] })!;
        var screens = ComicPageProcessor.ProcessPage(page, Options(Landscape), new PageCuts { Rows = [0.5] });
        var whole = ComicPageProcessor.PlanLandscape(page, Options(Landscape), new PageCuts { Columns = [new VerticalCut(0.5, 0.5)] })!;

        Assert.Equal(PageCutKind.Manual, plan.Kind);
        Assert.Equal([new RowRange(0, 750), new RowRange(750, 1500)], plan.Screens);
        Assert.False(plan.IsSuspect);
        Assert.Equal(2, screens.Count);
        Assert.Equal(new RowRange(0, 1500), Assert.Single(whole.Screens));
    }

    [Fact]
    public void Manual_lines_are_fractions_of_the_original_page_even_after_margin_crop()
    {
        var page = Tiers(1000, (200, 600), (650, 1050));
        var layout = Landscape with { Margins = MarginMode.Auto };

        var plan = ComicPageProcessor.PlanLandscape(page, Options(layout), new PageCuts { Rows = [625.0 / 1100] })!;

        Assert.True(plan.CropTop > 100);
        Assert.Equal(1100, plan.SourceHeight);
        Assert.Equal(2, plan.Screens.Count);
        Assert.Equal(625, plan.CropTop + plan.Screens[0].Bottom);
        Assert.Equal(625.0 / 1100, plan.ToSourceFraction(plan.Screens[0].Bottom), 3);
    }

    [Fact]
    public void Clean_tiers_are_trusted_and_doubtful_pages_are_flagged()
    {
        var clean = ComicPageProcessor.PlanLandscape(Tiers(1000, (50, 450), (500, 950), (1000, 1450)), Options(Landscape))!;
        var splash = ComicPageProcessor.PlanLandscape(Page(1000, 1500, c => c.DrawRect(0, 0, 1000, 1500, Fill(120))), Options(Landscape))!;
        var tiny = ComicPageProcessor.PlanLandscape(Tiers(1000, (50, 650), (700, 800), (850, 1450)), Options(Landscape with { MergeBands = 0 }))!;
        var huge = ComicPageProcessor.PlanLandscape(Tiers(1000, (50, 950), (1000, 1400)), Options(Landscape with { TallBand = 2.5 }))!;

        Assert.Equal(PageSuspicion.None, clean.Suspicion);
        Assert.Equal(1.0, clean.Confidence);
        Assert.Equal(PageSuspicion.NoClearGutter, splash.Suspicion);
        Assert.True(tiny.Suspicion.HasFlag(PageSuspicion.TinyPiece));
        Assert.True(huge.Suspicion.HasFlag(PageSuspicion.HugePiece));
        Assert.True(splash.Confidence < clean.Confidence);
    }

    [Fact]
    public void Page_cuts_survive_json_and_drop_lines_on_the_edges()
    {
        var cuts = new PageCuts { Rows = [0.7, 0.2, 0.2, 1.0, 0], Columns = [new VerticalCut(0.4, 0.1), new VerticalCut(1.2, 0.1)] };

        var loaded = PageCuts.FromJson(cuts.ToJson())!;

        Assert.Equal([0.2, 0.7], loaded.Rows);
        Assert.Equal([new VerticalCut(0.4, 0.1)], loaded.Columns);
        Assert.Equal([(0.0, 0.2), (0.2, 0.7), (0.7, 1.0)], loaded.Bands());
        Assert.Null(PageCuts.FromJson("lixo"));
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

    [Fact]
    public void Cached_pages_are_reused_and_a_new_cut_converts_only_that_page()
    {
        byte[][] pages = [Tiers(800, (50, 400), (450, 800)), Tiers(800, (50, 600)), Tiers(800, (50, 300), (350, 700))];
        var cache = new CountingStore();
        var options = Options(Landscape);

        var first = ComicPageProcessor.ProcessChapter(pages, options, TestContext.Current.CancellationToken, cache: cache);
        var saved = cache.Saves;
        var again = ComicPageProcessor.ProcessChapter(pages, options, TestContext.Current.CancellationToken, cache: cache);
        var cuts = new Dictionary<int, PageCuts> { [1] = new() { Rows = [0.5] } };
        var edited = ComicPageProcessor.ProcessChapter(pages, options, TestContext.Current.CancellationToken, cuts, cache);

        Assert.Equal(3, saved);
        Assert.Equal(first, again);
        Assert.Equal(4, cache.Saves);
        Assert.Equal(first.Count + 1, edited.Count);
    }

    [Fact]
    public void Settings_change_the_fingerprint()
    {
        var plain = Options(Landscape);

        Assert.Equal(plain.Fingerprint, Options(Landscape with { }).Fingerprint);
        Assert.NotEqual(plain.Fingerprint, Options(Landscape with { Gamma = 1.4 }).Fingerprint);
        Assert.NotEqual(plain.Fingerprint, new ComicPageOptions(KindleProfile.Default, Layout: Landscape).Fingerprint);
        Assert.NotEqual(plain.Fingerprint, (plain with { RightToLeft = true }).Fingerprint);
    }

    private static byte[] Grid() => Page(800, 1200, c =>
    {
        var border = new SKPaint { Color = SKColors.Black, Style = SKPaintStyle.Stroke, StrokeWidth = 4 };
        foreach (var (left, top) in new[] { (40, 40), (410, 40), (40, 620), (410, 620) })
        {
            c.DrawRect(left, top, 350, 540, border);
            c.DrawCircle(left + 175, top + 270, 60, Fill(60));
        }
    });

    private static SKPath Polygon(params (float X, float Y)[] points)
    {
        var path = new SKPath();
        path.AddPoly(points.Select(p => new SKPoint(p.X, p.Y)).ToArray());
        return path;
    }

    [Fact]
    public void Bordered_panels_are_found_in_reading_order()
    {
        var options = Options(Landscape);

        var panels = ComicPageProcessor.DetectPanels(Grid(), options);
        var manga = ComicPageProcessor.DetectPanels(Grid(), options with { RightToLeft = true });

        Assert.Equal(4, panels.Count);
        (double Left, double Top)[] expected = [(0.05, 0.03), (0.51, 0.03), (0.05, 0.52), (0.51, 0.52)];
        Assert.All(expected.Zip(panels), pair =>
        {
            Assert.InRange(pair.Second.Left, pair.First.Left - 0.02, pair.First.Left + 0.02);
            Assert.InRange(pair.Second.Top, pair.First.Top - 0.02, pair.First.Top + 0.02);
        });
        Assert.Equal([panels[1], panels[0], panels[3], panels[2]], manga);
    }

    [Fact]
    public void Slanted_gutter_is_cut_along_the_panel_rows()
    {
        var page = Page(800, 1200, c =>
        {
            c.DrawPath(Polygon((20, 40), (780, 40), (780, 580), (20, 620)), Fill(90));
            c.DrawPath(Polygon((20, 650), (780, 610), (780, 1160), (20, 1160)), Fill(90));
        });

        var plan = ComicPageProcessor.PlanLandscape(page, Options(Landscape))!;

        Assert.Equal(PageCutKind.PanelRows, plan.Kind);
        Assert.False(plan.IsSuspect);
        Assert.Equal(2, plan.Screens.Count);
        Assert.InRange(plan.Screens[0].Bottom, 600, 660);
    }

    [Fact]
    public void Splash_page_is_a_single_panel()
    {
        var page = Page(800, 1200, c => c.DrawRect(40, 40, 720, 1120, Fill(90)));

        var panels = ComicPageProcessor.DetectPanels(page, Options(Landscape));
        var plan = ComicPageProcessor.PlanLandscape(page, Options(Landscape))!;

        Assert.Single(panels);
        Assert.Equal(PageCutKind.Whole, plan.Kind);
    }

    private sealed class CountingStore : IConvertedPageStore
    {
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (byte[] Key, List<byte[]> Screens)> _entries = [];
        private int _saves;

        public int Saves => _saves;

        public List<byte[]>? Find(string slot, byte[] key) =>
            _entries.TryGetValue(slot, out var entry) && entry.Key.AsSpan().SequenceEqual(key) ? entry.Screens : null;

        public void Save(string slot, byte[] key, IReadOnlyList<byte[]> screens)
        {
            _entries[slot] = (key, screens.ToList());
            Interlocked.Increment(ref _saves);
        }
    }
}
