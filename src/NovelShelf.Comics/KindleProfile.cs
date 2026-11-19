namespace NovelShelf.Comics;

public sealed record KindleProfile(string Id, string Name, int Width, int Height, bool Color = false)
{
    public static IReadOnlyList<KindleProfile> All { get; } =
    [
        new("pw5", "Kindle Paperwhite (11ª e 12ª geração)", 1236, 1648),
        new("pw3", "Kindle Paperwhite (7ª a 10ª geração)", 1072, 1448),
        new("basic", "Kindle (2022 em diante)", 1072, 1448),
        new("oasis", "Kindle Oasis", 1264, 1680),
        new("scribe", "Kindle Scribe", 1860, 2480),
        new("colorsoft", "Kindle Colorsoft", 1264, 1680, Color: true),
    ];

    public static KindleProfile Default => All[0];

    public static KindleProfile Get(string? id) => All.FirstOrDefault(p => p.Id == id) ?? Default;

    public override string ToString() => Name;
}
