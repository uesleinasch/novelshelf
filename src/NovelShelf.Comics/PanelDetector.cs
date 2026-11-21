namespace NovelShelf.Comics;

public readonly record struct PanelBox(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
    public long Area => (long)Width * Height;
}

public sealed record PanelFrame(double Left, double Top, double Right, double Bottom);

public sealed record PanelLayout(int Width, int Height, IReadOnlyList<PanelBox> Panels, IReadOnlyList<RowRange> Rows)
{
    public double Coverage => Width * Height == 0 ? 0 : Panels.Sum(p => (double)p.Area) / ((double)Width * Height);
}

internal static class PanelDetector
{
    private const int WorkWidth = 480;
    private const int WhiteLevel = 215;
    private const int BlackLevel = 40;
    private const double MinSide = 0.06;
    private const double MinArea = 0.012;
    private const double Contained = 0.9;
    private const double OverlapTolerance = 0.015;

    public static PanelLayout Detect(Raster page, bool rightToLeft)
    {
        var scale = Math.Min(1.0, (double)WorkWidth / page.Width);
        var width = Math.Max(1, (int)Math.Round(page.Width * scale));
        var height = Math.Max(1, (int)Math.Round(page.Height * scale));
        var small = page.Resize(width, height);

        var ink = Ink(small);
        Dilate(ink, width, height, Math.Max(1, width / 240));
        var gutter = FloodFromBorder(ink, width, height);
        var boxes = Components(gutter, width, height)
            .Where(b => b.Width >= MinSide * width && b.Height >= MinSide * height && b.Area >= MinArea * width * height)
            .ToList();
        boxes = DropContained(boxes);

        var ordered = Order(boxes, rightToLeft, Math.Max(1, (int)(OverlapTolerance * height)), Math.Max(1, (int)(OverlapTolerance * width)));
        var panels = ordered.Select(b => Scale(b, scale, page.Width, page.Height)).ToList();
        var rows = Groups(panels, b => (b.Top, b.Bottom), Math.Max(1, (int)(OverlapTolerance * page.Height)))
            .Select(g => new RowRange(g.Min(b => b.Top), g.Max(b => b.Bottom)))
            .ToList();
        return new PanelLayout(page.Width, page.Height, panels, rows);
    }

    private static bool[] Ink(Raster page)
    {
        int width = page.Width, height = page.Height;
        int white = 0, black = 0;
        for (var x = 0; x < width; x++)
        {
            Count(page.Luma(x, 0));
            Count(page.Luma(x, height - 1));
        }
        for (var y = 0; y < height; y++)
        {
            Count(page.Luma(0, y));
            Count(page.Luma(width - 1, y));
        }
        var darkBackground = black > white;

        var ink = new bool[width * height];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var luma = page.Luma(x, y);
                ink[y * width + x] = darkBackground ? luma > BlackLevel : luma < WhiteLevel;
            }
        }
        return ink;

        void Count(byte luma)
        {
            if (luma >= WhiteLevel)
                white++;
            else if (luma <= BlackLevel)
                black++;
        }
    }

    private static void Dilate(bool[] mask, int width, int height, int radius)
    {
        var temp = new bool[mask.Length];
        for (var y = 0; y < height; y++)
        {
            var row = y * width;
            var count = 0;
            for (var x = 0; x < Math.Min(radius, width); x++)
                count += mask[row + x] ? 1 : 0;
            for (var x = 0; x < width; x++)
            {
                if (x + radius < width && mask[row + x + radius])
                    count++;
                if (x - radius - 1 >= 0 && mask[row + x - radius - 1])
                    count--;
                temp[row + x] = count > 0;
            }
        }
        for (var x = 0; x < width; x++)
        {
            var count = 0;
            for (var y = 0; y < Math.Min(radius, height); y++)
                count += temp[y * width + x] ? 1 : 0;
            for (var y = 0; y < height; y++)
            {
                if (y + radius < height && temp[(y + radius) * width + x])
                    count++;
                if (y - radius - 1 >= 0 && temp[(y - radius - 1) * width + x])
                    count--;
                mask[y * width + x] = count > 0;
            }
        }
    }

    private static bool[] FloodFromBorder(bool[] ink, int width, int height)
    {
        var gutter = new bool[ink.Length];
        var stack = new Stack<int>();
        for (var x = 0; x < width; x++)
        {
            Seed(x);
            Seed((height - 1) * width + x);
        }
        for (var y = 0; y < height; y++)
        {
            Seed(y * width);
            Seed(y * width + width - 1);
        }
        while (stack.TryPop(out var i))
        {
            int x = i % width, y = i / width;
            if (x > 0) Seed(i - 1);
            if (x < width - 1) Seed(i + 1);
            if (y > 0) Seed(i - width);
            if (y < height - 1) Seed(i + width);
        }
        return gutter;

        void Seed(int i)
        {
            if (ink[i] || gutter[i])
                return;
            gutter[i] = true;
            stack.Push(i);
        }
    }

    private static List<PanelBox> Components(bool[] gutter, int width, int height)
    {
        var seen = (bool[])gutter.Clone();
        var boxes = new List<PanelBox>();
        var stack = new Stack<int>();
        for (var start = 0; start < seen.Length; start++)
        {
            if (seen[start])
                continue;
            int left = width, top = height, right = 0, bottom = 0;
            seen[start] = true;
            stack.Push(start);
            while (stack.TryPop(out var i))
            {
                int x = i % width, y = i / width;
                left = Math.Min(left, x);
                right = Math.Max(right, x + 1);
                top = Math.Min(top, y);
                bottom = Math.Max(bottom, y + 1);
                if (x > 0) Visit(i - 1);
                if (x < width - 1) Visit(i + 1);
                if (y > 0) Visit(i - width);
                if (y < height - 1) Visit(i + width);
            }
            boxes.Add(new PanelBox(left, top, right, bottom));
        }
        return boxes;

        void Visit(int i)
        {
            if (seen[i])
                return;
            seen[i] = true;
            stack.Push(i);
        }
    }

    private static List<PanelBox> DropContained(List<PanelBox> boxes) =>
        boxes.Where(inner => !boxes.Any(outer => outer != inner && outer.Area >= inner.Area
                && Intersection(inner, outer) >= Contained * inner.Area))
            .ToList();

    private static long Intersection(PanelBox a, PanelBox b)
    {
        var width = Math.Min(a.Right, b.Right) - Math.Max(a.Left, b.Left);
        var height = Math.Min(a.Bottom, b.Bottom) - Math.Max(a.Top, b.Top);
        return width > 0 && height > 0 ? (long)width * height : 0;
    }

    private static List<PanelBox> Order(List<PanelBox> boxes, bool rightToLeft, int rowTolerance, int columnTolerance)
    {
        if (boxes.Count <= 1)
            return boxes;
        var rows = Groups(boxes, b => (b.Top, b.Bottom), rowTolerance);
        if (rows.Count > 1)
            return rows.SelectMany(r => Order(r, rightToLeft, rowTolerance, columnTolerance)).ToList();
        var columns = Groups(boxes, b => (b.Left, b.Right), columnTolerance);
        if (columns.Count > 1)
        {
            if (rightToLeft)
                columns.Reverse();
            return columns.SelectMany(c => Order(c, rightToLeft, rowTolerance, columnTolerance)).ToList();
        }
        return boxes.OrderBy(b => b.Top).ThenBy(b => rightToLeft ? -b.Right : b.Left).ToList();
    }

    private static List<List<PanelBox>> Groups(IEnumerable<PanelBox> boxes, Func<PanelBox, (int Start, int End)> span, int tolerance)
    {
        var groups = new List<List<PanelBox>>();
        var end = int.MinValue;
        foreach (var box in boxes.OrderBy(b => span(b).Start))
        {
            var (start, stop) = span(box);
            if (groups.Count == 0 || start >= end - tolerance)
                groups.Add([]);
            groups[^1].Add(box);
            end = Math.Max(groups[^1].Count == 1 ? stop : end, stop);
        }
        return groups;
    }

    private static PanelBox Scale(PanelBox box, double scale, int width, int height) => new(
        Math.Clamp((int)Math.Floor(box.Left / scale), 0, width),
        Math.Clamp((int)Math.Floor(box.Top / scale), 0, height),
        Math.Clamp((int)Math.Ceiling(box.Right / scale), 0, width),
        Math.Clamp((int)Math.Ceiling(box.Bottom / scale), 0, height));
}
