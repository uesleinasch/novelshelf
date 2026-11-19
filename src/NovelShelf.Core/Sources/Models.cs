namespace NovelShelf.Core.Sources;

public sealed record NovelSummary(
    string SourceId,
    Uri Url,
    string Title,
    string? Author,
    Uri? CoverUrl);

public sealed record NovelDetails(
    string SourceId,
    string Slug,
    Uri Url,
    string Title,
    string? Author,
    string? Synopsis,
    Uri? CoverUrl,
    IReadOnlyList<string> Genres,
    string? Status,
    int? ChapterCount,
    Uri? FirstChapterUrl);

public sealed record ChapterPage(
    Uri Url,
    string Title,
    string Html,
    Uri? NextUrl);
