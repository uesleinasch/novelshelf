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
    DateTimeOffset? SentAt,
    WorkKind Kind = WorkKind.Novel,
    string? RemoteKey = null,
    string? SourceName = null,
    bool RightToLeft = false,
    bool NeedsResend = false);

public enum WorkKind
{
    Novel,
    Comic,
}

public sealed record StoredComicChapter(
    long NovelId,
    string Key,
    string Title,
    double Number,
    int Order,
    int PageCount,
    string Folder,
    DateTimeOffset DownloadedAt);

public sealed record StoredChapter(
    long NovelId,
    int Index,
    Uri Url,
    string Title,
    string Html,
    Uri? NextUrl,
    DateTimeOffset DownloadedAt);

public sealed record QueuedDownload(long NovelId, int Requested, int Done, bool Paused);
