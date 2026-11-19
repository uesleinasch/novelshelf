using System.Security.Cryptography;
using System.Text;
using NovelShelf.Core.Downloading;
using NovelShelf.Core.Library;

namespace NovelShelf.Core.Comics;

public sealed class ComicDownloader(LibraryStore store, IComicProvider provider, AppPaths paths)
{
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
            var pages = await DownloadPagesAsync(Path.Combine(ChapterRoot(novelId), folder), chapter, ct);
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

    private async Task<int> DownloadPagesAsync(string folder, ComicChapterInfo chapter, CancellationToken ct)
    {
        var urls = await provider.GetPageUrlsAsync(chapter, ct);
        if (urls.Count == 0)
            throw new InvalidOperationException($"O capítulo '{chapter.Name}' não tem páginas.");

        var temp = folder + ".part";
        if (Directory.Exists(temp))
            Directory.Delete(temp, recursive: true);
        Directory.CreateDirectory(temp);

        for (var i = 0; i < urls.Count; i++)
        {
            var bytes = await provider.GetBytesAsync(urls[i], ct);
            await File.WriteAllBytesAsync(Path.Combine(temp, $"{i:D4}{Extension(bytes)}"), bytes, ct);
        }

        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
        Directory.Move(temp, folder);
        return urls.Count;
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
