using NovelShelf.Core.Net;
using NovelShelf.Sources.Illusia;

namespace NovelShelf.Tests.Sources;

public sealed class IllusiaLiveTests
{
    private static bool Enabled => Environment.GetEnvironmentVariable("NOVELSHELF_LIVE") == "1";

    [Fact]
    public async Task Live_story_and_first_two_chapters()
    {
        Assert.SkipUnless(Enabled, "Defina NOVELSHELF_LIVE=1 para rodar contra o site real.");

        var ct = TestContext.Current.CancellationToken;
        using var http = new PoliteHttpClient(TimeSpan.FromSeconds(1));
        var source = new IllusiaSource(http);

        var novel = await source.GetNovelAsync(new Uri("https://illusia.com.br/story/o-sistema-de-linhagem/"), ct);
        Assert.Equal("O Sistema de Linhagem", novel.Title);
        Assert.True(novel.ChapterCount > 100);
        Assert.NotNull(novel.FirstChapterUrl);

        var first = await source.GetChapterAsync(novel.FirstChapterUrl!, ct);
        Assert.Contains("Capitulo 1", first.Title);
        Assert.StartsWith("<p>", first.Html);
        Assert.NotNull(first.NextUrl);

        var second = await source.GetChapterAsync(first.NextUrl!, ct);
        Assert.Contains("2", second.Title);

        var search = await source.SearchAsync("linhagem", ct);
        Assert.Contains(search, r => r.Url.AbsolutePath == "/story/o-sistema-de-linhagem/");
    }
}
