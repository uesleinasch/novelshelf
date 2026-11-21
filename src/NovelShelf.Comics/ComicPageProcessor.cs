using SkiaSharp;

namespace NovelShelf.Comics;

public sealed record ComicPageOptions(
    KindleProfile Profile,
    bool RightToLeft = false,
    ComicLayout? Layout = null,
    int JpegQuality = 85)
{
    // Bump when the same page and settings start producing different screens, or stale cached pages keep being used.
    public const int PipelineVersion = 2;

    public ComicLayout EffectiveLayout => (Layout ?? ComicLayout.Default).Normalized();

    public string Fingerprint =>
        $"{PipelineVersion}|{Profile.Width}x{Profile.Height}|{Profile.Color}|{RightToLeft}|{JpegQuality}|{EffectiveLayout.ToJson()}";
}

public static class ComicPageProcessor
{
    private const double WebtoonAspect = 2.0;
    private const int BackgroundTolerance = 48;
    private const int UniformRowRange = 12;

    private static int Workers => Math.Max(1, Environment.ProcessorCount - 1);

    public static bool IsWebtoon(IReadOnlyList<byte[]> pages)
    {
        var ratios = pages.Select(Raster.Measure)
            .Where(d => d.Width > 0)
            .Select(d => (double)d.Height / d.Width)
            .Order()
            .ToList();
        return ratios.Count > 0 && ratios[ratios.Count / 2] >= WebtoonAspect;
    }

    public static bool UsesStrip(IReadOnlyList<byte[]> pages, ComicLayout layout) => layout.Mode switch
    {
        ComicReadingMode.Strip => true,
        ComicReadingMode.Pages => false,
        _ => IsWebtoon(pages),
    };

    public static List<byte[]> ProcessChapter(IReadOnlyList<byte[]> pages, ComicPageOptions options, CancellationToken ct = default,
        IReadOnlyDictionary<int, PageCuts>? cuts = null, IConvertedPageStore? cache = null)
    {
        if (options.EffectiveLayout.Mode != ComicReadingMode.Landscape && UsesStrip(pages, options.EffectiveLayout))
            return Cached(cache, "strip", () => ConvertedPageKey.Strip(pages), () => ProcessStrip(pages, options, ct));
        return ProcessChapterByPage(pages, options, cuts, ct, cache).SelectMany(r => r).ToList();
    }

    public static List<byte[]>[] ProcessChapterByPage(IReadOnlyList<byte[]> pages, ComicPageOptions options,
        IReadOnlyDictionary<int, PageCuts>? cuts = null, CancellationToken ct = default, IConvertedPageStore? cache = null)
    {
        var landscape = options.EffectiveLayout.Mode == ComicReadingMode.Landscape;
        var results = new List<byte[]>[pages.Count];
        Parallel.For(0, pages.Count, new ParallelOptions { MaxDegreeOfParallelism = Workers, CancellationToken = ct }, i =>
        {
            var cut = landscape ? cuts?.GetValueOrDefault(i) : null;
            results[i] = Cached(cache, i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                () => ConvertedPageKey.Page(pages[i], cut), () => ProcessPage(pages[i], options, cut));
        });
        return results;
    }

    private static List<byte[]> Cached(IConvertedPageStore? cache, string slot, Func<byte[]> key, Func<List<byte[]>> convert)
    {
        if (cache is null)
            return convert();
        var id = key();
        if (cache.Find(slot, id) is { } hit)
            return hit;
        var screens = convert();
        cache.Save(slot, id, screens);
        return screens;
    }

    public static List<byte[]> ProcessPage(byte[] data, ComicPageOptions options, PageCuts? cuts = null)
    {
        var layout = options.EffectiveLayout;
        var source = Raster.Decode(data, options.Profile.Color);
        if (source is null)
            return [];

        var bounds = MarginBounds(source, layout);
        var cropped = source.Crop(bounds);
        if (layout.Mode == ComicReadingMode.Landscape)
            return ProcessLandscape(cropped, options, ManualRows(cuts, bounds, source.Height));
        if (cropped.Width > cropped.Height * 1.05 && layout.WidePages != WidePageMode.Keep)
        {
            if (layout.WidePages == WidePageMode.Rotate)
                return [FitAndEncode(cropped.Rotate(options.RightToLeft), options)];
            var half = cropped.Width / 2;
            var left = cropped.Crop(new SKRectI(0, 0, half, cropped.Height));
            var right = cropped.Crop(new SKRectI(half, 0, cropped.Width, cropped.Height));
            return options.RightToLeft
                ? [FitAndEncode(right, options), FitAndEncode(left, options)]
                : [FitAndEncode(left, options), FitAndEncode(right, options)];
        }
        return [FitAndEncode(cropped, options)];
    }

    public static PageCut? PlanLandscape(byte[] data, ComicPageOptions options, PageCuts? cuts = null)
    {
        var source = Raster.Decode(data, options.Profile.Color);
        if (source is null)
            return null;
        var bounds = MarginBounds(source, options.EffectiveLayout);
        var plan = LandscapeCutter.Plan(source.Crop(bounds), options.Profile, options.EffectiveLayout, ManualRows(cuts, bounds, source.Height));
        return plan with { SourceWidth = source.Width, SourceHeight = source.Height, CropLeft = bounds.Left, CropTop = bounds.Top };
    }

    public static IReadOnlyList<PanelFrame> DetectPanels(byte[] data, ComicPageOptions options)
    {
        var source = Raster.Decode(data, color: false);
        if (source is null)
            return [];
        var bounds = MarginBounds(source, options.EffectiveLayout);
        var layout = PanelDetector.Detect(source.Crop(bounds), options.RightToLeft);
        return layout.Panels.Select(p => new PanelFrame(
                (double)(bounds.Left + p.Left) / source.Width, (double)(bounds.Top + p.Top) / source.Height,
                (double)(bounds.Left + p.Right) / source.Width, (double)(bounds.Top + p.Bottom) / source.Height))
            .ToList();
    }

    private static IReadOnlyList<int>? ManualRows(PageCuts? cuts, SKRectI bounds, int sourceHeight) =>
        cuts is null || cuts.Rows.Count == 0 && cuts.Columns.Count == 0
            ? null
            : cuts.Rows.Select(r => (int)Math.Round(r * sourceHeight) - bounds.Top).ToList();

    private static SKRectI MarginBounds(Raster source, ComicLayout layout) => layout.Margins switch
    {
        MarginMode.Auto => ContentBounds(source),
        MarginMode.Manual => ManualBounds(source.Width, source.Height, layout),
        _ => new SKRectI(0, 0, source.Width, source.Height),
    };

    private static List<byte[]> ProcessLandscape(Raster page, ComicPageOptions options, IReadOnlyList<int>? manualRows)
    {
        var plan = LandscapeCutter.Plan(page, options.Profile, options.EffectiveLayout, manualRows);
        var output = new List<byte[]>(plan.Screens.Count + 1);
        if (options.EffectiveLayout.PageBeforeBands && plan.Screens.Count > 1)
            output.Add(FitLandscape(page, options));
        foreach (var screen in plan.Screens)
            output.Add(FitLandscape(page.Crop(new SKRectI(0, screen.Top, page.Width, screen.Bottom)), options));
        return output;
    }

    private static byte[] FitLandscape(Raster region, ComicPageOptions options)
    {
        int screenWidth = options.Profile.Height, screenHeight = options.Profile.Width;
        var scale = Math.Min((double)screenWidth / region.Width, (double)screenHeight / region.Height);
        var width = Math.Clamp((int)Math.Round(region.Width * scale), 1, screenWidth);
        var height = Math.Clamp((int)Math.Round(region.Height * scale), 1, screenHeight);
        var landscape = Raster.Blank(screenWidth, screenHeight, region.Channels);
        landscape.Paste(region.Resize(width, height), (screenWidth - width) / 2, (screenHeight - height) / 2);
        var page = landscape.Rotate(counterClockwise: options.EffectiveLayout.Turn == LandscapeTurn.CounterClockwise);
        return Finish(page, options);
    }

    private static byte[] Finish(Raster page, ComicPageOptions options)
    {
        var layout = options.EffectiveLayout;
        if (layout.Sharpen)
            page.AutoLevel();
        page.ApplyGamma(layout.Gamma);
        if (layout.Sharpen)
            page.Sharpen();
        return page.EncodeJpeg(options.JpegQuality);
    }

    public static List<byte[]> ProcessStrip(IReadOnlyList<byte[]> pages, ComicPageOptions options, CancellationToken ct = default)
    {
        var profile = options.Profile;
        var layout = options.EffectiveLayout;
        var strip = new StripBuffer(profile.Width, profile.Color ? 4 : 1);
        var slices = new List<(byte[] Rows, int Count)>();
        var keep = (int)Math.Round(profile.Height * layout.Overlap);
        var minimumCut = (int)Math.Round(profile.Height * (1 - layout.CutSearch));
        var parallel = new ParallelOptions { MaxDegreeOfParallelism = Workers, CancellationToken = ct };

        foreach (var batch in pages.Chunk(Workers))
        {
            var prepared = new Raster?[batch.Length];
            Parallel.For(0, batch.Length, parallel, i => prepared[i] = PrepareStripImage(batch[i], profile, layout));
            foreach (var image in prepared)
            {
                if (image is null)
                    continue;
                strip.Append(image);
                while (true)
                {
                    strip.SkipLeadingWhite(UniformRowRange);
                    if (strip.Rows < profile.Height + profile.Height / 4)
                        break;
                    TakeSlice(strip, profile.Height, minimumCut, keep, slices);
                }
            }
        }
        while (true)
        {
            strip.SkipLeadingWhite(UniformRowRange);
            if (strip.Rows <= keep + profile.Height / 20)
                break;
            if (strip.Rows <= profile.Height)
            {
                var rows = strip.Rows;
                var last = strip.Take(rows, 0);
                if (!strip.IsBlank(last, UniformRowRange))
                    slices.Add((last, rows));
                break;
            }
            TakeSlice(strip, profile.Height, minimumCut, keep, slices);
        }

        var output = new byte[slices.Count][];
        Parallel.For(0, slices.Count, parallel, i => output[i] = EncodeSlice(slices[i].Rows, slices[i].Count, options));
        return [.. output];
    }

    public static byte[]? ProcessCover(byte[] data, KindleProfile profile)
    {
        var source = Raster.Decode(data, profile.Color);
        if (source is null)
            return null;
        var scale = Math.Max((double)profile.Width / source.Width, (double)profile.Height / source.Height);
        var width = Math.Max(profile.Width, (int)Math.Round(source.Width * scale));
        var height = Math.Max(profile.Height, (int)Math.Round(source.Height * scale));
        var filled = source.Resize(width, height);
        var left = (width - profile.Width) / 2;
        var top = (height - profile.Height) / 2;
        return filled.Crop(new SKRectI(left, top, left + profile.Width, top + profile.Height)).EncodeJpeg(90);
    }

    private static Raster? PrepareStripImage(byte[] data, KindleProfile profile, ComicLayout layout)
    {
        var source = Raster.Decode(data, profile.Color);
        if (source is null)
            return null;
        if (layout.Margins == MarginMode.Manual)
            source = source.Crop(ManualBounds(source.Width, source.Height, layout with { MarginTop = 0, MarginBottom = 0 }));
        var height = Math.Max(1, (int)Math.Round((double)source.Height * profile.Width / source.Width));
        return source.Resize(profile.Width, height);
    }

    private static void TakeSlice(StripBuffer strip, int target, int minimumCut, int keep, List<(byte[] Rows, int Count)> slices)
    {
        var cut = strip.FindCut(target, Math.Max(1, minimumCut), UniformRowRange);
        var slice = strip.Take(cut, Math.Min(keep, cut - 1));
        if (!strip.IsBlank(slice, UniformRowRange))
            slices.Add((slice, cut));
    }

    private static byte[] EncodeSlice(byte[] rows, int rowCount, ComicPageOptions options)
    {
        var profile = options.Profile;
        var page = Raster.Blank(profile.Width, profile.Height, profile.Color ? 4 : 1);
        Buffer.BlockCopy(rows, 0, page.Data, 0, rowCount * page.Stride);
        return Finish(page, options);
    }

    private static byte[] FitAndEncode(Raster source, ComicPageOptions options)
    {
        var profile = options.Profile;
        var scale = Math.Min((double)profile.Width / source.Width, (double)profile.Height / source.Height);
        var width = Math.Clamp((int)Math.Round(source.Width * scale), 1, profile.Width);
        var height = Math.Clamp((int)Math.Round(source.Height * scale), 1, profile.Height);
        var page = Raster.Blank(profile.Width, profile.Height, source.Channels);
        page.Paste(source.Resize(width, height), (profile.Width - width) / 2, (profile.Height - height) / 2);
        return Finish(page, options);
    }

    private static SKRectI ManualBounds(int width, int height, ComicLayout layout)
    {
        var left = (int)Math.Round(width * layout.MarginLeft);
        var right = width - (int)Math.Round(width * layout.MarginRight);
        var top = (int)Math.Round(height * layout.MarginTop);
        var bottom = height - (int)Math.Round(height * layout.MarginBottom);
        if (right - left < width / 10 || bottom - top < height / 10)
            return new SKRectI(0, 0, width, height);
        return new SKRectI(left, top, right, bottom);
    }

    private static SKRectI ContentBounds(Raster image)
    {
        int width = image.Width, height = image.Height;
        var full = new SKRectI(0, 0, width, height);

        long sum = 0;
        var count = 0;
        for (var x = 0; x < width; x++)
        {
            sum += image.Luma(x, 0) + image.Luma(x, height - 1);
            count += 2;
        }
        for (var y = 0; y < height; y++)
        {
            sum += image.Luma(0, y) + image.Luma(width - 1, y);
            count += 2;
        }
        var background = (int)(sum / count);
        if (background is > 60 and < 200)
            return full;

        bool IsContent(int x, int y) => Math.Abs(image.Luma(x, y) - background) > BackgroundTolerance;
        var minRow = Math.Max(1, width / 200);
        var minColumn = Math.Max(1, height / 200);

        bool RowHasContent(int y)
        {
            var n = 0;
            for (var x = 0; x < width; x++)
                if (IsContent(x, y) && ++n >= minRow)
                    return true;
            return false;
        }

        bool ColumnHasContent(int x, int top, int bottom)
        {
            var n = 0;
            for (var y = top; y < bottom; y++)
                if (IsContent(x, y) && ++n >= minColumn)
                    return true;
            return false;
        }

        var top = 0;
        while (top < height && !RowHasContent(top))
            top++;
        if (top == height)
            return full;
        var bottom = height;
        while (bottom > top && !RowHasContent(bottom - 1))
            bottom--;
        var left = 0;
        while (left < width && !ColumnHasContent(left, top, bottom))
            left++;
        var right = width;
        while (right > left && !ColumnHasContent(right - 1, top, bottom))
            right--;

        var padX = width / 100;
        var padY = height / 100;
        var bounds = new SKRectI(
            Math.Max(0, left - padX), Math.Max(0, top - padY),
            Math.Min(width, right + padX), Math.Min(height, bottom + padY));
        return bounds.Width < width / 2 || bounds.Height < height / 2 ? full : bounds;
    }
}
