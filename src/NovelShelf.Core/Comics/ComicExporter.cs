using NovelShelf.Comics;
using NovelShelf.Core.Books;
using NovelShelf.Core.Library;

namespace NovelShelf.Core.Comics;

public sealed record ComicExportProgress(int Done, int Total, string ChapterTitle);

public sealed class ComicExporter(LibraryStore store, AppPaths paths, ComicDownloader downloader)
{
    public async Task<ExportedBook> ExportAsync(
        long novelId, KindleProfile profile, IProgress<ComicExportProgress>? progress = null, CancellationToken ct = default)
    {
        var (comic, stored) = Load(novelId);
        var export = await ExportAsync(comic, stored, [new VolumeSpec(0, 0, stored.Count)], profile, progress, ct);
        return export.Single();
    }

    public async Task<VolumeExport> ExportVolumesAsync(long novelId, KindleProfile profile,
        Func<IReadOnlyList<VolumeSpec>, IEnumerable<VolumeSpec>>? select = null,
        IProgress<ComicExportProgress>? progress = null, CancellationToken ct = default)
    {
        var (comic, stored) = Load(novelId);
        var plan = Volumes.Split(stored.Count, comic.VolumeSize);
        var books = await ExportAsync(comic, stored, (select?.Invoke(plan) ?? plan).ToList(), profile, progress, ct);
        return new VolumeExport(books, plan, stored.Count);
    }

    private (LibraryNovel Comic, IReadOnlyList<StoredComicChapter> Chapters) Load(long novelId)
    {
        var comic = store.GetNovel(novelId) ?? throw new KeyNotFoundException($"Obra {novelId} não está na biblioteca.");
        var stored = store.GetComicChapters(novelId);
        if (stored.Count == 0)
            throw new InvalidOperationException($"'{comic.Title}' ainda não tem capítulos baixados.");
        return (comic, stored);
    }

    private async Task<IReadOnlyList<ExportedBook>> ExportAsync(LibraryNovel comic, IReadOnlyList<StoredComicChapter> stored,
        IReadOnlyList<VolumeSpec> volumes, KindleProfile profile, IProgress<ComicExportProgress>? progress, CancellationToken ct)
    {
        var options = new ComicPageOptions(profile, comic.RightToLeft, store.GetComicLayout(comic.Id));
        var cover = Cover(comic.Id, options);
        var total = volumes.Sum(v => v.Count);
        var done = 0;
        var books = new List<ExportedBook>();
        foreach (var volume in volumes)
        {
            var chapters = new List<ComicChapter>(volume.Count);
            foreach (var chapter in stored.Skip(volume.First).Take(volume.Count))
            {
                ct.ThrowIfCancellationRequested();
                progress?.Report(new ComicExportProgress(done++, total, chapter.Title));
                var files = downloader.PageFiles(comic.Id, chapter);
                var pages = await Task.WhenAll(files.Select(f => File.ReadAllBytesAsync(f, ct)));
                var processed = await Task.Run(() => ComicPageProcessor.ProcessChapter(pages, options), ct);
                chapters.Add(new ComicChapter(chapter.Title, processed));
            }

            var key = Volumes.BookKey(comic.BookId, volume);
            var info = new ComicBookInfo(
                Volumes.Title(comic.Title, volume), comic.Author, comic.Synopsis, "pt-BR",
                stored.Skip(volume.First).Take(volume.Count).Max(c => c.DownloadedAt), BookExporter.UniqueIdFor(key),
                Volumes.CoverFor(cover, volume));
            var book = ComicBookBuilder.Build(info, chapters, profile, comic.RightToLeft);

            var fileName = Volumes.FileName(BookExporter.FileNameFor(comic), volume);
            var path = Path.Combine(paths.Books, fileName);
            var temp = path + ".tmp";
            await using (var stream = File.Create(temp))
                book.Write(stream);
            File.Move(temp, path, overwrite: true);
            books.Add(new ExportedBook(null, path, fileName, book.ThumbnailFileName, book.CoverImage, volume.Count, volume.Number));
        }
        progress?.Report(new ComicExportProgress(total, total, ""));
        return books;
    }

    private byte[]? Cover(long novelId, ComicPageOptions options) =>
        new CoverCache(paths).TryRead(novelId, null) is { } bytes ? ComicPageProcessor.ProcessCover(bytes, options.Profile) : null;
}
