using System.Diagnostics;
using NovelShelf.Core.Comics;
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
}
