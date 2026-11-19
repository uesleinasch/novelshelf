using System.Collections.Concurrent;
using NovelShelf.Core.Downloading;
using NovelShelf.Core.Library;
using NovelShelf.Core.Sources;

namespace NovelShelf.Tests.Downloading;

public sealed class DownloadQueueTests
{
    private sealed class FakeWork
    {
        private readonly ConcurrentDictionary<long, SemaphoreSlim> _steps = new();

        public ConcurrentQueue<(long Novel, int Count)> Calls { get; } = new();
        public ConcurrentDictionary<long, int> Available { get; } = new();
        public ConcurrentDictionary<long, string> FailWith { get; } = new();

        public SemaphoreSlim Step(long novel) => _steps.GetOrAdd(novel, _ => new SemaphoreSlim(0));

        public void Release(long novel, int chapters = 1) => Step(novel).Release(chapters);

        public async Task<DownloadResult> RunAsync(DownloadJob job, int count, IProgress<DownloadProgress> progress, CancellationToken ct)
        {
            Calls.Enqueue((job.NovelId, count));
            if (FailWith.TryRemove(job.NovelId, out var message))
                throw new HttpRequestException(message);
            var available = Available.GetValueOrDefault(job.NovelId, int.MaxValue);
            var done = 0;
            while (done < count && done < available)
            {
                await Step(job.NovelId).WaitAsync(ct);
                done++;
                progress.Report(new DownloadProgress(done, count, $"Cap {done}"));
            }
            return new DownloadResult(done, ReachedEnd: done < count);
        }
    }

    private readonly FakeWork _work = new();
    private readonly DownloadQueue _queue;

    public DownloadQueueTests() => _queue = new DownloadQueue(_work.RunAsync);

    private DownloadJob Job(long id) => _queue.Find(id)!;

    private static async Task Until(Func<bool> condition)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!condition())
            await Task.Delay(10, timeout.Token);
    }

    [Fact]
    public async Task Same_site_runs_one_at_a_time_while_other_sites_run_in_parallel()
    {
        _queue.Enqueue(1, "A", "site-x", 1);
        _queue.Enqueue(2, "B", "site-x", 1);
        _queue.Enqueue(3, "C", "site-y", 1);

        await Until(() => Job(1).State == DownloadState.Running && Job(3).State == DownloadState.Running);
        Assert.Equal(DownloadState.Queued, Job(2).State);

        _work.Release(1);
        await Until(() => Job(2).State == DownloadState.Running);
        Assert.Equal(DownloadState.Done, Job(1).State);

        _work.Release(2);
        _work.Release(3);
        await _queue.WhenIdleAsync();
        Assert.All(_queue.Jobs, j => Assert.Equal(DownloadState.Done, j.State));
    }

    [Fact]
    public async Task Pause_keeps_progress_and_resume_asks_only_for_the_rest()
    {
        _queue.Enqueue(1, "A", "x", 5);
        _work.Release(1, 2);
        await Until(() => Job(1).Done == 2);

        _queue.Pause(1);
        await Until(() => Job(1).State == DownloadState.Paused);
        Assert.Equal(2, Job(1).Done);

        _queue.Resume(1);
        _work.Release(1, 3);
        await Until(() => Job(1).State == DownloadState.Done);
        await _queue.WhenIdleAsync();

        Assert.Equal(5, Job(1).Done);
        Assert.Equal([(1L, 5), (1L, 3)], _work.Calls);
    }

    [Fact]
    public async Task Paused_job_does_not_block_its_site()
    {
        _queue.Enqueue(1, "A", "x", 3);
        _queue.Enqueue(2, "B", "x", 1);
        await Until(() => Job(1).State == DownloadState.Running);

        _queue.Pause(1);
        await Until(() => Job(2).State == DownloadState.Running);
        _work.Release(2);
        await _queue.WhenIdleAsync();

        Assert.Equal(DownloadState.Paused, Job(1).State);
        Assert.Equal(DownloadState.Done, Job(2).State);
    }

    [Fact]
    public async Task Failure_is_reported_and_retry_runs_again()
    {
        _work.FailWith[1] = "site fora do ar";
        _queue.Enqueue(1, "A", "x", 1);
        await Until(() => Job(1).State == DownloadState.Failed);
        Assert.Equal("site fora do ar", Job(1).Detail);

        _queue.Resume(1);
        _work.Release(1);
        await Until(() => Job(1).State == DownloadState.Done);
    }

    [Fact]
    public async Task Reaching_the_last_chapter_finishes_early()
    {
        _work.Available[1] = 2;
        _work.Release(1, 2);
        _queue.Enqueue(1, "A", "x", 10);

        await Until(() => Job(1).State == DownloadState.Done);

        Assert.True(Job(1).ReachedEnd);
        Assert.Equal(2, Job(1).Done);
    }

    [Fact]
    public async Task Enqueue_again_extends_a_pending_job()
    {
        _queue.Enqueue(1, "A", "x", 2);
        _queue.Enqueue(1, "A", "x", 5);
        Assert.Single(_queue.Jobs);
        Assert.Equal(5, Job(1).Requested);

        _work.Release(1, 5);
        await Until(() => Job(1).State == DownloadState.Done);
        Assert.Equal(5, Job(1).Done);
    }

    [Fact]
    public async Task Move_changes_which_queued_job_starts_next()
    {
        _queue.Enqueue(1, "A", "x", 1);
        _queue.Enqueue(2, "B", "x", 1);
        _queue.Enqueue(3, "C", "x", 1);
        _queue.Move(3, -1);

        _work.Release(1);
        await Until(() => Job(3).State == DownloadState.Running);
        Assert.Equal(DownloadState.Queued, Job(2).State);
        Assert.Equal([1L, 3, 2], _queue.Jobs.Select(j => j.NovelId));

        _work.Release(3);
        _work.Release(2);
        await _queue.WhenIdleAsync();
    }

    [Fact]
    public async Task Remove_cancels_the_running_job_and_frees_the_site()
    {
        _queue.Enqueue(1, "A", "x", 3);
        _queue.Enqueue(2, "B", "x", 1);
        await Until(() => Job(1).State == DownloadState.Running);

        _queue.Remove(1);
        await Until(() => _queue.Find(2)?.State == DownloadState.Running);
        Assert.Null(_queue.Find(1));

        _work.Release(2);
        await _queue.WhenIdleAsync();
        _queue.ClearFinished();
        Assert.Empty(_queue.Jobs);
    }

    [Fact]
    public async Task Restore_brings_back_pending_jobs_with_their_progress()
    {
        _queue.Restore(new DownloadJob(1, "A", "x", 5, Done: 3, State: DownloadState.Running));
        _queue.Restore(new DownloadJob(2, "B", "y", 4, Done: 1, State: DownloadState.Paused));
        _queue.Restore(new DownloadJob(3, "C", "z", 2, Done: 2));

        _work.Release(1, 2);
        await Until(() => Job(1).State == DownloadState.Done);

        Assert.Equal(5, Job(1).Done);
        Assert.Equal((1L, 2), _work.Calls.Single());
        Assert.Equal(DownloadState.Paused, Job(2).State);
        Assert.Null(_queue.Find(3));
    }

    [Fact]
    public void Store_saves_and_loads_the_pending_queue_in_order()
    {
        var dir = Directory.CreateTempSubdirectory("novelshelf-queue-").FullName;
        try
        {
            var store = new LibraryStore(Path.Combine(dir, "library.db"));
            LibraryNovel Novel(string slug) => store.Upsert(new NovelDetails("fake", slug, new Uri($"https://fake.test/{slug}"),
                slug, null, null, null, [], null, 3, null));
            var a = Novel("a");
            var b = Novel("b");

            store.SaveDownloadQueue([new QueuedDownload(b.Id, 10, 4, true), new QueuedDownload(a.Id, 3, 0, false), new QueuedDownload(999, 1, 0, false)]);
            Assert.Equal([new QueuedDownload(b.Id, 10, 4, true), new QueuedDownload(a.Id, 3, 0, false)], store.LoadDownloadQueue());

            store.DeleteNovel(b.Id);
            Assert.Equal([new QueuedDownload(a.Id, 3, 0, false)], store.LoadDownloadQueue());
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public async Task Rate_limited_job_waits_and_holds_its_site_until_retry_time()
    {
        var calls = 0;
        var queue = new DownloadQueue((job, count, progress, ct) =>
        {
            if (job.NovelId == 1 && Interlocked.Increment(ref calls) == 1)
                throw new Core.Net.RateLimitedException("pausa", DateTimeOffset.UtcNow.AddMilliseconds(400));
            return Task.FromResult(new DownloadResult(count, ReachedEnd: true));
        });
        queue.Enqueue(1, "A", "x", 1);
        queue.Enqueue(2, "B", "x", 1);
        queue.Enqueue(3, "C", "y", 1);

        await Until(() => queue.Find(3)!.State == DownloadState.Done && queue.Find(1)!.RetryAt is not null);
        Assert.Equal(DownloadState.Queued, queue.Find(1)!.State);
        Assert.Equal(DownloadState.Queued, queue.Find(2)!.State);
        Assert.Equal("pausa", queue.Find(1)!.Detail);

        await Until(() => queue.Find(1)!.State == DownloadState.Done && queue.Find(2)!.State == DownloadState.Done);
        Assert.Null(queue.Find(1)!.RetryAt);
    }
}
