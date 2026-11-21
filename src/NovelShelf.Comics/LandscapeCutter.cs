namespace NovelShelf.Comics;

public enum PageCutKind
{
    Bands,
    Slices,
    Whole,
    Manual,
}

[Flags]
public enum PageSuspicion
{
    None = 0,
    NoClearGutter = 1,
    TinyPiece = 2,
    HugePiece = 4,
}

public readonly record struct RowRange(int Top, int Bottom)
{
    public int Height => Bottom - Top;
}

public sealed record PageCut(PageCutKind Kind, int Width, int Height, IReadOnlyList<RowRange> Screens)
{
    public PageSuspicion Suspicion { get; init; }
    public int SourceWidth { get; init; }
    public int SourceHeight { get; init; }
    public int CropLeft { get; init; }
    public int CropTop { get; init; }

    public bool IsSuspect => Suspicion != PageSuspicion.None;

    public double Confidence =>
        1.0
        - (Suspicion.HasFlag(PageSuspicion.NoClearGutter) ? 0.5 : 0)
        - (Suspicion.HasFlag(PageSuspicion.TinyPiece) ? 0.25 : 0)
        - (Suspicion.HasFlag(PageSuspicion.HugePiece) ? 0.25 : 0);

    public double ToSourceFraction(int croppedRow) => SourceHeight == 0 ? 0 : (double)(CropTop + croppedRow) / SourceHeight;
}

internal static class LandscapeCutter
{
    private const int WhiteLevel = 215;
    private const int BlackLevel = 40;
    private const double CleanGutter = 0.985;
    private const double WeakGutter = 0.8;

    private const double TinyScreen = 0.25;
    private const double HugeScreen = 1.2;

    public static PageCut Plan(Raster page, KindleProfile profile, ComicLayout layout, IReadOnlyList<int>? manualRows = null)
    {
        if (manualRows is not null)
            return Manual(page, manualRows);
        var screenRows = (double)page.Width * profile.Width / profile.Height;
        var plan = Detect(page, screenRows, layout);
        var suspicion = plan.Kind is PageCutKind.Slices or PageCutKind.Whole ? PageSuspicion.NoClearGutter : PageSuspicion.None;
        if (plan.Screens.Any(s => s.Height < TinyScreen * screenRows))
            suspicion |= PageSuspicion.TinyPiece;
        if (plan.Kind != PageCutKind.Whole && plan.Screens.Any(s => s.Height > HugeScreen * screenRows))
            suspicion |= PageSuspicion.HugePiece;
        return plan with { Suspicion = suspicion };
    }

    private static PageCut Manual(Raster page, IReadOnlyList<int> rows)
    {
        var edges = rows.Where(r => r > 0 && r < page.Height).Order().Distinct().Prepend(0).Append(page.Height).ToList();
        var screens = edges.Zip(edges.Skip(1)).Select(e => new RowRange(e.First, e.Second)).ToList();
        return new PageCut(PageCutKind.Manual, page.Width, page.Height, screens);
    }

    private static PageCut Detect(Raster page, double screenRows, ComicLayout layout)
    {
        int width = page.Width, height = page.Height;
        var background = BackgroundShare(page);
        var minGutter = Math.Max(2, height / 250);

        var bands = new List<RowRange>();
        var top = 0;
        foreach (var gutter in Runs(background, 0, height, CleanGutter, minGutter))
        {
            if (gutter.Top > top)
                bands.Add(new RowRange(top, gutter.Top));
            top = gutter.Bottom;
        }
        if (top < height)
            bands.Add(new RowRange(top, height));
        bands = AbsorbSlivers(bands, Math.Max(minGutter, height / 40));

        if (bands.Count == 0)
            return new PageCut(PageCutKind.Whole, width, height, [new RowRange(0, height)]);

        if (bands.Count == 1)
        {
            var only = bands[0];
            var weak = Runs(background, only.Top, only.Bottom, WeakGutter, minGutter)
                .Any(run => (run.Top + run.Bottom) / 2 is var middle
                    && middle > only.Top + only.Height * 15 / 100
                    && middle < only.Bottom - only.Height * 15 / 100);
            return weak
                ? new PageCut(PageCutKind.Slices, width, height, Split(only, screenRows, layout))
                : new PageCut(PageCutKind.Whole, width, height, [only]);
        }

        var pad = Math.Max(1, height / 200);
        var screens = new List<RowRange>();
        for (var i = 0; i < bands.Count;)
        {
            var group = bands[i];
            var next = i + 1;
            while (next < bands.Count && bands[next].Bottom - group.Top <= layout.MergeBands * screenRows)
                group = group with { Bottom = bands[next++].Bottom };
            group = new RowRange(Math.Max(0, group.Top - pad), Math.Min(height, group.Bottom + pad));
            if (group.Height > layout.TallBand * screenRows)
                screens.AddRange(Split(group, screenRows, layout));
            else
                screens.Add(group);
            i = next;
        }
        return new PageCut(PageCutKind.Bands, width, height, screens);
    }

    private static List<RowRange> Split(RowRange range, double screenRows, ComicLayout layout)
    {
        var count = Math.Max(2, (int)Math.Ceiling(range.Height / (layout.TallBand * screenRows)));
        var overlap = (int)Math.Round(layout.Overlap * Math.Min(screenRows, (double)range.Height / count));
        var piece = (range.Height + (count - 1) * overlap + count - 1) / count;
        var pieces = new List<RowRange>(count);
        for (var i = 0; i < count; i++)
        {
            var top = range.Top + i * (piece - overlap);
            var bottom = i == count - 1 ? range.Bottom : Math.Min(range.Bottom, top + piece);
            pieces.Add(new RowRange(Math.Min(top, bottom - 1), bottom));
        }
        return pieces;
    }

    private static List<RowRange> AbsorbSlivers(List<RowRange> bands, int minimum)
    {
        var result = new List<RowRange>(bands);
        while (result.Count > 1 && result.FindIndex(b => b.Height < minimum) is var i and >= 0)
        {
            var mergeUp = i > 0 && (i == result.Count - 1 || result[i - 1].Height <= result[i + 1].Height);
            if (mergeUp)
            {
                result[i - 1] = result[i - 1] with { Bottom = result[i].Bottom };
                result.RemoveAt(i);
            }
            else
            {
                result[i + 1] = result[i + 1] with { Top = result[i].Top };
                result.RemoveAt(i);
            }
        }
        return result;
    }

    private static IEnumerable<RowRange> Runs(double[] share, int from, int to, double threshold, int minimum)
    {
        var start = -1;
        for (var y = from; y <= to; y++)
        {
            var inside = y < to && share[y] >= threshold;
            if (inside && start < 0)
                start = y;
            else if (!inside && start >= 0)
            {
                if (y - start >= minimum)
                    yield return new RowRange(start, y);
                start = -1;
            }
        }
    }

    private static double[] BackgroundShare(Raster page)
    {
        var share = new double[page.Height];
        for (var y = 0; y < page.Height; y++)
        {
            int white = 0, black = 0;
            for (var x = 0; x < page.Width; x++)
            {
                var luma = page.Luma(x, y);
                if (luma >= WhiteLevel)
                    white++;
                else if (luma <= BlackLevel)
                    black++;
            }
            share[y] = (double)Math.Max(white, black) / page.Width;
        }
        return share;
    }
}
