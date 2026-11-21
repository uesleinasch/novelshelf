using System.Text.Json;

namespace NovelShelf.Comics;

public sealed record VerticalCut(double X, double Y);

public sealed record PageCuts
{
    private const double Edge = 0.005;

    public IReadOnlyList<double> Rows { get; init; } = [];
    public IReadOnlyList<VerticalCut> Columns { get; init; } = [];

    public bool IsEmpty => Rows.Count == 0 && Columns.Count == 0;

    public PageCuts Normalized() => this with
    {
        Rows = Rows.Where(r => r > Edge && r < 1 - Edge).Order().Distinct().ToList(),
        Columns = Columns.Where(c => c.X > Edge && c.X < 1 - Edge && c.Y is >= 0 and <= 1).ToList(),
    };

    public string ToJson() => JsonSerializer.Serialize(new Stored(Rows.ToArray(), Columns.Select(c => new[] { c.X, c.Y }).ToArray()));

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

    private sealed record Stored(double[]? Rows, double[][]? Columns);
}
