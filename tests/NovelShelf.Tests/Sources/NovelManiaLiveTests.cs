using NovelShelf.Core.Net;
using NovelShelf.Sources.NovelMania;

namespace NovelShelf.Tests.Sources;

public sealed class NovelManiaLiveTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("NOVELSHELF_LIVE") == "1";

    [Fact]
    public async Task Live_novel_and_first_two_chapters()
    {
        Assert.SkipUnless(Enabled, "Defina NOVELSHELF_LIVE=1 para rodar contra o site real.");

        var ct = TestContext.Current.CancellationToken;
        using var http = new PoliteHttpClient(TimeSpan.FromSeconds(1));
        var source = new NovelManiaSource(http);

        var novel = await source.GetNovelAsync(new Uri("https://novelmania.com.br/novels/overgeared"), ct);
        Assert.Equal("Superequipado", novel.Title);
        Assert.NotNull(novel.FirstChapterUrl);

        var first = await source.GetChapterAsync(novel.FirstChapterUrl!, ct);
        Assert.Contains("Prólogo", first.Title);
        Assert.StartsWith("<p>", first.Html);
        Assert.NotNull(first.NextUrl);

        var second = await source.GetChapterAsync(first.NextUrl!, ct);
        Assert.Contains("Capítulo 1", second.Title);

        var search = await source.SearchAsync("superequipado", ct);
        Assert.Contains(search, r => r.Url.AbsolutePath == "/novels/overgeared");
    }

    [Fact]
    public async Task Live_chapters_continue_after_the_first_volume_list()
    {
        Assert.SkipUnless(Enabled, "Defina NOVELSHELF_LIVE=1 para rodar contra o site real.");

        var ct = TestContext.Current.CancellationToken;
        using var http = new PoliteHttpClient(TimeSpan.FromSeconds(1));
        var source = new NovelManiaSource(http);

        var novel = await source.GetNovelAsync(new Uri("https://novelmania.com.br/novels/contra-os-deuses"), ct);
        Assert.Null(novel.ChapterCount);

        var lastListed = await source.GetChapterAsync(
            new Uri("https://novelmania.com.br/novels/contra-os-deuses/capitulos/volume-1-arco-5-capitulo-49"), ct);
        Assert.NotNull(lastListed.NextUrl);

        var next = await source.GetChapterAsync(lastListed.NextUrl!, ct);
        Assert.Contains("50", next.Title);
        Assert.StartsWith("<p>", next.Html);
        Assert.True(next.Html.Length > 1000);
        Assert.NotNull(next.NextUrl);
    }
}
