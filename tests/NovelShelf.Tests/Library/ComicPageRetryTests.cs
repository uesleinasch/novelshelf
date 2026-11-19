using System.Net;
using NovelShelf.Core.Comics;
using NovelShelf.Core.Library;
using NovelShelf.Core.Net;

namespace NovelShelf.Tests.Library;

public sealed class ComicPageRetryTests : IDisposable
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 19, 22, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class FlakyComics : IComicProvider
    {
        public Dictionary<int, Queue<HttpStatusCode>> Failures { get; } = [];
        public List<Uri> Requests { get; } = [];
        public int PageListCalls { get; private set; }
        public int Pages { get; set; } = 4;

        public Task<IReadOnlyList<ComicSourceInfo>> GetSourcesAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ComicSourceInfo>>([]);

        public Task<ComicBrowsePage> BrowseAsync(string sourceId, ComicListing listing, string? query, int page, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<ComicDetails> GetComicAsync(string sourceId, string key, CancellationToken ct = default) => Task.FromResult(Details);

        public static ComicDetails Details { get; } = new("src", "Fonte", "/manhua", "Manhua", null, null, null, [], null,
            new Uri("https://fonte.test/manhua"), 1);

        public Task<IReadOnlyList<ComicChapterInfo>> GetChaptersAsync(string sourceId, string key, CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ComicChapterInfo>>([new ComicChapterInfo("/cap/1", "Cap 1", 1, 1, 1)]);

        public Task<IReadOnlyList<Uri>> GetPageUrlsAsync(ComicChapterInfo chapter, CancellationToken ct = default)
        {
            PageListCalls++;
            return Task.FromResult<IReadOnlyList<Uri>>(Enumerable.Range(0, Pages)
                .Select(p => new Uri($"https://local.test/page/{p}?list={PageListCalls}")).ToList());
        }

        public Task<byte[]> GetBytesAsync(Uri url, CancellationToken ct = default)
        {
            Requests.Add(url);
            var page = int.Parse(url.AbsolutePath.Split('/')[^1]);
            if (Failures.TryGetValue(page, out var pending) && pending.Count > 0)
            {
                var status = pending.Dequeue();
                throw new HttpRequestException($"falhou ({(int)status})", null, status);
            }
            return Task.FromResult<byte[]>([0xFF, 0xD8, 0xFF, (byte)page]);
        }
    }

    private readonly string _dir = Directory.CreateTempSubdirectory("novelshelf-retry-").FullName;
    private readonly AppPaths _paths;
    private readonly LibraryStore _store;
    private readonly FlakyComics _provider = new();
    private readonly Clock _clock = new();
    private readonly List<TimeSpan> _sleeps = [];
    private readonly ComicDownloader _downloader;
    private readonly long _comicId;

    public ComicPageRetryTests()
    {
        _paths = new AppPaths(_dir);
        _paths.EnsureCreated();
        _store = new LibraryStore(_paths.Database);
        _downloader = new ComicDownloader(_store, _provider, _paths, _clock, (delay, _) =>
        {
            _sleeps.Add(delay);
            _clock.Now += delay;
            return Task.CompletedTask;
        });
        _comicId = _store.UpsertComic(FlakyComics.Details).Id;
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Queue<HttpStatusCode> Fail(int times, HttpStatusCode status = HttpStatusCode.InternalServerError) =>
        new(Enumerable.Repeat(status, times));

    [Fact]
    public async Task Pages_are_paced_per_source()
    {
        await _downloader.DownloadNextAsync(_comicId, 1, ct: Ct);

        Assert.Equal(4, _provider.Requests.Count);
        Assert.Equal(3, _sleeps.Count(s => s == ComicDownloader.BasePageInterval));
    }

    [Fact]
    public async Task Failing_page_waits_retries_and_refreshes_the_page_list()
    {
        _provider.Failures[2] = Fail(2);

        var result = await _downloader.DownloadNextAsync(_comicId, 1, ct: Ct);

        Assert.Equal(1, result.Downloaded);
        Assert.Contains(TimeSpan.FromSeconds(15), _sleeps);
        Assert.Contains(TimeSpan.FromSeconds(30), _sleeps);
        Assert.Equal(2, _provider.PageListCalls);
        Assert.Equal(TimeSpan.FromSeconds(2), _downloader.PageIntervalFor("src"));
        Assert.Equal(4, _downloader.PageFiles(_comicId, _store.GetComicChapters(_comicId)[0]).Count);
    }

    [Fact]
    public async Task Persistent_failure_pauses_and_the_next_run_resumes_at_the_failed_page()
    {
        _provider.Failures[2] = Fail(6, HttpStatusCode.TooManyRequests);

        var error = await Assert.ThrowsAsync<RateLimitedException>(() => _downloader.DownloadNextAsync(_comicId, 1, ct: Ct));
        Assert.Equal(_clock.Now + PoliteHttpClient.GiveUpPause, error.RetryAt);
        Assert.Empty(_store.GetComicChapters(_comicId));

        _provider.Requests.Clear();
        var result = await _downloader.DownloadNextAsync(_comicId, 1, ct: Ct);

        Assert.Equal(1, result.Downloaded);
        Assert.Equal(["/page/2", "/page/3"], _provider.Requests.Select(u => u.AbsolutePath));
        Assert.Equal(4, _downloader.PageFiles(_comicId, _store.GetComicChapters(_comicId)[0]).Count);
    }

    [Fact]
    public async Task Missing_page_fails_at_once()
    {
        _provider.Failures[1] = Fail(1, HttpStatusCode.NotFound);

        var error = await Assert.ThrowsAsync<HttpRequestException>(() => _downloader.DownloadNextAsync(_comicId, 1, ct: Ct));

        Assert.IsNotType<RateLimitedException>(error);
        Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task Changed_page_count_starts_the_chapter_over()
    {
        _provider.Failures[2] = Fail(6);
        await Assert.ThrowsAsync<RateLimitedException>(() => _downloader.DownloadNextAsync(_comicId, 1, ct: Ct));

        _provider.Pages = 5;
        _provider.Requests.Clear();
        await _downloader.DownloadNextAsync(_comicId, 1, ct: Ct);

        Assert.Equal(5, _provider.Requests.Count);
    }
}
