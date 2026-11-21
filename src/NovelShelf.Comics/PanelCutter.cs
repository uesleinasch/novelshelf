namespace NovelShelf.Comics;

internal sealed record ManualGrid(IReadOnlyList<int> Rows, IReadOnlyList<(int X, int Y)> Columns);

internal static class PanelCutter
{
    private const int WhiteLevel = 215;
    private const double MinCoverage = 0.5;
    private const double TinyPanel = 0.03;
    private const double HugePanel = 0.6;
    private const double MinCell = 0.005;

    public static PageCut Plan(Raster page, bool rightToLeft, ManualGrid? manual)
    {
        int width = page.Width, height = page.Height;
        if (manual is not null)
            return Manual(page, rightToLeft, manual);

        var detected = PanelDetector.Detect(page, rightToLeft);
        var whole = new PanelBox(0, 0, width, height);
        if (detected.Panels.Count == 0 || detected.Coverage < MinCoverage)
            return new PageCut(PageCutKind.Whole, width, height, [new RowRange(0, height)])
            {
                Panels = [whole],
                Suspicion = PageSuspicion.NoClearGutter,
            };
        if (detected.Panels.Count == 1)
            return new PageCut(PageCutKind.Whole, width, height, [new RowRange(0, height)]) { Panels = [whole] };

        var area = (double)width * height;
        var suspicion = PageSuspicion.None;
        if (detected.Panels.Any(p => p.Area < TinyPanel * area))
            suspicion |= PageSuspicion.TinyPiece;
        if (detected.Panels.Any(p => p.Area > HugePanel * area))
            suspicion |= PageSuspicion.HugePiece;
        return new PageCut(PageCutKind.Panels, width, height, detected.Rows) { Panels = detected.Panels, Suspicion = suspicion };
    }

    private static PageCut Manual(Raster page, bool rightToLeft, ManualGrid grid)
    {
        int width = page.Width, height = page.Height;
        var edges = grid.Rows.Where(r => r > 0 && r < height).Order().Distinct().Prepend(0).Append(height).ToList();
        var bands = edges.Zip(edges.Skip(1)).Select(e => new RowRange(e.First, e.Second)).ToList();
        var panels = new List<PanelBox>();
        foreach (var band in bands)
        {
            var xs = grid.Columns.Where(c => c.Y >= band.Top && c.Y < band.Bottom)
                .Select(c => c.X).Where(x => x > 0 && x < width)
                .Order().Distinct().Prepend(0).Append(width).ToList();
            var cells = xs.Zip(xs.Skip(1)).Select(x => new PanelBox(x.First, band.Top, x.Second, band.Bottom)).ToList();
            if (rightToLeft)
                cells.Reverse();
            panels.AddRange(cells.Select(c => Trim(page, c)).OfType<PanelBox>());
        }
        return new PageCut(PageCutKind.Manual, width, height, bands) { Panels = panels };
    }

    private static PanelBox? Trim(Raster page, PanelBox cell)
    {
        int left = cell.Right, top = cell.Bottom, right = cell.Left, bottom = cell.Top;
        for (var y = cell.Top; y < cell.Bottom; y++)
        {
            for (var x = cell.Left; x < cell.Right; x++)
            {
                if (page.Luma(x, y) >= WhiteLevel)
                    continue;
                left = Math.Min(left, x);
                right = Math.Max(right, x + 1);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y + 1);
            }
        }
        if (right <= left || bottom <= top || (long)(right - left) * (bottom - top) < MinCell * page.Width * page.Height)
            return null;
        var pad = Math.Max(1, Math.Min(page.Width, page.Height) / 150);
        return new PanelBox(
            Math.Max(cell.Left, left - pad), Math.Max(cell.Top, top - pad),
            Math.Min(cell.Right, right + pad), Math.Min(cell.Bottom, bottom + pad));
    }
}
