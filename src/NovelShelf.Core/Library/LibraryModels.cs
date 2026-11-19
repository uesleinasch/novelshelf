namespace NovelShelf.Core.Library;

public sealed record LibraryNovel(
    long Id,
    string SourceId,
    string Slug,
    Uri Url,
    string Title,
    string? Author,
    string? Synopsis,
    Uri? CoverUrl,
    string? Status,
    IReadOnlyList<string> Genres,
    int? RemoteChapterCount,
    Uri? FirstChapterUrl,
    string BookId,
    int DownloadedChapters,
    int SentChapterCount,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SentAt);

public sealed record StoredChapter(
    long NovelId,
    int Index,
    Uri Url,
    string Title,
    string Html,
    Uri? NextUrl,
    DateTimeOffset DownloadedAt);
