namespace NovelShelf.Core.Sources;

public interface INovelSource
{
    string Id { get; }
    string Name { get; }
    Uri BaseUrl { get; }
    bool CanHandle(Uri url);
    Task<IReadOnlyList<NovelSummary>> SearchAsync(string query, CancellationToken ct = default);
    Task<NovelDetails> GetNovelAsync(Uri novelUrl, CancellationToken ct = default);
    Task<ChapterPage> GetChapterAsync(Uri chapterUrl, CancellationToken ct = default);
}
