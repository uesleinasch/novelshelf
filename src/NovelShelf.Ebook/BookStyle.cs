using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovelShelf.Ebook;

[JsonConverter(typeof(JsonStringEnumConverter<ChapterHeading>))]
public enum ChapterHeading
{
    AsIs,
    NumberOnly,
    Hidden,
}

public sealed record BookStyle
{
    private static readonly JsonSerializerOptions Json = new();

    public static BookStyle Default { get; } = new();

    public double Indent { get; init; } = 1.2;
    public double ParagraphSpacing { get; init; } = 0.6;
    public bool Justify { get; init; } = true;
    public ChapterHeading Heading { get; init; } = ChapterHeading.AsIs;

    public string ToCss() => string.Create(CultureInfo.InvariantCulture, $$"""
        h1 { font-size: 1.4em; text-align: center; margin: 1.5em 0 1em; }
        p { text-indent: {{Indent:0.##}}em; margin: 0 0 {{ParagraphSpacing:0.##}}em; text-align: {{(Justify ? "justify" : "left")}}; }
        """);

    public string ChapterTitle(int number, string title) =>
        Heading == ChapterHeading.NumberOnly ? $"Capítulo {number}" : title;

    public bool ShowsHeading => Heading != ChapterHeading.Hidden;

    public string ToJson() => JsonSerializer.Serialize(this, Json);

    public static BookStyle? FromJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            return JsonSerializer.Deserialize<BookStyle>(json, Json)?.Normalized();
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public BookStyle Normalized() => this with
    {
        Indent = Math.Round(Math.Clamp(Indent, 0, 3), 1),
        ParagraphSpacing = Math.Round(Math.Clamp(ParagraphSpacing, 0, 2), 1),
        Heading = Enum.IsDefined(Heading) ? Heading : ChapterHeading.AsIs,
    };
}
