using System.Diagnostics;
using NovelShelf.Comics;
using NovelShelf.Core.Comics;
using NovelShelf.Core.Library;
using NovelShelf.Suwayomi;

namespace NovelShelf.Tests.Suwayomi;

public sealed class SuwayomiLiveTests
{
    private const string HqNow = "eu.kanade.tachiyomi.extension.pt.hqnow";

    [Fact]
    public async Task Server_runs_the_nox_extensions_and_stops_with_the_app()
    {
        var root = Environment.GetEnvironmentVariable("NOVELSHELF_SUWAYOMI_ROOT");
        Assert.SkipWhen(string.IsNullOrEmpty(root), "Defina NOVELSHELF_SUWAYOMI_ROOT para testar o servidor de mangás de verdade.");
        var ct = TestContext.Current.CancellationToken;

        var paths = new SuwayomiPaths(root!);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var installer = new SuwayomiInstaller(paths, http);
        if (!installer.IsInstalled)
            await installer.InstallAsync(ct: ct);
        await using var server = new SuwayomiServer(paths, installer, http);
        var client = new SuwayomiClient(http, () => server.BaseUrl);

        await server.StartAsync(ct);
        var pid = int.Parse(await File.ReadAllTextAsync(paths.PidFile, ct));
        await client.EnsureNoxStoreAsync(ct);
        var extensions = await client.GetExtensionsAsync(refresh: true, ct);
        Assert.Contains(extensions, e => e.Language == "pt-BR");
        await client.SetExtensionAsync(HqNow, install: true, ct: ct);

        var source = (await client.GetSourcesAsync(ct)).Single(s => s.Name == "HQ Now!");
        var popular = await client.BrowseAsync(source.Id, ComicListing.Popular, null, 1, ct);
        var first = popular.Items[0];
        var details = await client.GetComicAsync(source.Id, first.Key, ct);
        var chapters = await client.GetChaptersAsync(source.Id, first.Key, ct);
        var pages = await client.GetPageUrlsAsync(chapters[0], ct);
        var image = await client.GetBytesAsync(pages[0], ct);

        Assert.False(string.IsNullOrWhiteSpace(details.Title));
        Assert.NotEmpty(chapters);
        Assert.True(image.Length > 1000);

        await server.StopAsync();
        Assert.Equal(SuwayomiState.Stopped, server.State);
        Assert.Throws<ArgumentException>(() => Process.GetProcessById(pid));
    }

    [Theory]
    [InlineData("eu.kanade.tachiyomi.extension.pt.hqnow", "HQ Now!", false)]
    [InlineData("eu.kanade.tachiyomi.extension.pt.akaitomangas", "Akaito Mangás", false)]
    public async Task Real_chapter_becomes_a_kindle_book(string pkg, string sourceName, bool rightToLeft)
    {
        var root = Environment.GetEnvironmentVariable("NOVELSHELF_SUWAYOMI_ROOT");
        Assert.SkipWhen(string.IsNullOrEmpty(root), "Defina NOVELSHELF_SUWAYOMI_ROOT para testar o servidor de mangás de verdade.");
        var ct = TestContext.Current.CancellationToken;
        var paths = new SuwayomiPaths(root!);
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var installer = new SuwayomiInstaller(paths, http);
        await using var server = new SuwayomiServer(paths, installer, http);
        var client = new SuwayomiClient(http, () => server.BaseUrl);
        await server.StartAsync(ct);
        await client.EnsureNoxStoreAsync(ct);
        await client.GetExtensionsAsync(refresh: true, ct);
        await client.SetExtensionAsync(pkg, install: true, ct: ct);
        var source = (await client.GetSourcesAsync(ct)).Single(s => s.Name == sourceName);
        var first = (await client.BrowseAsync(source.Id, ComicListing.Popular, null, 1, ct)).Items[0];

        var library = new AppPaths(Directory.CreateTempSubdirectory("novelshelf-live-").FullName);
        library.EnsureCreated();
        var store = new LibraryStore(library.Database);
        var comic = store.UpsertComic(await client.GetComicAsync(source.Id, first.Key, ct));
        store.SetRightToLeft(comic.Id, rightToLeft);
        var downloader = new ComicDownloader(store, client, library);
        var download = await downloader.DownloadNextAsync(comic.Id, 1, ct: ct);
        var book = await new ComicExporter(store, library, downloader).ExportAsync(comic.Id, KindleProfile.Default, ct: ct);

        Assert.Equal(1, download.Downloaded);
        Assert.True(new FileInfo(book.Azw3Path).Length > 50_000);
        var dump = Environment.GetEnvironmentVariable("NOVELSHELF_DUMP_DIR");
        if (!string.IsNullOrEmpty(dump))
        {
            Directory.CreateDirectory(dump);
            File.Copy(book.Azw3Path, Path.Combine(dump, book.KindleFileName), overwrite: true);
            var pages = downloader.PageFiles(comic.Id, store.GetComicChapters(comic.Id)[0]);
            var processed = ComicPageProcessor.ProcessChapter(pages.Select(File.ReadAllBytes).ToList(), new ComicPageOptions(KindleProfile.Default, rightToLeft));
            var sample = Path.Combine(dump, Path.GetFileNameWithoutExtension(book.KindleFileName));
            Directory.CreateDirectory(sample);
            for (var i = 0; i < Math.Min(4, processed.Count); i++)
                File.WriteAllBytes(Path.Combine(sample, $"pagina-{i:D2}.jpg"), processed[i]);
            File.WriteAllText(Path.Combine(sample, "info.txt"), $"{comic.Title}\n{pages.Count} páginas originais -> {processed.Count} páginas no Kindle\nwebtoon: {ComicPageProcessor.IsWebtoon(pages.Select(File.ReadAllBytes).ToList())}\n");
        }
        await server.StopAsync();
    }
}
