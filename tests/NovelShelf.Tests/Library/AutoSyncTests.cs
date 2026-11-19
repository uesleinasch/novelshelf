using NovelShelf.Core.Downloading;
using NovelShelf.Core.Library;
using NovelShelf.Core.Sources;

namespace NovelShelf.Tests.Library;

public sealed class AutoSyncTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 1, 12, 0, 0, TimeSpan.Zero);

    private static LibraryNovel Work(long id, WorkKind kind = WorkKind.Novel, bool caughtUp = false,
        int downloaded = 0, int sent = 0, bool everSent = false, bool needsResend = false) =>
        new(id, "fake", $"slug-{id}", new Uri($"https://example.com/{id}"), $"Obra {id}", null, null, null, null, [], null, null,
            $"book-{id}", downloaded, sent, T0, T0, everSent ? T0 : null, kind, NeedsResend: needsResend, CaughtUp: caughtUp);

    [Fact]
    public void Only_caught_up_works_are_updated_and_comics_only_when_asked()
    {
        LibraryNovel[] works =
        [
            Work(1, caughtUp: true),
            Work(2, caughtUp: false),
            Work(3, WorkKind.Comic, caughtUp: true),
        ];

        Assert.Equal([1L], AutoSync.UpdateCandidates(works, includeComics: false).Select(w => w.Id));
        Assert.Equal([1L, 3], AutoSync.UpdateCandidates(works, includeComics: true).Select(w => w.Id));
    }

    [Fact]
    public void Pending_for_kindle_skips_never_sent_and_up_to_date_works()
    {
        LibraryNovel[] works =
        [
            Work(1, downloaded: 10, sent: 8, everSent: true),
            Work(2, downloaded: 10, sent: 10, everSent: true),
            Work(3, downloaded: 10),
            Work(4, downloaded: 10, sent: 10, everSent: true, needsResend: true),
        ];

        Assert.Equal([1L, 4], AutoSync.PendingForKindle(works).Select(w => w.Id));
    }

    [Fact]
    public void Update_round_finishes_when_its_jobs_stop_and_reports_new_chapters()
    {
        var round = new UpdateRound([1, 2, 3]);
        DownloadJob[] running =
        [
            new(1, "A", "x", 1000, Done: 3, State: DownloadState.Done, ReachedEnd: true),
            new(2, "B", "x", 1000, State: DownloadState.Running),
            new(9, "Outra", "y", 5, State: DownloadState.Running),
        ];
        Assert.False(round.IsFinished(running));

        DownloadJob[] finished =
        [
            running[0],
            new(2, "B", "x", 1000, State: DownloadState.Done, ReachedEnd: true),
            new(3, "C", "z", 1000, State: DownloadState.Failed, Detail: "fora do ar"),
            running[2],
        ];
        Assert.True(round.IsFinished(finished));
        Assert.Equal([new UpdateResult(1, "A", 3)], round.Results(finished));
        Assert.Equal([3L], round.Failures(finished).Select(f => f.NovelId));
    }

    [Fact]
    public void Paused_job_does_not_hold_the_round()
    {
        var round = new UpdateRound([1]);
        Assert.True(round.IsFinished([new DownloadJob(1, "A", "x", 1000, Done: 1, State: DownloadState.Paused)]));
    }

    [Fact]
    public void Caught_up_flag_is_stored()
    {
        var dir = Directory.CreateTempSubdirectory("novelshelf-sync-").FullName;
        try
        {
            var store = new LibraryStore(Path.Combine(dir, "library.db"));
            var novel = store.Upsert(new NovelDetails("fake", "a", new Uri("https://fake.test/a"), "A", null, null, null, [], null, 3, null));
            Assert.False(novel.CaughtUp);

            store.SetCaughtUp(novel.Id, true);
            Assert.True(store.GetNovel(novel.Id)!.CaughtUp);

            store.SetCaughtUp(novel.Id, false);
            Assert.False(store.GetNovel(novel.Id)!.CaughtUp);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Upgrade_does_not_trust_the_site_chapter_count_for_caught_up()
    {
        var dir = Directory.CreateTempSubdirectory("novelshelf-sync-").FullName;
        var db = Path.Combine(dir, "library.db");
        try
        {
            var store = new LibraryStore(db);
            NovelDetails Details(string slug) => new("fake", slug, new Uri($"https://fake.test/{slug}"), slug, null, null, null, [], null, 2, null);
            var complete = store.Upsert(Details("completa"));
            var partial = store.Upsert(Details("parcial"));
            for (var i = 0; i < 2; i++)
                store.AddChapter(complete.Id, i, new ChapterPage(new Uri($"https://fake.test/c/{i}"), $"Cap {i}", "<p>x</p>", null));
            store.AddChapter(partial.Id, 0, new ChapterPage(new Uri("https://fake.test/p/0"), "Cap 0", "<p>x</p>", null));

            using (var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db};Pooling=False"))
            {
                raw.Open();
                using var cmd = raw.CreateCommand();
                cmd.CommandText = "ALTER TABLE novels DROP COLUMN caught_up; PRAGMA user_version = 4;";
                cmd.ExecuteNonQuery();
            }

            var upgraded = new LibraryStore(db);
            Assert.False(upgraded.GetNovel(complete.Id)!.CaughtUp);
            Assert.False(upgraded.GetNovel(partial.Id)!.CaughtUp);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }
}
