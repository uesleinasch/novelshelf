using NovelShelf.Core.Net;

namespace NovelShelf.Core.Downloading;

public enum DownloadState
{
    Queued,
    Running,
    Paused,
    Done,
    Failed,
}

public sealed record DownloadJob(
    long NovelId,
    string Title,
    string Lane,
    int Requested,
    int Done = 0,
    DownloadState State = DownloadState.Queued,
    string? Detail = null,
    bool ReachedEnd = false,
    DateTimeOffset? RetryAt = null)
{
    public int Remaining => Math.Max(0, Requested - Done);
    public bool IsPending => State is DownloadState.Queued or DownloadState.Running or DownloadState.Paused;
}

public delegate Task<DownloadResult> DownloadWork(
    DownloadJob job, int count, IProgress<DownloadProgress> progress, CancellationToken ct);

public sealed class DownloadQueue(DownloadWork work, TimeProvider? time = null)
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Dictionary<string, DateTimeOffset> _laneCooldown = [];

    private sealed class Entry(DownloadJob job)
    {
        public DownloadJob Job = job;
        public CancellationTokenSource? Cancel;
        public Task? Run;
        public bool PauseRequested;
    }

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        public void Report(T value) => report(value);
    }

    private readonly Lock _gate = new();
    private readonly List<Entry> _entries = [];

    public event Action? Changed;

    public IReadOnlyList<DownloadJob> Jobs
    {
        get
        {
            lock (_gate)
                return _entries.Select(e => e.Job).ToList();
        }
    }

    public DownloadJob? Find(long novelId)
    {
        lock (_gate)
            return Get(novelId)?.Job;
    }

    public void Enqueue(long novelId, string title, string lane, int count, bool paused = false)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);
        lock (_gate)
        {
            if (Get(novelId) is { Job.IsPending: true } existing)
            {
                existing.Job = existing.Job with
                {
                    Requested = Math.Max(existing.Job.Requested, existing.Job.Done + count),
                    State = existing.Job.State == DownloadState.Paused && !paused ? DownloadState.Queued : existing.Job.State,
                    Detail = null,
                };
            }
            else
            {
                _entries.RemoveAll(e => e.Job.NovelId == novelId);
                _entries.Add(new Entry(new DownloadJob(novelId, title, lane, count,
                    State: paused ? DownloadState.Paused : DownloadState.Queued)));
            }
        }
        Pump();
    }

    public void Restore(DownloadJob job)
    {
        lock (_gate)
        {
            if (Get(job.NovelId) is not null || job.Remaining == 0)
                return;
            var state = job.State == DownloadState.Paused ? DownloadState.Paused : DownloadState.Queued;
            _entries.Add(new Entry(job with { State = state, Detail = null }));
        }
        Pump();
    }

    public void Pause(long novelId)
    {
        lock (_gate)
            PauseLocked(Get(novelId));
        Notify();
    }

    public void PauseAll()
    {
        lock (_gate)
            foreach (var entry in _entries)
                PauseLocked(entry);
        Notify();
    }

    public void Resume(long novelId)
    {
        lock (_gate)
            ResumeLocked(Get(novelId));
        Pump();
    }

    public void ResumeAll()
    {
        lock (_gate)
            foreach (var entry in _entries)
                if (entry.Job.State == DownloadState.Paused)
                    ResumeLocked(entry);
        Pump();
    }

    public void Remove(long novelId)
    {
        lock (_gate)
        {
            if (Get(novelId) is not { } entry)
                return;
            entry.Cancel?.Cancel();
            _entries.Remove(entry);
        }
        Pump();
    }

    public void ClearFinished()
    {
        lock (_gate)
            _entries.RemoveAll(e => !e.Job.IsPending);
        Notify();
    }

    public void Move(long novelId, int delta)
    {
        lock (_gate)
        {
            if (Get(novelId) is not { } entry)
                return;
            var from = _entries.IndexOf(entry);
            var to = Math.Clamp(from + delta, 0, _entries.Count - 1);
            if (from == to)
                return;
            _entries.RemoveAt(from);
            _entries.Insert(to, entry);
        }
        Pump();
    }

    public async Task WhenIdleAsync()
    {
        while (true)
        {
            Task[] running;
            lock (_gate)
                running = _entries.Where(e => e.Job.State == DownloadState.Running).Select(e => e.Run).OfType<Task>().ToArray();
            if (running.Length == 0)
                return;
            await Task.WhenAll(running);
        }
    }

    public void Annotate(string lane, string? detail)
    {
        lock (_gate)
        {
            var running = _entries.FirstOrDefault(e => e.Job.Lane == lane && e.Job.State == DownloadState.Running);
            if (running is null)
                return;
            running.Job = running.Job with { Detail = detail };
        }
        Notify();
    }

    private Entry? Get(long novelId) => _entries.FirstOrDefault(e => e.Job.NovelId == novelId);

    private static void PauseLocked(Entry? entry)
    {
        if (entry is null)
            return;
        if (entry.Job.State == DownloadState.Running)
        {
            entry.PauseRequested = true;
            entry.Cancel?.Cancel();
        }
        else if (entry.Job.State == DownloadState.Queued)
        {
            entry.Job = entry.Job with { State = DownloadState.Paused };
        }
    }

    private static void ResumeLocked(Entry? entry)
    {
        if (entry is null)
            return;
        if (entry.Job.State == DownloadState.Running && entry.PauseRequested)
            entry.PauseRequested = false;
        else if (entry.Job.State is DownloadState.Paused or DownloadState.Failed)
            entry.Job = entry.Job with { State = DownloadState.Queued, Detail = null };
    }

    private void Pump()
    {
        lock (_gate)
        {
            var now = _time.GetUtcNow();
            foreach (var expired in _laneCooldown.Where(c => c.Value <= now).Select(c => c.Key).ToList())
                _laneCooldown.Remove(expired);
            var busy = _entries.Where(e => e.Job.State == DownloadState.Running).Select(e => e.Job.Lane)
                .Concat(_laneCooldown.Keys).ToHashSet();
            foreach (var entry in _entries)
            {
                if (entry.Job.State != DownloadState.Queued || !busy.Add(entry.Job.Lane))
                    continue;
                if (entry.Job.Remaining == 0)
                {
                    entry.Job = entry.Job with { State = DownloadState.Done };
                    busy.Remove(entry.Job.Lane);
                    continue;
                }
                entry.Job = entry.Job with { State = DownloadState.Running, Detail = null, RetryAt = null };
                entry.PauseRequested = false;
                entry.Cancel = new CancellationTokenSource();
                entry.Run = RunAsync(entry, entry.Job, entry.Cancel.Token);
            }
        }
        Notify();
    }

    private async Task RunAsync(Entry entry, DownloadJob job, CancellationToken ct)
    {
        await Task.Yield();
        var before = job.Done;
        var progress = new InlineProgress<DownloadProgress>(p =>
        {
            lock (_gate)
            {
                if (!ReferenceEquals(Get(job.NovelId), entry))
                    return;
                entry.Job = entry.Job with { Done = before + p.Done, Detail = p.ChapterTitle };
            }
            Notify();
        });

        DownloadJob Finish(Func<DownloadJob, DownloadJob> update) => entry.Job = update(entry.Job);

        try
        {
            var result = await work(job, job.Remaining, progress, ct);
            lock (_gate)
                Finish(j =>
                {
                    var done = Math.Max(j.Done, before + result.Downloaded);
                    return result.ReachedEnd || done >= j.Requested || result.Downloaded == 0
                        ? j with { Done = done, State = DownloadState.Done, ReachedEnd = result.ReachedEnd, Detail = null }
                        : j with { Done = done, State = DownloadState.Queued };
                });
        }
        catch (RateLimitedException e)
        {
            lock (_gate)
            {
                Finish(j => j with { State = entry.PauseRequested ? DownloadState.Paused : DownloadState.Queued, Detail = e.Message, RetryAt = e.RetryAt });
                if (!_laneCooldown.TryGetValue(job.Lane, out var current) || current < e.RetryAt)
                    _laneCooldown[job.Lane] = e.RetryAt;
            }
            _ = WakeAtAsync(e.RetryAt);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            lock (_gate)
                Finish(j => j with { State = entry.PauseRequested ? DownloadState.Paused : DownloadState.Queued });
        }
        catch (Exception e)
        {
            lock (_gate)
                Finish(j => j with { State = DownloadState.Failed, Detail = e.Message });
        }
        finally
        {
            lock (_gate)
            {
                entry.Cancel?.Dispose();
                entry.Cancel = null;
                entry.PauseRequested = false;
            }
        }
        Pump();
    }

    private async Task WakeAtAsync(DateTimeOffset at)
    {
        for (var wait = at - _time.GetUtcNow(); wait > TimeSpan.Zero; wait = at - _time.GetUtcNow())
            await Task.Delay(wait + TimeSpan.FromMilliseconds(5), _time);
        Pump();
    }

    private void Notify() => Changed?.Invoke();
}
