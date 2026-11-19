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
        var comic = store.GetNovel(novelId) ?? throw new KeyNotFoundException($"Obra {novelId} não está na biblioteca.");
        var stored = store.GetComicChapters(novelId);
        if (stored.Count == 0)
            throw new InvalidOperationException($"'{comic.Title}' ainda não tem capítulos baixados.");

        var options = new ComicPageOptions(profile, comic.RightToLeft);
        var chapters = new List<ComicChapter>(stored.Count);
        for (var i = 0; i < stored.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var chapter = stored[i];
            progress?.Report(new ComicExportProgress(i, stored.Count, chapter.Title));
            var files = downloader.PageFiles(novelId, chapter);
            var pages = await Task.WhenAll(files.Select(f => File.ReadAllBytesAsync(f, ct)));
            var processed = await Task.Run(() => ComicPageProcessor.ProcessChapter(pages, options), ct);
            chapters.Add(new ComicChapter(chapter.Title, processed));
        }
        progress?.Report(new ComicExportProgress(stored.Count, stored.Count, ""));

        var cover = await CoverAsync(novelId, options, ct);
        var info = new ComicBookInfo(
            comic.Title, comic.Author, comic.Synopsis, "pt-BR",
            stored.Max(c => c.DownloadedAt), BookExporter.UniqueIdFor(comic.BookId), cover);
        var book = ComicBookBuilder.Build(info, chapters, profile, comic.RightToLeft);

        var fileName = BookExporter.FileNameFor(comic) + ".azw3";
        var path = Path.Combine(paths.Books, fileName);
        var temp = path + ".tmp";
        await using (var stream = File.Create(temp))
            book.Write(stream);
        File.Move(temp, path, overwrite: true);

        return new ExportedBook(null, path, fileName, book.ThumbnailFileName, book.CoverImage, stored.Count);
    }

    private async Task<byte[]?> CoverAsync(long novelId, ComicPageOptions options, CancellationToken ct)
    {
        var file = Path.Combine(paths.Covers, $"{novelId}.jpg");
        if (!File.Exists(file))
            return null;
        var bytes = await File.ReadAllBytesAsync(file, ct);
        return ComicPageProcessor.ProcessPage(bytes, options with { AutoCrop = false, SplitWidePages = false })
            .FirstOrDefault();
    }
}
