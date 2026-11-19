using Avalonia.Threading;
using NovelShelf.Core.Downloading;
using NovelShelf.Core.Library;
using NovelShelf.Core.Net;

namespace NovelShelf.App.Services;

public sealed class DownloadService
{
    private readonly AppServices _services;
    private readonly Lock _postGate = new();
    private bool _updatePosted;

    public DownloadService(AppServices services)
    {
        _services = services;
        Queue = new DownloadQueue(RunAsync);
        Queue.Changed += OnQueueChanged;
        services.Http.RateLimited += OnRateLimited;
    }

    public DownloadQueue Queue { get; }

    public event Action? Updated;

    public static string LaneOf(LibraryNovel novel) =>
        novel.Kind == WorkKind.Comic ? $"comic:{novel.SourceId}" : novel.SourceId;

    public void Enqueue(LibraryNovel novel, int count) =>
        Queue.Enqueue(novel.Id, novel.Title, LaneOf(novel), Math.Max(1, count));

    public void RestorePending()
    {
        foreach (var saved in _services.Library.LoadDownloadQueue())
        {
            if (_services.Library.GetNovel(saved.NovelId) is not { } novel)
                continue;
            Queue.Restore(new DownloadJob(novel.Id, novel.Title, LaneOf(novel), saved.Requested, saved.Done,
                saved.Paused ? DownloadState.Paused : DownloadState.Queued));
        }
    }

    private async Task<DownloadResult> RunAsync(DownloadJob job, int count, IProgress<DownloadProgress> progress, CancellationToken ct)
    {
        var novel = _services.Library.GetNovel(job.NovelId)
            ?? throw new InvalidOperationException("A obra não está mais na biblioteca.");
        if (novel.Kind == WorkKind.Comic)
        {
            await _services.Manga.EnsureStartedAsync();
            _services.Library.UpsertComic(await _services.Manga.Client.GetComicAsync(novel.SourceId, novel.RemoteKey!, ct));
            return Track(novel.Id, await _services.ComicDownloader.DownloadNextAsync(novel.Id, count, progress, ct));
        }

        var details = await _services.Sources.Get(novel.SourceId).GetNovelAsync(novel.Url, ct);
        _services.Library.Upsert(details);
        return Track(novel.Id, await _services.Downloader.DownloadNextAsync(novel.Id, count, progress, ct));
    }

    private DownloadResult Track(long workId, DownloadResult result)
    {
        if (result.ReachedEnd || result.Downloaded > 0)
            _services.Library.SetCaughtUp(workId, result.ReachedEnd);
        return result;
    }

    private void OnRateLimited(RateLimitNotice notice)
    {
        var source = _services.Sources.All.FirstOrDefault(s => string.Equals(s.BaseUrl.Host, notice.Host, StringComparison.OrdinalIgnoreCase));
        if (source is not null)
            Queue.Annotate(source.Id, $"Site pediu pausa (muitas requisições) — esperando até {notice.Until.ToLocalTime():HH:mm:ss}");
    }

    private void OnQueueChanged()
    {
        lock (_postGate)
        {
            if (_updatePosted)
                return;
            _updatePosted = true;
        }
        Dispatcher.UIThread.Post(() =>
        {
            lock (_postGate)
                _updatePosted = false;
            Persist();
            Updated?.Invoke();
        });
    }

    private void Persist()
    {
        var pending = Queue.Jobs
            .Where(j => j.IsPending)
            .Select(j => new QueuedDownload(j.NovelId, j.Requested, j.Done, j.State == DownloadState.Paused))
            .ToList();
        _services.Library.SaveDownloadQueue(pending);
    }
}
