using NovelShelf.Core.Books;
using NovelShelf.Core.Library;
using NovelShelf.Core.Sources;

namespace NovelShelf.Tests.Library;

public sealed class ResendTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novelshelf-resend-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Ranges_merge_and_survive_a_round_trip()
    {
        var merged = ChapterRange.Merge([new(200, 250), new(0, 10), new(5, 20), new(20, 30), new(-3, 0), new(40, 40)]);

        Assert.Equal([new ChapterRange(0, 30), new ChapterRange(200, 250)], merged);
        Assert.Equal("0-30,200-250", ChapterRange.Format(merged));
        Assert.Equal(merged, ChapterRange.Parse("0-30,200-250"));
        Assert.Empty(ChapterRange.Parse(null));
        Assert.Empty(ChapterRange.Parse("lixo,3"));
        Assert.Null(ChapterRange.Format([]));
        Assert.Equal(new ChapterRange(9, 20), ChapterRange.FromChapters(10, 20));
    }

    [Fact]
    public void Marked_chapters_bring_back_only_the_volumes_that_hold_them()
    {
        var plan = Volumes.Split(250, 100);

        Assert.Equal([2], Volumes.Changed(plan, 250, everything: false, [ChapterRange.FromChapters(150, 160)]).Select(v => v.Number));
        Assert.Equal([1, 2], Volumes.Changed(plan, 250, everything: false, [ChapterRange.FromChapters(100, 101)]).Select(v => v.Number));
        Assert.Equal([1, 3], Volumes.Changed(plan, 220, everything: false, [plan[0].Range]).Select(v => v.Number));
        Assert.Empty(Volumes.Changed(plan, 250, everything: false, []));

        var whole = Volumes.Split(250, 0);
        Assert.Equal([0], Volumes.Changed(whole, 250, everything: false, [ChapterRange.FromChapters(3, 3)]).Select(v => v.Number));
    }

    [Fact]
    public void Marking_a_sent_work_makes_it_pending_until_the_next_send()
    {
        var store = new LibraryStore(Path.Combine(_dir, "library.db"));
        var novel = store.Upsert(new NovelDetails("fake", "a", new Uri("https://fake.test/a"), "A", null, null, null, [], null, 3, null));
        store.MarkSent(novel.Id, 0);

        store.MarkResend(novel.Id, [new ChapterRange(0, 10)]);
        store.MarkResend(novel.Id, [new ChapterRange(5, 30), new ChapterRange(100, 200)]);
        var marked = store.GetNovel(novel.Id)!;
        Assert.Equal([new ChapterRange(0, 30), new ChapterRange(100, 200)], marked.ResendRanges);
        Assert.True(marked.ResendMarked);
        Assert.Equal(KindleState.Pending, LibraryFilter.KindleStateOf(marked));

        store.MarkSent(novel.Id, 0);
        var sent = store.GetNovel(novel.Id)!;
        Assert.Empty(sent.ResendRanges!);
        Assert.Equal(KindleState.UpToDate, LibraryFilter.KindleStateOf(sent));

        store.MarkResendAll(novel.Id);
        Assert.True(store.GetNovel(novel.Id)!.NeedsResend);
    }

    [Fact]
    public void A_work_never_sent_has_nothing_to_reset()
    {
        var store = new LibraryStore(Path.Combine(_dir, "library.db"));
        var novel = store.Upsert(new NovelDetails("fake", "b", new Uri("https://fake.test/b"), "B", null, null, null, [], null, 3, null));

        store.MarkResend(novel.Id, [new ChapterRange(0, 10)]);
        store.MarkResendAll(novel.Id);

        var untouched = store.GetNovel(novel.Id)!;
        Assert.Empty(untouched.ResendRanges!);
        Assert.False(untouched.NeedsResend);
        Assert.Equal(KindleState.NeverSent, LibraryFilter.KindleStateOf(untouched));
    }
}
