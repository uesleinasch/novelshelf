using NovelShelf.Core.Net;
using NovelShelf.Sources.Illusia;

namespace NovelShelf.Tests.Sources;

public sealed class IllusiaParsingTests
{
    private readonly IllusiaSource _source = new(new PoliteHttpClient(TimeSpan.Zero));

    [Fact]
    public void ParseSearch_reads_story_cards_only()
    {
        const string html = """
            <ul>
            <li id="story-card-1" class="post-1 illusia-card illusia-card--story card _large _story"><article>
              <div class="illusia-card__cover"><a href="https://illusia.com.br/story/o-sistema-de-linhagem/">
                <img class="illusia-card__cover-img" src="https://illusia.com.br/wp-content/uploads/capa-400x534.webp" alt="Capa" /></a></div>
              <header><h2 class="illusia-card__title"><a href="https://illusia.com.br/story/o-sistema-de-linhagem/" class="illusia-card__title-link">O Sistema de Linhagem</a></h2>
              <p class="illusia-card__author">por <a href="https://illusia.com.br/author/timvic/" class="author">TimVic</a></p></header>
            </article></li>
            <li class="illusia-card illusia-card--chapter"><a class="illusia-card__title-link" href="https://illusia.com.br/story/x/capitulo-1/">Capítulo 1</a></li>
            <li class="illusia-card illusia-card--story"><a class="illusia-card__title-link" href="https://illusia.com.br/story/o-sistema-de-linhagem/">Duplicada</a></li>
            </ul>
            """;

        var results = _source.ParseSearch(html);

        var only = Assert.Single(results);
        Assert.Equal("O Sistema de Linhagem", only.Title);
        Assert.Equal("TimVic", only.Author);
        Assert.Equal("https://illusia.com.br/story/o-sistema-de-linhagem/", only.Url.ToString());
        Assert.EndsWith("capa-400x534.webp", only.CoverUrl!.ToString());
        Assert.Equal(IllusiaSource.SourceId, only.SourceId);
    }

    [Fact]
    public void ParseNovel_reads_header_taxonomies_and_full_chapter_list()
    {
        const string html = """
            <html><head>
            <script type="application/ld+json">{"@context":"https://schema.org","@type":"Book","name":"O Sistema de Linhagem",
              "author":{"@type":"Person","name":"tradutor"},"image":"https://illusia.com.br/wp-content/uploads/capa.webp"}</script>
            </head><body>
            <div class="illusia-single-story__meta"><h1 class="illusia-single-story__title">O Sistema de Linhagem</h1>
              <div class="illusia-single-story__credits"><span class="illusia-single-story__credits-primary">por <a href="#" class="author">TimVic</a></span>
              <span class="illusia-single-story__credits-secondary">Tradução: <a href="#" class="author">tradutor</a></span></div>
              <div class="illusia-single-story__badges"><span class="illusia-single-story__badge _hiatus"><i></i> Hiato </span>
              <span class="illusia-single-story__badge _rating-everyone">L</span></div></div>
            <div class="illusia-single-story__description content-section"><p>Primeiro parágrafo.</p><p>Segundo.</p></div>
            <div class="illusia-single-story__taxonomies tag-group">
              <a href="https://illusia.com.br/nacionalidade/ocidental/" class="tag-pill _taxonomy-fandom">Ocidental</a>
              <a href="https://illusia.com.br/genero/acao/" class="tag-pill _taxonomy-genre">Ação</a>
              <a href="https://illusia.com.br/genero/aventura/" class="tag-pill _taxonomy-genre">Aventura</a></div>
            <ol class="chapter-group__list">
              <li class="chapter-group__list-item _publish"><a href='https://illusia.com.br/story/o-sistema-de-linhagem/como-tudo-comecou/' class="chapter-group__list-item-link">Capitulo 1</a></li>
              <li class="chapter-group__list-item _publish"><a href='https://illusia.com.br/story/o-sistema-de-linhagem/capitulo-2/' class="chapter-group__list-item-link">Capitulo 2</a></li>
              <li class="chapter-group__list-item _folding-toggle"><button>Mostrar mais</button></li>
              <li class="chapter-group__list-item _publish"><a href='https://illusia.com.br/story/o-sistema-de-linhagem/capitulo-3/' class="chapter-group__list-item-link">Capitulo 3</a></li>
            </ol></body></html>
            """;

        var url = new Uri("https://illusia.com.br/story/o-sistema-de-linhagem/");
        var novel = _source.ParseNovel(html, "o-sistema-de-linhagem", url);

        Assert.Equal("O Sistema de Linhagem", novel.Title);
        Assert.Equal("TimVic", novel.Author);
        Assert.Equal("Primeiro parágrafo.\n\nSegundo.", novel.Synopsis);
        Assert.Equal("Hiato", novel.Status);
        Assert.Equal(["Ação", "Aventura"], novel.Genres);
        Assert.Equal(3, novel.ChapterCount);
        Assert.Equal("https://illusia.com.br/story/o-sistema-de-linhagem/como-tudo-comecou/", novel.FirstChapterUrl!.ToString());
        Assert.EndsWith("capa.webp", novel.CoverUrl!.ToString());
    }

    [Fact]
    public void ParseChapter_keeps_text_moves_footnotes_and_strips_next_fragment()
    {
        const string html = """
            <html><head><title>Capitulo 1 — Como Tudo Começou &#8211; Illusia</title></head><body>
            <h1 class="chapter__title">Como Tudo Começou</h1>
            <section id="chapter-content" class="chapter__content"><div class="resize-font chapter-formatting">
              <p id='paragraph-0' class="wp-block-paragraph">Olá &amp; <em>mundo</em><sup class="illusia-fn-sup"><a href="#fn-1" class="illusia-fn-ref">1</a></sup></p>
              <p class="wp-block-paragraph">&nbsp;</p><script>alert(1)</script>
              <aside class="illusia-fn-container" role="doc-endnotes"><div class="illusia-fn-separator"><span>❧</span></div>
                <ol class="illusia-fn-list"><li id="fn-1" class="illusia-fn-item"><span class="illusia-fn-content">N/T: nota.</span>
                <span class="illusia-fn-backlinks"> <a href="#fn-ref-1" class="illusia-fn-backref">&#8617;</a></span></li></ol></aside>
            </div></section>
            <a href="https://illusia.com.br/story/o-sistema-de-linhagem/capitulo-2/#start" class="button _secondary _navigation _next">Próximo</a>
            </body></html>
            """;

        var chapter = _source.ParseChapter(html, new Uri("https://illusia.com.br/story/o-sistema-de-linhagem/como-tudo-comecou/"));

        Assert.Equal("Capitulo 1 — Como Tudo Começou", chapter.Title);
        Assert.Equal("https://illusia.com.br/story/o-sistema-de-linhagem/capitulo-2/", chapter.NextUrl!.ToString());
        Assert.StartsWith("<p>Olá &amp; <em>mundo</em><sup>1</sup></p>", chapter.Html);
        Assert.Contains("<hr />", chapter.Html);
        Assert.Matches(@"<ol>\s*<li>N/T: nota\.\s*</li>\s*</ol>", chapter.Html);
        Assert.DoesNotContain("↩", chapter.Html);
        Assert.DoesNotContain("script", chapter.Html);
    }

    [Fact]
    public void ParseChapter_without_next_is_the_last_one()
    {
        const string html = """
            <html><head><title>Fim &#8211; Illusia</title></head><body>
            <section id="chapter-content"><div class="chapter-formatting"><p>Fim.</p></div></section></body></html>
            """;

        var chapter = _source.ParseChapter(html, new Uri("https://illusia.com.br/story/x/fim/"));

        Assert.Equal("Fim", chapter.Title);
        Assert.Null(chapter.NextUrl);
    }

    [Fact]
    public void ParseChapter_reports_password_protected_chapters()
    {
        const string html = """<html><body><form class="post-password-form"><input type="password"></form></body></html>""";

        var error = Assert.Throws<FormatException>(() => _source.ParseChapter(html, new Uri("https://illusia.com.br/story/x/vip/")));
        Assert.Contains("senha", error.Message);
    }

    [Theory]
    [InlineData("https://illusia.com.br/story/o-sistema-de-linhagem/", "o-sistema-de-linhagem")]
    [InlineData("https://illusia.com.br/story/o-sistema-de-linhagem/como-tudo-comecou/", "o-sistema-de-linhagem")]
    public void SlugFrom_accepts_story_and_chapter_urls(string url, string slug)
    {
        Assert.Equal(slug, IllusiaSource.SlugFrom(new Uri(url)));
        Assert.True(_source.CanHandle(new Uri(url)));
    }

    [Fact]
    public void SlugFrom_rejects_other_pages()
    {
        Assert.Throws<ArgumentException>(() => IllusiaSource.SlugFrom(new Uri("https://illusia.com.br/historias/")));
    }
}
