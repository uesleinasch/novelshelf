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
        Assert.True(novel.ChapterCount > 100);
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
}
