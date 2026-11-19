using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using NovelShelf.Core.Downloading;
using NovelShelf.Core.Library;
using NovelShelf.Core.Net;

namespace NovelShelf.Core.Comics;

public sealed class ComicDownloader(
    LibraryStore store, IComicProvider provider, AppPaths paths,
    TimeProvider? time = null, Func<TimeSpan, CancellationToken, Task>? sleep = null)
{
    public static readonly TimeSpan BasePageInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan MaxPageInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan[] RetryWaits =
        [TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)];
    private const int RefreshUrlsAfter = 2;
    private const int SuccessesToSpeedUp = 20;

    private sealed class Pace
    {
        public TimeSpan Interval = BasePageInterval;
        public int Successes;
        public DateTimeOffset Last = DateTimeOffset.MinValue;
    }

    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly Func<TimeSpan, CancellationToken, Task> _sleep = sleep ?? ((delay, ct) => Task.Delay(delay, time ?? TimeProvider.System, ct));
    private readonly ConcurrentDictionary<string, Pace> _paces = new();

    public TimeSpan PageIntervalFor(string sourceId) => _paces.TryGetValue(sourceId, out var pace) ? pace.Interval : BasePageInterval;

    public async Task<DownloadResult> DownloadNextAsync(
        long novelId, int count, IProgress<DownloadProgress>? progress = null, CancellationToken ct = default)
    {
        var comic = store.GetNovel(novelId) ?? throw new KeyNotFoundException($"Obra {novelId} não está na biblioteca.");
        if (comic.Kind != WorkKind.Comic || comic.RemoteKey is null)
            throw new InvalidOperationException($"'{comic.Title}' não é um quadrinho.");

        var remote = await provider.GetChaptersAsync(comic.SourceId, comic.RemoteKey, ct);
        var stored = store.GetComicChapters(novelId).Select(c => c.Key).ToHashSet();
        var pending = remote
            .OrderBy(c => c.Order)
            .ThenBy(c => c.Number)
            .Where(c => !stored.Contains(c.Key))
            .ToList();
        var batch = pending.Take(count).ToList();

        var done = 0;
        foreach (var chapter in batch)
        {
            ct.ThrowIfCancellationRequested();
            progress?.Report(new DownloadProgress(done, batch.Count, chapter.Name));
            var folder = FolderFor(chapter.Key);
            var report = (string detail) => progress?.Report(new DownloadProgress(done, batch.Count, $"{chapter.Name} — {detail}"));
            var pages = await DownloadPagesAsync(Path.Combine(ChapterRoot(novelId), folder), comic.SourceId, chapter, report, ct);
            store.AddComicChapter(novelId, chapter, pages, folder);
            done++;
            progress?.Report(new DownloadProgress(done, batch.Count, chapter.Name));
        }

        return new DownloadResult(done, ReachedEnd: pending.Count <= batch.Count);
    }

    public string ChapterRoot(long novelId) => Path.Combine(paths.Comics, novelId.ToString(System.Globalization.CultureInfo.InvariantCulture));

    public IReadOnlyList<string> PageFiles(long novelId, StoredComicChapter chapter)
    {
        var folder = Path.Combine(ChapterRoot(novelId), chapter.Folder);
        return Directory.Exists(folder) ? Directory.GetFiles(folder).Order(StringComparer.Ordinal).ToList() : [];
    }

    public static string FolderFor(string chapterKey) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(chapterKey)))[..16];

    private async Task<int> DownloadPagesAsync(string folder, string sourceId, ComicChapterInfo chapter,
        Action<string> report, CancellationToken ct)
    {
        var urls = await provider.GetPageUrlsAsync(chapter, ct);
        if (urls.Count == 0)
            throw new InvalidOperationException($"O capítulo '{chapter.Name}' não tem páginas.");

        var temp = folder + ".part";
        var countFile = Path.Combine(temp, "count");
        if (Directory.Exists(temp) && (!File.Exists(countFile) || await File.ReadAllTextAsync(countFile, ct) != urls.Count.ToString(System.Globalization.CultureInfo.InvariantCulture)))
            Directory.Delete(temp, recursive: true);
        Directory.CreateDirectory(temp);
        await File.WriteAllTextAsync(countFile, urls.Count.ToString(System.Globalization.CultureInfo.InvariantCulture), ct);

        var pace = _paces.GetOrAdd(sourceId, _ => new Pace());
        for (var i = 0; i < urls.Count; i++)
        {
            if (Directory.EnumerateFiles(temp, $"{i:D4}.*").Any(f => !f.EndsWith(".tmp", StringComparison.Ordinal)))
                continue;
            var bytes = await FetchPageAsync(pace, chapter, i, () => urls, async () => urls = await provider.GetPageUrlsAsync(chapter, ct), report, ct);
            var target = Path.Combine(temp, $"{i:D4}{Extension(bytes)}");
            await File.WriteAllBytesAsync(target + ".tmp", bytes, ct);
            File.Move(target + ".tmp", target, overwrite: true);
        }

        File.Delete(countFile);
        foreach (var leftover in Directory.EnumerateFiles(temp, "*.tmp"))
            File.Delete(leftover);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.Move(temp, folder);
        return urls.Count;
    }

    private async Task<byte[]> FetchPageAsync(Pace pace, ComicChapterInfo chapter, int index,
        Func<IReadOnlyList<Uri>> urls, Func<Task> refreshUrls, Action<string> report, CancellationToken ct)
    {
        for (var failures = 0; ; failures++)
        {
            await WaitTurnAsync(pace, ct);
            try
            {
                var current = urls();
                if (index >= current.Count)
                    throw new InvalidOperationException($"O capítulo '{chapter.Name}' mudou de número de páginas. Baixe de novo.");
                var bytes = await provider.GetBytesAsync(current[index], ct);
                Succeeded(pace);
                return bytes;
            }
            catch (Exception e) when (IsTransient(e, ct))
            {
                Slower(pace);
                if (failures == RetryWaits.Length)
                {
                    var retryAt = _time.GetUtcNow() + PoliteHttpClient.GiveUpPause;
                    throw new RateLimitedException(
                        $"A fonte não entregou a página {index + 1} de '{chapter.Name}'. Tento de novo às {retryAt.ToLocalTime():HH:mm}.", retryAt);
                }
                var wait = RetryWaits[failures];
                report($"fonte pediu pausa na página {index + 1}, esperando {(wait.TotalMinutes >= 1 ? $"{wait.TotalMinutes:0} min" : $"{wait.TotalSeconds:0} s")}");
                await _sleep(wait, ct);
                if (failures + 1 == RefreshUrlsAfter)
                    await refreshUrls();
            }
        }
    }

    private static bool IsTransient(Exception e, CancellationToken ct) => e switch
    {
        HttpRequestException { StatusCode: null } => true,
        HttpRequestException { StatusCode: { } code } => code == HttpStatusCode.TooManyRequests || (int)code >= 500,
        TaskCanceledException => !ct.IsCancellationRequested,
        IOException => true,
        _ => false,
    };

    private async Task WaitTurnAsync(Pace pace, CancellationToken ct)
    {
        TimeSpan wait;
        lock (pace)
            wait = pace.Last + pace.Interval - _time.GetUtcNow();
        if (wait > TimeSpan.Zero)
            await _sleep(wait, ct);
        lock (pace)
            pace.Last = _time.GetUtcNow();
    }

    private static void Slower(Pace pace)
    {
        lock (pace)
        {
            pace.Successes = 0;
            var slower = pace.Interval * 2;
            pace.Interval = slower > MaxPageInterval ? MaxPageInterval : slower;
        }
    }

    private static void Succeeded(Pace pace)
    {
        lock (pace)
        {
            if (pace.Interval <= BasePageInterval || ++pace.Successes < SuccessesToSpeedUp)
                return;
            pace.Successes = 0;
            var faster = pace.Interval * 0.75;
            pace.Interval = faster < BasePageInterval ? BasePageInterval : faster;
        }
    }

    private static string Extension(byte[] data) => data switch
    {
        [0xFF, 0xD8, ..] => ".jpg",
        [0x89, 0x50, 0x4E, 0x47, ..] => ".png",
        [0x52, 0x49, 0x46, 0x46, _, _, _, _, 0x57, 0x45, 0x42, 0x50, ..] => ".webp",
        [0x47, 0x49, 0x46, ..] => ".gif",
        _ => ".img",
    };
}
