namespace NovelShelf.Comics;

public sealed record KindleProfile(string Id, string Name, int Width, int Height, bool Color = false)
{
    public static IReadOnlyList<KindleProfile> All { get; } =
    [
        new("pw5", "Kindle Paperwhite 11ª/12ª geração (6,8\", 1236×1648)", 1236, 1648),
        new("basic", "Kindle 11ª geração (6\", 1072×1448)", 1072, 1448),
    ];

    public static KindleProfile Default => All[0];

    public static KindleProfile Get(string? id) => All.FirstOrDefault(p => p.Id == id) ?? Default;

    public override string ToString() => Name;
}
