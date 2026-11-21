using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovelShelf.Comics;

public enum ComicReadingMode
{
    Auto,
    Pages,
    Strip,
    Landscape,
    Panels,
}

public enum MarginMode
{
    Auto,
    Off,
    Manual,
}

public enum WidePageMode
{
    Split,
    Keep,
    Rotate,
}

public enum LandscapeTurn
{
    Clockwise,
    CounterClockwise,
}

public sealed record ComicLayout
{
    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() } };

    public static ComicLayout Default { get; } = new();

    public ComicReadingMode Mode { get; init; } = ComicReadingMode.Auto;
    public MarginMode Margins { get; init; } = MarginMode.Auto;
    public double MarginLeft { get; init; }
    public double MarginRight { get; init; }
    public double MarginTop { get; init; }
    public double MarginBottom { get; init; }
    public WidePageMode WidePages { get; init; } = WidePageMode.Split;
    public double Overlap { get; init; } = 0.05;
    public double CutSearch { get; init; } = 0.4;
    public double Gamma { get; init; } = 1.0;
    public bool Sharpen { get; init; } = true;
    public LandscapeTurn Turn { get; init; } = LandscapeTurn.Clockwise;
    public double TallBand { get; init; } = 1.3;
    public double MergeBands { get; init; } = 1.0;
    public bool PageBeforeBands { get; init; }
    public bool AvoidBubbles { get; init; }

    [JsonIgnore]
    public bool UsesPageCuts => Mode is ComicReadingMode.Landscape or ComicReadingMode.Panels;

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static ComicLayout FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return Default;
        try
        {
            return (JsonSerializer.Deserialize<ComicLayout>(json, Json) ?? Default).Normalized();
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    public ComicLayout Normalized() => this with
    {
        MarginLeft = Math.Clamp(MarginLeft, 0, 0.4),
        MarginRight = Math.Clamp(MarginRight, 0, 0.4),
        MarginTop = Math.Clamp(MarginTop, 0, 0.4),
        MarginBottom = Math.Clamp(MarginBottom, 0, 0.4),
        Overlap = Math.Clamp(Overlap, 0, 0.4),
        CutSearch = Math.Clamp(CutSearch, 0, 0.6),
        Gamma = Math.Clamp(Gamma, 0.4, 2.5),
        TallBand = Math.Clamp(TallBand, 1.0, 2.5),
        MergeBands = Math.Clamp(MergeBands, 0, 1.0),
    };
}
