using NovelShelf.Core.Net;
using NovelShelf.Sources.NovelMania;

namespace NovelShelf.Tests.Sources;

public sealed class NovelManiaParsingTests
{
    private readonly NovelManiaSource _source = new(new PoliteHttpClient(TimeSpan.Zero));

    [Fact]
    public void ParseNovel_reads_json_ld_and_chapter_count()
    {
        const string html = """
            <html><head>
            <script type="application/ld+json">{"@type":"BreadcrumbList","itemListElement":[]}</script>
            <script type="application/ld+json">{"@context":"https://schema.org","@type":"Book","name":"Livro Teste",
              "description":"Uma sinopse.","image":"https://assets.novelmania.com.br/uploads/novel/cover/1/hero_capa.jpg",
              "author":{"@type":"Person","name":"Fulano"},"genre":["Ação","Fantasia"],"creativeWorkStatus":"Em andamento",
              "potentialAction":{"@type":"ReadAction","target":"https://novelmania.com.br/novels/livro-teste/capitulos/volume-1-prologo"}}</script>
            </head><body><span><span>12<!-- --> <!-- -->capítulos</span></span></body></html>
            """;

        var novel = _source.ParseNovel(html, "livro-teste", new Uri("https://novelmania.com.br/novels/livro-teste"));

        Assert.Equal("Livro Teste", novel.Title);
        Assert.Equal("Fulano", novel.Author);
        Assert.Equal("Uma sinopse.", novel.Synopsis);
        Assert.Equal(["Ação", "Fantasia"], novel.Genres);
        Assert.Equal("Em andamento", novel.Status);
        Assert.Equal(12, novel.ChapterCount);
        Assert.Equal("https://novelmania.com.br/novels/livro-teste/capitulos/volume-1-prologo", novel.FirstChapterUrl!.ToString());
        Assert.EndsWith("hero_capa.jpg", novel.CoverUrl!.ToString());
    }

    [Fact]
    public void ParseChapter_extracts_title_clean_body_and_next_link()
    {
        const string html = """
            <html><head>
            <link rel="next" href="https://novelmania.com.br/novels/livro-teste/capitulos/volume-1-capitulo-2"/>
            <script type="application/ld+json">{"@type":"Chapter","name":"Volume 1 – Capítulo 1 – Livro Teste"}</script>
            </head><body>
            <h1>Capítulo 1<span class="sr-only"> – Livro Teste</span></h1>
            <div class="rich-text-content prose"><p data-wahfont="17">Olá &amp; <em>mundo</em> &lt;3</p>
            <p data-wahfont="17"> </p><script>alert(1)</script><p class="x"><strong>Fim</strong><br>linha</p><img src="a.png"></div>
            </body></html>
            """;

        var chapter = _source.ParseChapter(html, new Uri("https://novelmania.com.br/novels/livro-teste/capitulos/volume-1-capitulo-1"));

        Assert.Equal("Volume 1 – Capítulo 1", chapter.Title);
        Assert.Equal("<p>Olá &amp; <em>mundo</em> &lt;3</p>\n<p><strong>Fim</strong><br />linha</p>", chapter.Html);
        Assert.Equal("https://novelmania.com.br/novels/livro-teste/capitulos/volume-1-capitulo-2", chapter.NextUrl!.ToString());
    }

    [Fact]
    public void ParseChapter_without_next_link_marks_end()
    {
        const string html = """<html><body><h1>Último</h1><div class="rich-text-content"><p>x</p></div></body></html>""";

        var chapter = _source.ParseChapter(html, new Uri("https://novelmania.com.br/novels/a/capitulos/b"));

        Assert.Null(chapter.NextUrl);
        Assert.Equal("Último", chapter.Title);
    }

    [Fact]
    public void ParseSearch_reads_cards_and_prefers_jpg_cover()
    {
        const string html = """
            <html><body>
            <a href="/novels">Novels</a>
            <ul><li><div><a href="/novels/o-guerreiro"><div><img src="https://x/card.webp"
               srcSet="https://x/mini.webp 128w, https://x/card.webp 320w, https://x/hero.jpg 600w" alt="O Guerreiro"/></div>
               <p>O Guerreiro</p></a><div>Hugo</div></div></li>
            <li><div><a href="/novels/o-guerreiro"><img alt="O Guerreiro" src="https://x/dup.webp"/></a></div></li></ul>
            </body></html>
            """;

        var results = _source.ParseSearch(html);

        var single = Assert.Single(results);
        Assert.Equal("O Guerreiro", single.Title);
        Assert.Equal("Hugo", single.Author);
        Assert.Equal("https://x/hero.jpg", single.CoverUrl!.ToString());
        Assert.Equal("https://novelmania.com.br/novels/o-guerreiro", single.Url.ToString());
    }

    [Theory]
    [InlineData("https://novelmania.com.br/novels/overgeared", "overgeared")]
    [InlineData("https://novelmania.com.br/novels/overgeared/capitulos/volume-1-prologo", "overgeared")]
    public void SlugFrom_accepts_novel_and_chapter_urls(string url, string slug) =>
        Assert.Equal(slug, NovelManiaSource.SlugFrom(new Uri(url)));
}
