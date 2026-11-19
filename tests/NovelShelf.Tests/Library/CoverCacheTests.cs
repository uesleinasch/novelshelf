using System.Net;
using NovelShelf.Core.Books;
using NovelShelf.Core.Library;
using NovelShelf.Core.Net;
using NovelShelf.Core.Sources;
using SkiaSharp;

namespace NovelShelf.Tests.Library;

public sealed class CoverCacheTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novelshelf-covers-").FullName;
    private readonly AppPaths _paths;
    private readonly CoverCache _cache;

    public CoverCacheTests()
    {
        _paths = new AppPaths(_dir);
        _paths.EnsureCreated();
        _cache = new CoverCache(_paths);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private static readonly Uri A = new("https://site.test/a.webp");
    private static readonly Uri B = new("https://site.test/b.webp");

    [Fact]
    public void Cover_is_only_reused_for_the_same_source()
    {
        _cache.Save(7, A, [1, 2, 3]);

        Assert.Equal([1, 2, 3], _cache.TryRead(7, A));
        Assert.Null(_cache.TryRead(7, B));
        Assert.Null(_cache.TryRead(8, A));
    }

    [Fact]
    public void Old_cover_without_source_is_not_trusted_for_a_work_with_a_link()
    {
        File.WriteAllBytes(_cache.PathFor(7), [9, 9]);

        Assert.Null(_cache.TryRead(7, A));
        Assert.Equal([9, 9], _cache.TryRead(7, null));
    }

    [Fact]
    public void Forget_removes_the_cover_and_its_source()
    {
        _cache.Save(7, A, [1]);
        _cache.Forget(7);

        Assert.Null(_cache.TryRead(7, A));
        Assert.Null(_cache.TryRead(7, null));
        Assert.Empty(Directory.GetFiles(_paths.Covers));
    }

    [Fact]
    public void Saving_without_source_drops_the_old_source()
    {
        _cache.Save(7, A, [1]);
        _cache.Save(7, null, [2]);

        Assert.Equal([2], _cache.TryRead(7, null));
        Assert.Null(_cache.TryRead(7, A));
    }

    private sealed class Images(Dictionary<string, byte[]> files) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!.ToString());
            return Task.FromResult(files.TryGetValue(request.RequestUri!.ToString(), out var bytes)
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static byte[] Jpeg(SKColor color)
    {
        using var bitmap = new SKBitmap(30, 40);
        using (var canvas = new SKCanvas(bitmap))
            canvas.Clear(color);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    [Fact]
    public async Task Book_ignores_a_leftover_cover_from_a_removed_work_with_the_same_id()
    {
        var ct = TestContext.Current.CancellationToken;
        var store = new LibraryStore(_paths.Database);
        NovelDetails Details(string slug, Uri cover) => new("fake", slug, new Uri($"https://fake.test/{slug}"), slug, null, null, cover, [], null, 1, null);
        var removed = store.Upsert(Details("antiga", A));
        var stale = Jpeg(SKColors.Red);
        _cache.Save(removed.Id, A, stale);
        store.DeleteNovel(removed.Id);

        var added = store.Upsert(Details("nova", B));
        Assert.Equal(removed.Id, added.Id);
        store.AddChapter(added.Id, 0, new ChapterPage(new Uri("https://fake.test/nova/1"), "Cap 1", "<p>x</p>", null));
        var fresh = Jpeg(SKColors.Blue);
        var handler = new Images(new() { [B.ToString()] = fresh });
        using var http = new PoliteHttpClient(TimeSpan.Zero, handler);

        var book = await new BookExporter(store, _paths, http).ExportAsync(added.Id, ct: ct);

        Assert.Equal(fresh, book.CoverJpeg);
        Assert.Equal([B.ToString()], handler.Requests);
        Assert.Equal(fresh, _cache.TryRead(added.Id, B));
    }
}
