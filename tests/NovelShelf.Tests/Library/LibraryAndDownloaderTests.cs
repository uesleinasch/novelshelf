using NovelShelf.Core.Downloading;
using NovelShelf.Core.Library;
using NovelShelf.Core.Sources;

namespace NovelShelf.Tests.Library;

public sealed class LibraryAndDownloaderTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novelshelf-test-").FullName;
    private readonly LibraryStore _store;
    private readonly FakeSource _source = new(chapters: 5);
    private readonly ChapterDownloader _downloader;

    public LibraryAndDownloaderTests()
    {
        _store = new LibraryStore(Path.Combine(_dir, "library.db"));
        _downloader = new ChapterDownloader(_store, new SourceRegistry([_source]));
    }

    [Fact]
    public void Upsert_keeps_book_id_stable_across_updates()
    {
        var first = _store.Upsert(_source.Details());
        var second = _store.Upsert(_source.Details() with { Title = "Novo título", ChapterCount = 9 });

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.BookId, second.BookId);
        Assert.Equal("Novo título", second.Title);
        Assert.Equal(9, second.RemoteChapterCount);
        Assert.Single(_store.GetNovels());
    }

    [Fact]
    public async Task Download_continues_from_last_chapter_and_stops_at_end()
    {
        var ct = TestContext.Current.CancellationToken;
        var novel = _store.Upsert(_source.Details());

        var r1 = await _downloader.DownloadNextAsync(novel.Id, 2, ct: ct);
        var r2 = await _downloader.DownloadNextAsync(novel.Id, 10, ct: ct);

        Assert.Equal(new DownloadResult(2, ReachedEnd: false), r1);
        Assert.Equal(new DownloadResult(3, ReachedEnd: true), r2);
        Assert.Equal(["Cap 1", "Cap 2", "Cap 3", "Cap 4", "Cap 5"], _store.GetChapters(novel.Id).Select(c => c.Title));
        Assert.Equal(5, _store.GetNovel(novel.Id)!.DownloadedChapters);
    }

    [Fact]
    public async Task Download_after_end_picks_up_newly_released_chapters()
    {
        var ct = TestContext.Current.CancellationToken;
        var novel = _store.Upsert(_source.Details());
        await _downloader.DownloadNextAsync(novel.Id, 10, ct: ct);

        _source.Release(2);
        var result = await _downloader.DownloadNextAsync(novel.Id, 10, ct: ct);

        Assert.Equal(new DownloadResult(2, ReachedEnd: true), result);
        Assert.Equal("Cap 7", _store.GetLastChapter(novel.Id)!.Title);
    }

    [Fact]
    public void MarkSent_records_chapter_count()
    {
        var novel = _store.Upsert(_source.Details());

        _store.MarkSent(novel.Id, 42);

        var stored = _store.GetNovel(novel.Id)!;
        Assert.Equal(42, stored.SentChapterCount);
        Assert.NotNull(stored.SentAt);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class FakeSource(int chapters) : INovelSource
    {
        private int _released = chapters;

        public string Id => "fake";
        public string Name => "Fake";
        public Uri BaseUrl { get; } = new("https://fake.test/");
        public bool CanHandle(Uri url) => url.Host == "fake.test";
        public void Release(int more) => _released += more;

        public NovelDetails Details() => new("fake", "livro", new Uri(BaseUrl, "livro"), "Livro", "Autor", null, null,
            [], null, _released, ChapterUrl(1));

        public Task<IReadOnlyList<NovelSummary>> SearchAsync(string query, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<NovelSummary>>([]);

        public Task<NovelDetails> GetNovelAsync(Uri novelUrl, CancellationToken ct = default) => Task.FromResult(Details());

        public Task<ChapterPage> GetChapterAsync(Uri chapterUrl, CancellationToken ct = default)
        {
            var n = int.Parse(chapterUrl.Segments[^1]);
            var next = n < _released ? ChapterUrl(n + 1) : null;
            return Task.FromResult(new ChapterPage(chapterUrl, $"Cap {n}", $"<p>texto {n}</p>", next));
        }

        private Uri ChapterUrl(int n) => new(BaseUrl, $"livro/cap/{n}");
    }
}
