using NovelShelf.Comics;
using NovelShelf.Core.Books;
using NovelShelf.Core.Comics;
using NovelShelf.Core.Library;
using NovelShelf.Core.Sources;
using NovelShelf.Ebook;

namespace NovelShelf.Tests.Library;

public sealed class BookStyleStoreTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novelshelf-style-").FullName;
    private readonly LibraryStore _store;

    public BookStyleStoreTests() => _store = new LibraryStore(Path.Combine(_dir, "library.db"));

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private LibraryNovel Novel(string slug) => _store.Upsert(new NovelDetails("fake", slug, new Uri($"https://fake.test/{slug}"),
        slug, null, null, null, [], null, 3, null));

    private LibraryNovel Reload(LibraryNovel novel) => _store.GetNovel(novel.Id)!;

    [Fact]
    public void Book_style_is_stored_per_novel()
    {
        var novel = Novel("a");
        Assert.Null(_store.GetBookStyle(novel.Id));

        var style = new BookStyle { Indent = 0, Heading = ChapterHeading.Hidden };
        _store.SetBookStyle(novel.Id, style);
        Assert.Equal(style, _store.GetBookStyle(novel.Id));

        _store.SetBookStyle(novel.Id, null);
        Assert.Null(_store.GetBookStyle(novel.Id));
    }

    [Fact]
    public void Changing_style_of_sent_novel_asks_for_resend_until_sent_again()
    {
        var sent = Novel("enviada");
        var unsent = Novel("nova");
        _store.MarkSent(sent.Id, 0);

        _store.SetBookStyle(sent.Id, new BookStyle { Justify = false });
        _store.SetBookStyle(unsent.Id, new BookStyle { Justify = false });

        Assert.True(Reload(sent).NeedsResend);
        Assert.Equal(KindleState.Pending, LibraryFilter.KindleStateOf(Reload(sent)));
        Assert.False(Reload(unsent).NeedsResend);

        _store.MarkSent(sent.Id, 0);
        Assert.False(Reload(sent).NeedsResend);
        Assert.Equal(KindleState.UpToDate, LibraryFilter.KindleStateOf(Reload(sent)));

        _store.SetBookStyle(sent.Id, new BookStyle { Justify = false });
        Assert.False(Reload(sent).NeedsResend);
    }

    [Fact]
    public void Default_style_change_only_flags_sent_novels_without_own_style()
    {
        var plain = Novel("padrao");
        var custom = Novel("propria");
        var comic = _store.UpsertComic(new ComicDetails("123", "HQ", "/hq/x", "HQ", null, null, null, [], null,
            new Uri("https://hq.test/x"), 2));
        _store.SetBookStyle(custom.Id, new BookStyle { Indent = 0 });
        foreach (var id in new[] { plain.Id, custom.Id, comic.Id })
            _store.MarkSent(id, 0);

        _store.MarkDefaultStyleChanged();

        Assert.True(Reload(plain).NeedsResend);
        Assert.False(Reload(custom).NeedsResend);
        Assert.False(Reload(comic).NeedsResend);
    }

    [Fact]
    public void Changing_comic_layout_of_sent_comic_asks_for_resend()
    {
        var comic = _store.UpsertComic(new ComicDetails("123", "HQ", "/hq/x", "HQ", null, null, null, [], null,
            new Uri("https://hq.test/x"), 2));
        _store.MarkSent(comic.Id, 0);

        _store.SetComicLayout(comic.Id, ComicLayout.Default);
        Assert.False(Reload(comic).NeedsResend);

        _store.SetComicLayout(comic.Id, ComicLayout.Default with { Gamma = 1.4 });
        Assert.True(Reload(comic).NeedsResend);
    }

    [Fact]
    public void Mobi_uses_style_css_and_heading()
    {
        BookContent Book(BookStyle style) => new("urn:uuid:1", "Livro", null, "pt-BR", null, DateTimeOffset.UnixEpoch, null,
            [new BookChapter("Capítulo 1 - Início", "<p>Texto</p>")], style);

        var shown = BookExporter.ToMobi(Book(new BookStyle { Justify = false }), 1);
        var hidden = BookExporter.ToMobi(Book(new BookStyle { Heading = ChapterHeading.Hidden }), 1);

        Assert.Contains("text-align: left", shown.CssFlows.Single());
        Assert.Contains("<h1>Capítulo 1 - Início</h1>", string.Concat(shown.Chapters[0].Chunks));
        Assert.DoesNotContain("<h1>", string.Concat(hidden.Chapters[0].Chunks));
    }
}
