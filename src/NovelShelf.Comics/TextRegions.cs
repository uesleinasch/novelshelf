namespace NovelShelf.Comics;

public enum TextRegionKind
{
    Bubble,
    TextInBubble,
    FreeText,
}

public readonly record struct TextRegion(PanelBox Box, TextRegionKind Kind, float Score);

public interface ITextRegionDetector
{
    string Id { get; }

    IReadOnlyList<TextRegion> Detect(byte[] pixels, int width, int height, int channels);
}

internal static class BubbleGuard
{
    private const double MaxStretch = 0.35;
    private const double NearPanel = 0.05;

    public static IReadOnlyList<PanelBox> Regions(Raster page, ITextRegionDetector detector) =>
        detector.Detect(page.Data, page.Width, page.Height, page.Channels)
            .Where(r => r.Kind != TextRegionKind.TextInBubble)
            .Select(r => r.Box)
            .ToList();

    public static PageCut AvoidCuttingBubbles(PageCut plan, IReadOnlyList<PanelBox> bubbles, double screenRows)
    {
        if (plan.Kind == PageCutKind.Manual || bubbles.Count == 0 || plan.Screens.Count < 2)
            return plan;
        var pad = Math.Max(1, plan.Height / 200);
        var limit = (int)(MaxStretch * screenRows);
        var screens = plan.Screens.Select(screen =>
        {
            var top = screen.Top;
            var bottom = screen.Bottom;
            foreach (var bubble in bubbles)
            {
                if (bubble.Top < top && bubble.Bottom > top && top - bubble.Top <= limit)
                    top = Math.Max(0, bubble.Top - pad);
                if (bubble.Top < bottom && bubble.Bottom > bottom && bubble.Bottom - bottom <= limit)
                    bottom = Math.Min(plan.Height, bubble.Bottom + pad);
            }
            return new RowRange(top, bottom);
        }).ToList();
        return plan with { Screens = screens };
    }

    public static PageCut IncludeBubbles(PageCut plan, IReadOnlyList<PanelBox> bubbles)
    {
        if (plan.Kind != PageCutKind.Panels || bubbles.Count == 0)
            return plan;
        var panels = plan.Panels.ToList();
        var near = NearPanel * plan.Height;
        foreach (var bubble in bubbles)
        {
            var touched = Enumerable.Range(0, panels.Count).Where(i => Overlaps(panels[i], bubble)).ToList();
            if (touched.Count == 0 && Nearest(panels, bubble) is var (index, distance) && distance <= near)
                touched.Add(index);
            foreach (var i in touched)
                panels[i] = Union(panels[i], bubble);
        }
        return plan with { Panels = panels };
    }

    private static bool Overlaps(PanelBox a, PanelBox b) =>
        a.Left < b.Right && b.Left < a.Right && a.Top < b.Bottom && b.Top < a.Bottom;

    private static PanelBox Union(PanelBox a, PanelBox b) =>
        new(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top), Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom));

    private static (int Index, double Distance) Nearest(List<PanelBox> panels, PanelBox bubble) => panels
        .Select((panel, i) => (i, Distance(panel, bubble)))
        .MinBy(p => p.Item2);

    private static double Distance(PanelBox a, PanelBox b)
    {
        var dx = Math.Max(0, Math.Max(a.Left - b.Right, b.Left - a.Right));
        var dy = Math.Max(0, Math.Max(a.Top - b.Bottom, b.Top - a.Bottom));
        return Math.Sqrt((double)dx * dx + (double)dy * dy);
    }
}
