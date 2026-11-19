namespace NovelShelf.Core.Comics;

public enum ComicListing
{
    Popular,
    Latest,
    Search,
}

public sealed record ComicSourceInfo(string Id, string Name, string Language, bool IsNsfw, bool SupportsLatest);

public sealed record ComicSummary(string SourceId, string Key, string Title, string? Author, Uri? ThumbnailUrl);

public sealed record ComicBrowsePage(IReadOnlyList<ComicSummary> Items, bool HasNextPage);

public sealed record ComicDetails(
    string SourceId,
    string SourceName,
    string Key,
    string Title,
    string? Author,
    string? Description,
    string? Status,
    IReadOnlyList<string> Genres,
    Uri? ThumbnailUrl,
    Uri? WebUrl,
    int? ChapterCount);

public sealed record ComicChapterInfo(string Key, string Name, double Number, int Order, long RemoteId);

public interface IComicProvider
{
    Task<IReadOnlyList<ComicSourceInfo>> GetSourcesAsync(CancellationToken ct = default);

    Task<ComicBrowsePage> BrowseAsync(string sourceId, ComicListing listing, string? query, int page, CancellationToken ct = default);

    Task<ComicDetails> GetComicAsync(string sourceId, string key, CancellationToken ct = default);

    Task<IReadOnlyList<ComicChapterInfo>> GetChaptersAsync(string sourceId, string key, CancellationToken ct = default);

    Task<IReadOnlyList<Uri>> GetPageUrlsAsync(ComicChapterInfo chapter, CancellationToken ct = default);

    Task<byte[]> GetBytesAsync(Uri url, CancellationToken ct = default);
}
