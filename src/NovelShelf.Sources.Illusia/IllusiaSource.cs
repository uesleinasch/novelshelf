using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using NovelShelf.Core.Html;
using NovelShelf.Core.Net;
using NovelShelf.Core.Sources;

namespace NovelShelf.Sources.Illusia;

public sealed class IllusiaSource(PoliteHttpClient http) : INovelSource
{
    public const string SourceId = "illusia";

    private static readonly HtmlParser Parser = new();

    public string Id => SourceId;
    public string Name => "Illusia";
    public Uri BaseUrl { get; } = new("https://illusia.com.br/");

    public bool CanHandle(Uri url) =>
        url.IsAbsoluteUri && url.Host.EndsWith("illusia.com.br", StringComparison.OrdinalIgnoreCase);

    public async Task<IReadOnlyList<NovelSummary>> SearchAsync(string query, CancellationToken ct = default)
    {
        var url = new Uri(BaseUrl, $"?s={Uri.EscapeDataString(query.Trim())}&post_type=fcn_story");
        var html = await http.GetStringAsync(url, ct);
        return ParseSearch(html);
    }

    public async Task<NovelDetails> GetNovelAsync(Uri novelUrl, CancellationToken ct = default)
    {
        var slug = SlugFrom(novelUrl);
        var canonical = new Uri(BaseUrl, $"story/{slug}/");
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

        foreach (var card in doc.QuerySelectorAll("li.illusia-card--story"))
        {
            if (card.QuerySelector("a.illusia-card__title-link") is not { } link
                || link.GetAttribute("href") is not { } href
                || !Uri.TryCreate(BaseUrl, href, out var url)
                || !IsStoryUrl(url)
                || !seen.Add(url.AbsolutePath))
                continue;

            var title = link.TextContent.Trim();
            if (title.Length == 0)
                continue;

            var author = card.QuerySelector(".illusia-card__author a")?.TextContent.Trim();
            var cover = card.QuerySelector("img.illusia-card__cover-img")?.GetAttribute("src");
            results.Add(new NovelSummary(
                SourceId,
                url,
                title,
                string.IsNullOrEmpty(author) ? null : author,
                cover is null ? null : new Uri(BaseUrl, cover)));
        }

        return results;
    }

    public NovelDetails ParseNovel(string html, string slug, Uri url)
    {
        using var doc = Parser.ParseDocument(html);
        var book = FindJsonLd(doc, "Book");

        var title = Text(doc.QuerySelector("h1.illusia-single-story__title"))
            ?? (book is { } b1 ? GetString(b1, "name") : null)
            ?? throw new FormatException($"Página da história '{slug}' sem título.");

        var author = Text(doc.QuerySelector(".illusia-single-story__credits-primary a.author"))
            ?? (book is { } b2 && b2.TryGetProperty("author", out var a) ? GetString(a, "name") : null);

        var description = doc.QuerySelector(".illusia-single-story__description");
        var synopsis = description is null
            ? null
            : string.Join("\n\n", description.QuerySelectorAll("p").Select(p => p.TextContent.Trim()).Where(p => p.Length > 0));
        if (string.IsNullOrEmpty(synopsis))
            synopsis = Text(description);

        var image = (book is { } b3 ? GetString(b3, "image") : null)
            ?? doc.QuerySelector("meta[property='og:image']")?.GetAttribute("content");

        var status = doc.QuerySelectorAll(".illusia-single-story__badge")
            .FirstOrDefault(e => !e.ClassList.Any(c => c.StartsWith("_rating", StringComparison.Ordinal)))
            ?.TextContent.Trim();

        var genres = doc.QuerySelectorAll(".illusia-single-story__taxonomies a._taxonomy-genre")
            .Select(e => e.TextContent.Trim())
            .Where(g => g.Length > 0)
            .Distinct()
            .ToList();

        var chapters = doc.QuerySelectorAll("a.chapter-group__list-item-link")
            .Select(e => e.GetAttribute("href"))
            .OfType<string>()
            .ToList();
        var first = chapters.Count > 0 ? new Uri(BaseUrl, chapters[0]) : null;

        return new NovelDetails(SourceId, slug, url, title, author, synopsis, image is null ? null : new Uri(BaseUrl, image),
            genres, string.IsNullOrEmpty(status) ? null : status, chapters.Count, first);
    }

    public ChapterPage ParseChapter(string html, Uri url)
    {
        using var doc = Parser.ParseDocument(html);
        var body = doc.QuerySelector("#chapter-content .chapter-formatting") ?? doc.QuerySelector("#chapter-content");
        if (body is null)
        {
            var reason = doc.QuerySelector(".post-password-form") is null ? "Capítulo sem conteúdo" : "Capítulo protegido por senha";
            throw new FormatException($"{reason}: {url}");
        }

        MoveFootnotesInline(body);

        var nextHref = doc.QuerySelector("a._navigation._next")?.GetAttribute("href");
        var next = nextHref is null ? null : WithoutFragment(new Uri(BaseUrl, nextHref));

        return new ChapterPage(url, ChapterTitle(doc), ChapterHtmlSanitizer.ToXhtml(body), next);
    }

    private static void MoveFootnotesInline(IElement body)
    {
        foreach (var back in body.QuerySelectorAll(".illusia-fn-backlinks").ToList())
            back.Remove();

        foreach (var aside in body.QuerySelectorAll("aside.illusia-fn-container").ToList())
        {
            var list = aside.QuerySelector("ol");
            if (list is null)
            {
                aside.Remove();
                continue;
            }
            var hr = body.Owner!.CreateElement("hr");
            aside.Parent!.InsertBefore(hr, aside);
            aside.Parent.ReplaceChild(list, aside);
        }
    }

    private static string ChapterTitle(IDocument doc)
    {
        var title = doc.Title?.Trim();
        if (!string.IsNullOrEmpty(title))
        {
            var cut = title.LastIndexOf(" – Illusia", StringComparison.Ordinal);
            if (cut > 0)
                return title[..cut].Trim();
        }
        return Text(doc.QuerySelector("h1.chapter__title")) ?? "Capítulo";
    }

    private static Uri WithoutFragment(Uri url) =>
        string.IsNullOrEmpty(url.Fragment) ? url : new Uri(url.GetLeftPart(UriPartial.Query));

    private static string? Text(IElement? element)
    {
        var text = element?.TextContent.Trim();
        return string.IsNullOrEmpty(text) ? null : text;
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

    private static bool IsStoryUrl(Uri url)
    {
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        return segments.Length == 2 && segments[0] == "story";
    }

    public static string SlugFrom(Uri url)
    {
        var segments = url.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (url.IsAbsoluteUri && segments.Length >= 2 && segments[0] == "story")
            return segments[1];
        throw new ArgumentException($"URL não é de uma história do Illusia: {url}", nameof(url));
    }
}
