using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using NovelShelf.Core.Html;
using NovelShelf.Core.Net;
using NovelShelf.Core.Sources;

namespace NovelShelf.Sources.NovelMania;

public sealed partial class NovelManiaSource(PoliteHttpClient http) : INovelSource
{
    public const string SourceId = "novelmania";

    private static readonly HtmlParser Parser = new();

    public string Id => SourceId;
    public string Name => "Novel Mania";
    public Uri BaseUrl { get; } = new("https://novelmania.com.br/");

    public bool CanHandle(Uri url) =>
        url.IsAbsoluteUri && url.Host.EndsWith("novelmania.com.br", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<NovelSummary>> SearchAsync(string query, CancellationToken ct = default)
    {
        var url = new Uri(BaseUrl, $"novels?q={Uri.EscapeDataString(query.Trim())}");
        var html = await http.GetStringAsync(url, ct);
        return ParseSearch(html);
    }

    public async Task<NovelDetails> GetNovelAsync(Uri novelUrl, CancellationToken ct = default)
    {
        var slug = SlugFrom(novelUrl);
        var canonical = new Uri(BaseUrl, $"novels/{slug}");
        var html = await http.GetStringAsync(canonical, ct);
        return ParseNovel(html, slug, canonical);
    }

    public async Task<ChapterPage> GetChapterAsync(Uri chapterUrl, CancellationToken ct = default)
    {
        var html = await http.GetStringAsync(chapterUrl, ct);
        return ParseChapter(html, chapterUrl);
    }

    public IReadOnlyList<NovelSummary> ParseSearch(string html)
    {
        using var doc = Parser.ParseDocument(html);
        var results = new List<NovelSummary>();
        var seen = new HashSet<string>();

        foreach (var anchor in doc.QuerySelectorAll("a[href^='/novels/']"))
        {
            var href = anchor.GetAttribute("href")!;
            if (!NovelPathRegex().IsMatch(href) || anchor.QuerySelector("img[alt]") is not { } img)
                continue;
            if (!seen.Add(href))
                continue;

            var title = img.GetAttribute("alt")!.Trim();
            if (title.Length == 0)
                continue;

            var author = anchor.NextElementSibling?.TextContent.Trim();
            results.Add(new NovelSummary(
                SourceId,
                new Uri(BaseUrl, href),
                title,
                string.IsNullOrEmpty(author) ? null : author,
                PickCover(img)));
        }

        return results;
    }

    public NovelDetails ParseNovel(string html, string slug, Uri url)
    {
        using var doc = Parser.ParseDocument(html);
        var book = FindJsonLd(doc, "Book")
            ?? throw new FormatException($"Página da novel '{slug}' sem JSON-LD do tipo Book.");

        var title = GetString(book, "name") ?? doc.Title ?? slug;
        var author = book.TryGetProperty("author", out var a) ? GetString(a, "name") : null;
        var synopsis = GetString(book, "description");
        var cover = GetString(book, "image") is { } image ? new Uri(image) : null;
        var status = GetString(book, "creativeWorkStatus");
        var genres = book.TryGetProperty("genre", out var g) && g.ValueKind == JsonValueKind.Array
            ? g.EnumerateArray().Select(x => x.GetString()).OfType<string>().ToList()
            : [];
        var first = book.TryGetProperty("potentialAction", out var action) && GetString(action, "target") is { } target
            ? new Uri(target)
            : null;

        return new NovelDetails(SourceId, slug, url, title, author, synopsis, cover, genres, status,
            ChapterCount: null, first);
    }

    public ChapterPage ParseChapter(string html, Uri url)
    {
        using var doc = Parser.ParseDocument(html);
        var body = doc.QuerySelector(".rich-text-content")
            ?? throw new FormatException($"Capítulo sem conteúdo: {url}");

        var next = doc.QuerySelector("link[rel='next']")?.GetAttribute("href") is { } href
            ? new Uri(BaseUrl, href)
            : null;

        return new ChapterPage(url, ChapterTitle(doc), ChapterHtmlSanitizer.ToXhtml(body), next);
    }

    private static string ChapterTitle(IDocument doc)
    {
        var name = FindJsonLd(doc, "Chapter") is { } chapter ? GetString(chapter, "name") : null;
        if (name is not null)
        {
            var parts = name.Split(" – ");
            return parts.Length > 1 ? string.Join(" – ", parts[..^1]) : name;
        }

        var h1 = doc.QuerySelector("h1");
        if (h1 is null)
            return "Capítulo";
        foreach (var hidden in h1.QuerySelectorAll(".sr-only").ToList())
            hidden.Remove();
        return h1.TextContent.Trim();
    }

    private static Uri? PickCover(IElement img)
    {
        var srcset = img.GetAttribute("srcset") ?? img.GetAttribute("srcSet");
        var jpg = srcset?
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Select(entry => entry.Split(' ')[0])
            .LastOrDefault(u => u.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) || u.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase));
        var src = jpg ?? img.GetAttribute("src");
        return src is null ? null : new Uri(src);
    }

    private static JsonElement? FindJsonLd(IDocument doc, string type)
    {
        foreach (var script in doc.QuerySelectorAll("script[type='application/ld+json']"))
        {
            try
            {
                using var json = JsonDocument.Parse(script.TextContent);
                if (json.RootElement.ValueKind == JsonValueKind.Object && GetString(json.RootElement, "@type") == type)
                    return json.RootElement.Clone();
            }
            catch (JsonException)
            {
            }
        }
        return null;
    }

    private static string? GetString(JsonElement element, string property) =>
        element.ValueKind == JsonValueKind.Object
        && element.TryGetProperty(property, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string SlugFrom(Uri url)
    {
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0] == "novels")
            return segments[1];
        throw new ArgumentException($"URL não é de uma novel do Novel Mania: {url}", nameof(url));
    }

    [GeneratedRegex(@"^/novels/[a-z0-9-]+$")]
    private static partial Regex NovelPathRegex();
}
