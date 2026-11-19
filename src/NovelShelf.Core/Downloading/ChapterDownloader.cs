using NovelShelf.Core.Library;
using NovelShelf.Core.Sources;

namespace NovelShelf.Core.Downloading;

public sealed record DownloadProgress(int Done, int Requested, string ChapterTitle);

public sealed record DownloadResult(int Downloaded, bool ReachedEnd);

public sealed class ChapterDownloader(LibraryStore store, SourceRegistry sources)
{
    public async Task<DownloadResult> DownloadNextAsync(
        long novelId,
        int count,
        IProgress<DownloadProgress>? progress = null,
        CancellationToken ct = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(count);

        var novel = store.GetNovel(novelId) ?? throw new KeyNotFoundException($"Novel {novelId} não está na biblioteca.");
        var source = sources.Get(novel.SourceId);
        var last = store.GetLastChapter(novelId);

        var nextIndex = last is null ? 0 : last.Index + 1;
        var nextUrl = last is null ? novel.FirstChapterUrl : await ResolveNextAfterAsync(source, last, ct);

        var downloaded = 0;
        while (downloaded < count && nextUrl is not null)
        {
            var page = await source.GetChapterAsync(nextUrl, ct);
            store.AddChapter(novelId, nextIndex, page);
            downloaded++;
            nextIndex++;
            progress?.Report(new DownloadProgress(downloaded, count, page.Title));
            nextUrl = page.NextUrl;
        }

        return new DownloadResult(downloaded, ReachedEnd: nextUrl is null);
    }

    private async Task<Uri?> ResolveNextAfterAsync(INovelSource source, StoredChapter last, CancellationToken ct)
    {
        if (last.NextUrl is not null)
            return last.NextUrl;

        var refreshed = await source.GetChapterAsync(last.Url, ct);
        if (refreshed.NextUrl is not null)
            store.SetNextUrl(last.NovelId, last.Index, refreshed.NextUrl);
        return refreshed.NextUrl;
    }
}
