using NovelShelf.Core.Library;

namespace NovelShelf.Tests.Library;

public sealed class LibraryFilterTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static LibraryNovel Work(long id, string title, string site, WorkKind kind = WorkKind.Novel,
        string? author = null, int downloaded = 0, int sent = 0, bool everSent = false, int addedDay = 0, int updatedDay = 0) =>
        new(id, site, $"slug-{id}", new Uri($"https://example.com/{id}"), title, author, null, null, null, [], null, null,
            $"book-{id}", downloaded, sent, T0.AddDays(addedDay), T0.AddDays(updatedDay), everSent ? T0 : null,
            kind, SourceName: site);

    private static readonly LibraryNovel[] Library =
    [
        Work(1, "Superequipado", "Novel Mania", author: "Park Saenal", downloaded: 10, sent: 10, everSent: true, addedDay: 1, updatedDay: 5),
        Work(2, "O Sistema de Linhagem", "Illusia", author: "TimVic", downloaded: 30, sent: 20, everSent: true, addedDay: 3, updatedDay: 2),
        Work(3, "Ásura", "Illusia", downloaded: 5, addedDay: 2, updatedDay: 9),
        Work(4, "One Piece", "Mangá Livre", WorkKind.Comic, author: "Eiichiro Oda", downloaded: 3, addedDay: 4, updatedDay: 1),
    ];

    private static IReadOnlyList<long> Ids(LibraryFilter filter) =>
        filter.Apply(Library, n => n, n => n.SourceName!).Select(n => n.Id).ToList();

    [Fact]
    public void Default_lists_everything_by_last_update()
    {
        Assert.Equal([3, 1, 2, 4], Ids(new LibraryFilter()));
        Assert.False(new LibraryFilter().IsActive);
    }

    [Fact]
    public void Filters_by_kind_and_site()
    {
        Assert.Equal([4], Ids(new LibraryFilter(Kind: WorkKind.Comic)));
        Assert.Equal([3, 2], Ids(new LibraryFilter(Site: "illusia")));
        Assert.Empty(Ids(new LibraryFilter(Kind: WorkKind.Comic, Site: "Illusia")));
    }

    [Theory]
    [InlineData(KindleState.Pending, new long[] { 2 })]
    [InlineData(KindleState.UpToDate, new long[] { 1 })]
    [InlineData(KindleState.NeverSent, new long[] { 3, 4 })]
    public void Filters_by_kindle_state(KindleState state, long[] expected)
    {
        Assert.Equal(expected, Ids(new LibraryFilter(Kindle: state)));
    }

    [Fact]
    public void Text_matches_title_or_author_ignoring_case_and_accents()
    {
        Assert.Equal([3], Ids(new LibraryFilter(Text: "asura")));
        Assert.Equal([2], Ids(new LibraryFilter(Text: " timvic ")));
        Assert.Equal([2], Ids(new LibraryFilter(Text: "linhagem")));
        Assert.True(new LibraryFilter(Text: "x").IsActive);
        Assert.False(new LibraryFilter(Text: "  ").IsActive);
    }

    [Fact]
    public void Sorts_by_title_or_added_date()
    {
        Assert.Equal([3, 2, 4, 1], Ids(new LibraryFilter(Sort: LibrarySort.Title)));
        Assert.Equal([4, 2, 3, 1], Ids(new LibraryFilter(Sort: LibrarySort.Added)));
    }
}
