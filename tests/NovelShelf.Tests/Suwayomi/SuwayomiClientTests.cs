using System.Formats.Tar;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using NovelShelf.Core.Comics;
using NovelShelf.Suwayomi;

namespace NovelShelf.Tests.Suwayomi;

public sealed class SuwayomiClientTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;
    private static readonly Uri Base = new("http://127.0.0.1:45999/");

    private sealed class FakeHandler(Func<JsonObject, string> graph) : HttpMessageHandler
    {
        public List<JsonObject> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent([0xFF, 0xD8, 0xFF]) };
            var body = JsonNode.Parse(await request.Content!.ReadAsStringAsync(ct))!.AsObject();
            Requests.Add(body);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(graph(body), Encoding.UTF8, "application/json"),
            };
        }
    }

    private static (SuwayomiClient Client, FakeHandler Handler) Client(Func<JsonObject, string> graph)
    {
        var handler = new FakeHandler(graph);
        return (new SuwayomiClient(new HttpClient(handler), () => Base), handler);
    }

    [Fact]
    public async Task Browse_sends_search_variables_and_resolves_thumbnails_against_the_server()
    {
        var (client, handler) = Client(_ => """
            {"data":{"fetchSourceManga":{"hasNextPage":true,"mangas":[
              {"id":7,"url":"/obra/solo","title":"Solo","author":"Autor","thumbnailUrl":"/api/v1/manga/7/thumbnail"}]}}}
            """);

        var page = await client.BrowseAsync("123", ComicListing.Search, "solo", 2, Ct);

        var variables = handler.Requests.Single()["variables"]!;
        Assert.Equal("SEARCH", (string?)variables["type"]);
        Assert.Equal("solo", (string?)variables["query"]);
        Assert.Equal(2, (int?)variables["page"]);
        Assert.True(page.HasNextPage);
        var item = Assert.Single(page.Items);
        Assert.Equal(("123", "/obra/solo", "Solo"), (item.SourceId, item.Key, item.Title));
        Assert.Equal(new Uri(Base, "api/v1/manga/7/thumbnail"), item.ThumbnailUrl);
    }

    [Fact]
    public async Task Popular_listing_does_not_send_the_query()
    {
        var (client, handler) = Client(_ => """{"data":{"fetchSourceManga":{"hasNextPage":false,"mangas":[]}}}""");

        await client.BrowseAsync("123", ComicListing.Popular, "ignorado", 1, Ct);

        var variables = handler.Requests.Single()["variables"]!;
        Assert.Equal("POPULAR", (string?)variables["type"]);
        Assert.Null(variables["query"]);
    }

    [Fact]
    public async Task Chapters_are_resolved_by_source_and_url_and_returned_in_reading_order()
    {
        var (client, handler) = Client(body => ((string)body["query"]!).Contains("mangas(")
            ? """{"data":{"mangas":{"nodes":[{"id":42}]}}}"""
            : """
              {"data":{"fetchMangaAndChapters":{"chapters":[
                {"id":3,"url":"/c/3","name":"Cap 3","chapterNumber":3,"sourceOrder":3},
                {"id":1,"url":"/c/1","name":"Cap 1","chapterNumber":1,"sourceOrder":1},
                {"id":2,"url":"/c/2","name":"Cap 2","chapterNumber":2,"sourceOrder":2}]}}}
              """);

        var chapters = await client.GetChaptersAsync("123", "/obra/solo", Ct);

        Assert.Equal(["Cap 1", "Cap 2", "Cap 3"], chapters.Select(c => c.Name));
        Assert.Equal("/obra/solo", (string?)handler.Requests[0]["variables"]!["url"]);
        Assert.Equal(42, (int?)handler.Requests[1]["variables"]!["id"]);
    }

    [Fact]
    public async Task Pages_become_absolute_urls_on_the_local_server()
    {
        var (client, _) = Client(_ => """{"data":{"fetchChapterPages":{"pages":["/api/v1/manga/1/chapter/1/page/0","/api/v1/manga/1/chapter/1/page/1"]}}}""");

        var pages = await client.GetPageUrlsAsync(new ComicChapterInfo("/c/1", "Cap 1", 1, 1, 9), Ct);

        Assert.Equal([new Uri(Base, "api/v1/manga/1/chapter/1/page/0"), new Uri(Base, "api/v1/manga/1/chapter/1/page/1")], pages);
    }

    [Fact]
    public async Task Graphql_errors_become_readable_messages()
    {
        var (client, _) = Client(_ => """{"errors":[{"message":"Exception while fetching data (/fetchSourceManga) : HTTP error 503"}],"data":null}""");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.BrowseAsync("123", ComicListing.Popular, null, 1, Ct));

        Assert.Equal("HTTP error 503", error.Message);
    }

    [Fact]
    public async Task Missing_manga_tells_the_user_to_search_again()
    {
        var (client, _) = Client(_ => """{"data":{"mangas":{"nodes":[]}}}""");

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => client.GetChaptersAsync("123", "/sumiu", Ct));

        Assert.Contains("Busque e adicione de novo", error.Message);
    }

    [Fact]
    public async Task Nox_store_is_added_only_once()
    {
        var stores = new JsonArray();
        var (client, handler) = Client(body =>
        {
            if (((string)body["query"]!).Contains("addExtensionStore"))
            {
                stores.Add(new JsonObject { ["indexUrl"] = SuwayomiRelease.NoxRepositoryPrefix + "index.pb" });
                return """{"data":{"addExtensionStore":{"clientMutationId":null}}}""";
            }
            return new JsonObject { ["data"] = new JsonObject { ["extensionStores"] = new JsonObject { ["nodes"] = stores.DeepClone() } } }.ToJsonString();
        });

        await client.EnsureNoxStoreAsync(Ct);
        await client.EnsureNoxStoreAsync(Ct);

        Assert.Single(handler.Requests, r => ((string)r["query"]!).Contains("addExtensionStore"));
    }

    [Fact]
    public async Task Extensions_keep_content_warning()
    {
        var (client, _) = Client(_ => """
            {"data":{"extensions":{"nodes":[
              {"pkgName":"b","name":"B","lang":"pt-BR","versionName":"1.6.1","isInstalled":true,"hasUpdate":false,"isObsolete":false,"contentWarning":"NSFW","iconUrl":"/api/v1/extension/icon/b"},
              {"pkgName":"a","name":"A","lang":"pt-BR","versionName":"1.6.1","isInstalled":false,"hasUpdate":false,"isObsolete":false,"contentWarning":"SAFE","iconUrl":null}]}}}
            """);

        var extensions = await client.GetExtensionsAsync(refresh: false, Ct);

        Assert.Equal(["A", "B"], extensions.Select(e => e.Name));
        Assert.Equal(ContentWarning.Nsfw, extensions[1].ContentWarning);
        Assert.Equal(new Uri(Base, "api/v1/extension/icon/b"), extensions[1].IconUrl);
    }
}

public sealed class SuwayomiInstallerTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novelshelf-suwa-").FullName;

    [Fact]
    public void Tar_extraction_strips_the_top_folder_and_keeps_executable_bits()
    {
        var archive = Path.Combine(_dir, "jre.tar.gz");
        using (var file = File.Create(archive))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gzip))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.Directory, "jdk-21-jre/bin/"));
            var java = new PaxTarEntry(TarEntryType.RegularFile, "jdk-21-jre/bin/java")
            {
                DataStream = new MemoryStream("#!/bin/sh\n"u8.ToArray()),
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute,
            };
            tar.WriteEntry(java);
            tar.WriteEntry(new PaxTarEntry(TarEntryType.SymbolicLink, "jdk-21-jre/bin/java2") { LinkName = "java" });
        }
        var target = Path.Combine(_dir, "jre");
        Directory.CreateDirectory(target);

        SuwayomiInstaller.ExtractTarGz(archive, target);

        var extracted = Path.Combine(target, "bin", "java");
        Assert.True(File.Exists(extracted));
        if (!OperatingSystem.IsWindows())
            Assert.True(File.GetUnixFileMode(extracted).HasFlag(UnixFileMode.UserExecute));
        Assert.Equal("java", new FileInfo(Path.Combine(target, "bin", "java2")).LinkTarget);
    }

    [Fact]
    public void Tar_extraction_rejects_paths_outside_the_target()
    {
        var archive = Path.Combine(_dir, "evil.tar.gz");
        using (var file = File.Create(archive))
        using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
        using (var tar = new TarWriter(gzip))
        {
            tar.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, "jre/../../fora.txt") { DataStream = new MemoryStream([1]) });
        }
        var target = Path.Combine(_dir, "jre");
        Directory.CreateDirectory(target);

        Assert.Throws<InvalidDataException>(() => SuwayomiInstaller.ExtractTarGz(archive, target));
        Assert.False(File.Exists(Path.Combine(_dir, "fora.txt")));
    }

    [Fact]
    public async Task Install_rejects_a_download_with_the_wrong_checksum()
    {
        var installer = new SuwayomiInstaller(new SuwayomiPaths(_dir), new HttpClient(new BytesHandler([1, 2, 3])));

        await Assert.ThrowsAsync<InvalidDataException>(() => installer.InstallAsync(ct: TestContext.Current.CancellationToken));

        Assert.False(installer.IsInstalled);
        Assert.False(Directory.Exists(Path.Combine(_dir, "staging")));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class BytesHandler(byte[] bytes) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
    }
}
