using System.Text;
using Microsoft.Data.Sqlite;
using NovelShelf.Comics;
using NovelShelf.Core.Comics;
using NovelShelf.Core.Library;
using SkiaSharp;

namespace NovelShelf.Tests.Library;

public sealed class ComicLibraryTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novelshelf-comic-").FullName;
    private readonly AppPaths _paths;
    private readonly LibraryStore _store;
    private readonly FakeComics _provider = new(chapters: 4, pagesPerChapter: 3);
    private readonly ComicDownloader _downloader;

    public ComicLibraryTests()
    {
        _paths = new AppPaths(_dir);
        _paths.EnsureCreated();
        _store = new LibraryStore(_paths.Database);
        _downloader = new ComicDownloader(_store, _provider, _paths);
    }

    [Fact]
    public void Opening_a_v0_1_database_keeps_novels_and_adds_comic_support()
    {
        var path = Path.Combine(_dir, "old.db");
        using (var db = new SqliteConnection($"Data Source={path};Pooling=False"))
        {
            db.Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = """
                CREATE TABLE novels (id INTEGER PRIMARY KEY, source_id TEXT NOT NULL, slug TEXT NOT NULL, url TEXT NOT NULL,
                    title TEXT NOT NULL, author TEXT, synopsis TEXT, cover_url TEXT, status TEXT, genres TEXT NOT NULL DEFAULT '',
                    remote_chapter_count INTEGER, first_chapter_url TEXT, book_id TEXT NOT NULL,
                    sent_chapter_count INTEGER NOT NULL DEFAULT 0, added_at TEXT NOT NULL, updated_at TEXT NOT NULL, sent_at TEXT,
                    UNIQUE (source_id, slug));
                CREATE TABLE chapters (novel_id INTEGER NOT NULL REFERENCES novels(id) ON DELETE CASCADE, idx INTEGER NOT NULL,
                    url TEXT NOT NULL, title TEXT NOT NULL, html TEXT NOT NULL, next_url TEXT, downloaded_at TEXT NOT NULL,
                    PRIMARY KEY (novel_id, idx));
                INSERT INTO novels (source_id, slug, url, title, book_id, added_at, updated_at)
                VALUES ('novelmania', 'antiga', 'https://novelmania.com.br/novels/antiga', 'Antiga', 'abc', '2026-11-18T00:00:00+00:00', '2026-11-18T00:00:00+00:00');
                """;
            cmd.ExecuteNonQuery();
        }

        var store = new LibraryStore(path);
        var novel = Assert.Single(store.GetNovels());

        Assert.Equal(WorkKind.Novel, novel.Kind);
        Assert.Equal("Antiga", novel.Title);
        Assert.Empty(store.GetComicChapters(novel.Id));
        _ = new LibraryStore(path);
    }

    [Fact]
    public void UpsertComic_keeps_identity_when_details_change()
    {
        var first = _store.UpsertComic(_provider.Details("Invencível"));
        var second = _store.UpsertComic(_provider.Details("Invencível (2003)"));

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.BookId, second.BookId);
        Assert.Equal(first.Slug, second.Slug);
        Assert.Equal(WorkKind.Comic, second.Kind);
        Assert.Equal("HQ Now!", second.SourceName);
        Assert.Equal("Invencível (2003)", second.Title);
        Assert.StartsWith("invencivel-", first.Slug);
        Assert.Equal($"hq-{first.Slug}", Core.Books.BookExporter.FileNameFor(first));
    }

    [Fact]
    public void Comic_layout_is_saved_per_work()
    {
        var comic = _store.UpsertComic(_provider.Details("Invencível"));
        Assert.Equal(ComicLayout.Default, _store.GetComicLayout(comic.Id));

        var layout = new ComicLayout { Mode = ComicReadingMode.Strip, Overlap = 0.1, Gamma = 1.3 };
        _store.SetComicLayout(comic.Id, layout);

        Assert.Equal(layout, new LibraryStore(_paths.Database).GetComicLayout(comic.Id));
        _store.SetComicLayout(comic.Id, ComicLayout.Default);
        Assert.Equal(ComicLayout.Default, _store.GetComicLayout(comic.Id));
    }

    [Fact]
    public async Task Download_fetches_next_chapters_in_reading_order_and_reports_the_end()
    {
        var comic = _store.UpsertComic(_provider.Details("Invencível"));

        var first = await _downloader.DownloadNextAsync(comic.Id, 3, ct: Ct);
        var second = await _downloader.DownloadNextAsync(comic.Id, 3, ct: Ct);

        Assert.Equal((3, false), (first.Downloaded, first.ReachedEnd));
        Assert.Equal((1, true), (second.Downloaded, second.ReachedEnd));
        var stored = _store.GetComicChapters(comic.Id);
        Assert.Equal(["#1", "#2", "#3", "#4"], stored.Select(c => c.Title));
        Assert.All(stored, c => Assert.Equal(3, _downloader.PageFiles(comic.Id, c).Count));
        Assert.EndsWith(".png", _downloader.PageFiles(comic.Id, stored[0])[0]);
        Assert.Equal(4, _store.GetNovel(comic.Id)!.DownloadedChapters);
    }

    [Fact]
    public async Task Download_after_end_picks_up_new_chapters()
    {
        var comic = _store.UpsertComic(_provider.Details("Invencível"));
        await _downloader.DownloadNextAsync(comic.Id, 10, ct: Ct);

        _provider.Release(2);
        var result = await _downloader.DownloadNextAsync(comic.Id, 10, ct: Ct);

        Assert.Equal((2, true), (result.Downloaded, result.ReachedEnd));
    }

    [Fact]
    public async Task Export_writes_a_fixed_layout_azw3_with_every_page()
    {
        var comic = _store.UpsertComic(_provider.Details("Invencível"));
        await _downloader.DownloadNextAsync(comic.Id, 2, ct: Ct);
        var exporter = new ComicExporter(_store, _paths, _downloader);

        var book = await exporter.ExportAsync(comic.Id, KindleProfile.Default, ct: Ct);

        Assert.Null(book.EpubPath);
        Assert.Equal(2, book.ChapterCount);
        Assert.Equal($"hq-{comic.Slug}.azw3", book.KindleFileName);
        var bytes = await File.ReadAllBytesAsync(book.Azw3Path, Ct);
        Assert.Equal("BOOKMOBI", Encoding.ASCII.GetString(bytes, 60, 8));
        Assert.NotNull(book.CoverJpeg);
        Assert.False(File.Exists(book.Azw3Path + ".tmp"));
    }

    [Fact]
    public async Task Export_volumes_converts_only_the_selected_volumes()
    {
        var comic = _store.UpsertComic(_provider.Details("Invencível"));
        await _downloader.DownloadNextAsync(comic.Id, 3, ct: Ct);
        _store.SetVolumeSize(comic.Id, 2);
        var exporter = new ComicExporter(_store, _paths, _downloader);
        var reported = new List<ComicExportProgress>();

        var all = await exporter.ExportVolumesAsync(comic.Id, KindleProfile.Default, ct: Ct);
        var last = await exporter.ExportVolumesAsync(comic.Id, KindleProfile.Default,
            plan => Core.Books.Volumes.Changed(plan, 2, everything: false), new SyncProgress(reported.Add), Ct);

        Assert.Equal([$"hq-{comic.Slug}-vol01.azw3", $"hq-{comic.Slug}-vol02.azw3"], all.Books.Select(b => b.KindleFileName));
        Assert.Equal([2, 1], all.Books.Select(b => b.ChapterCount));
        Assert.Equal(3, all.TotalChapters);
        Assert.Equal([2], last.Books.Select(b => b.Volume));
        Assert.Equal(1, reported[^1].Total);
    }

    private sealed class SyncProgress(Action<ComicExportProgress> report) : IProgress<ComicExportProgress>
    {
        public void Report(ComicExportProgress value) => report(value);
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class FakeComics(int chapters, int pagesPerChapter) : IComicProvider
    {
        private int _released = chapters;

        public void Release(int more) => _released += more;

        public ComicDetails Details(string title) => new("123", "HQ Now!", "/hq/invencivel", title, "Kirkman", "Sinopse",
            "Em andamento", ["Ação"], null, new Uri("https://hqnow.test/hq/invencivel"), _released);

        public Task<IReadOnlyList<ComicSourceInfo>> GetSourcesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ComicSourceInfo>>([new("123", "HQ Now!", "pt-BR", false, true)]);

        public Task<ComicBrowsePage> BrowseAsync(string sourceId, ComicListing listing, string? query, int page, CancellationToken ct = default) =>
            Task.FromResult(new ComicBrowsePage([], false));

        public Task<ComicDetails> GetComicAsync(string sourceId, string key, CancellationToken ct = default) =>
            Task.FromResult(Details("Invencível"));

        public Task<IReadOnlyList<ComicChapterInfo>> GetChaptersAsync(string sourceId, string key, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ComicChapterInfo>>(Enumerable.Range(1, _released).Reverse()
                .Select(n => new ComicChapterInfo($"/cap/{n}", $"#{n}", n, n, n))
                .ToList());

        public Task<IReadOnlyList<Uri>> GetPageUrlsAsync(ComicChapterInfo chapter, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<Uri>>(Enumerable.Range(0, pagesPerChapter)
                .Select(p => new Uri($"https://hqnow.test/page/{chapter.RemoteId}/{p}"))
                .ToList());

        public Task<byte[]> GetBytesAsync(Uri url, CancellationToken ct = default)
        {
            using var bitmap = new SKBitmap(600, 900);
            using (var canvas = new SKCanvas(bitmap))
            {
                canvas.Clear(SKColors.White);
                canvas.DrawRect(40, 40, 520, 820, new SKPaint { Color = SKColors.Black });
            }
            using var image = SKImage.FromBitmap(bitmap);
            return Task.FromResult(image.Encode(SKEncodedImageFormat.Png, 90).ToArray());
        }
    }
}
