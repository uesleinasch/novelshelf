using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovelShelf.Comics;

public sealed record VerticalCut(double X, double Y);

public sealed record PageCuts
{
    private const double Edge = 0.005;
    private const double MinPanelSide = 0.02;
    private const int OrderScale = 10_000;

    public IReadOnlyList<double> Rows { get; init; } = [];
    public IReadOnlyList<VerticalCut> Columns { get; init; } = [];
    public IReadOnlyList<PanelFrame> Panels { get; init; } = [];

    public bool IsEmpty => Rows.Count == 0 && Columns.Count == 0 && Panels.Count == 0;

    public PageCuts Normalized() => this with
    {
        Rows = Rows.Where(r => r > Edge && r < 1 - Edge).Order().Distinct().ToList(),
        Columns = Columns.Where(c => c.X > Edge && c.X < 1 - Edge && c.Y is >= 0 and <= 1).ToList(),
        Panels = Panels.Select(Clamp).Where(p => p.Right - p.Left >= MinPanelSide && p.Bottom - p.Top >= MinPanelSide).ToList(),
    };

    public IReadOnlyList<int> PanelOrder(bool rightToLeft)
    {
        var boxes = Panels.Select(p => new PanelBox(
            (int)Math.Round(p.Left * OrderScale), (int)Math.Round(p.Top * OrderScale),
            (int)Math.Round(p.Right * OrderScale), (int)Math.Round(p.Bottom * OrderScale))).ToList();
        var used = new bool[boxes.Count];
        return PanelDetector.Arrange(boxes, rightToLeft, OrderScale, OrderScale).Panels
            .Select(box =>
            {
                var index = Enumerable.Range(0, boxes.Count).First(i => !used[i] && boxes[i] == box);
                used[index] = true;
                return index;
            })
            .ToList();
    }

    public string ToJson() => JsonSerializer.Serialize(new Stored(
        Rows.ToArray(),
        Columns.Select(c => new[] { c.X, c.Y }).ToArray(),
        Panels.Count == 0 ? null : Panels.Select(p => new[] { p.Left, p.Top, p.Right, p.Bottom }).ToArray()));

    public static PageCuts? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            var stored = JsonSerializer.Deserialize<Stored>(json);
            if (stored is null)
                return null;
            return new PageCuts
            {
                Rows = stored.Rows ?? [],
                Columns = (stored.Columns ?? []).Where(c => c.Length == 2).Select(c => new VerticalCut(c[0], c[1])).ToList(),
                Panels = (stored.Panels ?? []).Where(p => p.Length == 4).Select(p => new PanelFrame(p[0], p[1], p[2], p[3])).ToList(),
            }.Normalized();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public IReadOnlyList<(double Top, double Bottom)> Bands()
    {
        var edges = Rows.Prepend(0).Append(1).ToList();
        return edges.Zip(edges.Skip(1)).Select(e => (e.First, e.Second)).ToList();
    }

    private static PanelFrame Clamp(PanelFrame p) => new(
        Math.Clamp(Math.Min(p.Left, p.Right), 0, 1), Math.Clamp(Math.Min(p.Top, p.Bottom), 0, 1),
        Math.Clamp(Math.Max(p.Left, p.Right), 0, 1), Math.Clamp(Math.Max(p.Top, p.Bottom), 0, 1));

    private sealed record Stored(
        double[]? Rows,
        double[][]? Columns,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] double[][]? Panels = null);
}
