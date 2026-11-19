using System.Text;
using NovelShelf.Comics;
using NovelShelf.Core.Library;
using NovelShelf.Core.Net;
using NovelShelf.Ebook;
using NovelShelf.Ebook.Epub;
using NovelShelf.Ebook.Mobi;

namespace NovelShelf.Core.Books;

public sealed record ExportedBook(
    string? EpubPath,
    string Azw3Path,
    string KindleFileName,
    string ThumbnailFileName,
    byte[]? CoverJpeg,
    int ChapterCount,
    int Volume = 0);

public sealed record VolumeExport(IReadOnlyList<ExportedBook> Books, IReadOnlyList<VolumeSpec> Plan, int TotalChapters);

public sealed class BookExporter(LibraryStore store, AppPaths paths, PoliteHttpClient http)
{
    public async Task<ExportedBook> ExportAsync(long novelId, BookStyle? defaultStyle = null, CancellationToken ct = default)
    {
        var (novel, chapters) = Load(novelId);
        var whole = new VolumeSpec(0, 0, chapters.Count);
        return await BuildAsync(novel, chapters, whole, await GetCoverAsync(novel, ct), Style(novelId, defaultStyle));
    }

    public async Task<VolumeExport> ExportVolumesAsync(long novelId, BookStyle? defaultStyle = null,
        Func<IReadOnlyList<VolumeSpec>, IEnumerable<VolumeSpec>>? select = null, CancellationToken ct = default)
    {
        var (novel, chapters) = Load(novelId);
        var plan = Volumes.Split(chapters.Count, novel.VolumeSize);
        var cover = await GetCoverAsync(novel, ct);
        var style = Style(novelId, defaultStyle);
        var books = new List<ExportedBook>();
        foreach (var volume in select?.Invoke(plan) ?? plan)
        {
            ct.ThrowIfCancellationRequested();
            books.Add(await BuildAsync(novel, chapters, volume, cover, style));
        }
        return new VolumeExport(books, plan, chapters.Count);
    }

    private (LibraryNovel Novel, IReadOnlyList<StoredChapter> Chapters) Load(long novelId)
    {
        var novel = store.GetNovel(novelId) ?? throw new KeyNotFoundException($"Novel {novelId} não está na biblioteca.");
        var chapters = store.GetChapters(novelId);
        if (chapters.Count == 0)
            throw new InvalidOperationException($"'{novel.Title}' ainda não tem capítulos baixados.");
        return (novel, chapters);
    }

    private BookStyle Style(long novelId, BookStyle? defaultStyle) =>
        store.GetBookStyle(novelId) ?? defaultStyle ?? BookStyle.Default;

    private Task<ExportedBook> BuildAsync(LibraryNovel novel, IReadOnlyList<StoredChapter> all, VolumeSpec volume, byte[]? cover, BookStyle style)
    {
        var chapters = all.Skip(volume.First).Take(volume.Count).ToList();
        var key = Volumes.BookKey(novel.BookId, volume);
        var content = new BookContent(
            Identifier: Volumes.Identifier(key),
            Title: Volumes.Title(novel.Title, volume),
            Author: novel.Author,
            Language: "pt-BR",
            Description: novel.Synopsis,
            Modified: chapters.Max(c => c.DownloadedAt),
            CoverJpeg: cover,
            Chapters: chapters.Select((c, i) => new BookChapter(style.ChapterTitle(volume.First + i + 1, c.Title), c.Html)).ToList(),
            Style: style);

        var fileName = Volumes.FileName(FileNameFor(novel), volume);
        var azw3Path = Path.Combine(paths.Books, fileName);
        var epubPath = Path.ChangeExtension(azw3Path, ".epub");

        WriteAtomically(epubPath, s => EpubWriter.Write(content, s));
        var mobi = ToMobi(content, UniqueIdFor(key));
        WriteAtomically(azw3Path, mobi.Write);

        return Task.FromResult(new ExportedBook(epubPath, azw3Path, fileName, mobi.ThumbnailFileName, cover, chapters.Count, volume.Number));
    }

    public static MobiBook ToMobi(BookContent content, uint uniqueId) => new()
    {
        Title = content.Title,
        Authors = content.Author is null ? [] : [content.Author],
        Description = content.Description,
        Language = content.Language,
        CreatedDate = content.Modified,
        UniqueId = uniqueId,
        CoverImage = content.CoverJpeg,
        CssFlows = [content.EffectiveStyle.ToCss()],
        Chapters = content.Chapters.Select(c => new MobiChapter(c.Title, content.ChapterBody(c))).ToList(),
    };

    public static uint UniqueIdFor(string bookId)
    {
        const uint offset = 2166136261, prime = 16777619;
        var hash = offset;
        foreach (var b in Encoding.UTF8.GetBytes(bookId))
            hash = (hash ^ b) * prime;
        return hash;
    }

    public static string FileNameFor(LibraryNovel novel) =>
        novel.Kind == WorkKind.Comic ? $"hq-{novel.Slug}" : $"{novel.SourceId}-{novel.Slug}";

    private async Task<byte[]?> GetCoverAsync(LibraryNovel novel, CancellationToken ct)
    {
        var cached = Path.Combine(paths.Covers, $"{novel.Id}.jpg");
        if (File.Exists(cached))
            return await File.ReadAllBytesAsync(cached, ct);
        if (novel.CoverUrl is null)
            return null;

        try
        {
            var bytes = CoverImage.ToJpeg(await http.GetBytesAsync(novel.CoverUrl, ct));
            if (bytes is null)
                return null;
            await File.WriteAllBytesAsync(cached, bytes, ct);
            return bytes;
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    private static void WriteAtomically(string path, Action<Stream> write)
    {
        var temp = path + ".tmp";
        using (var stream = File.Create(temp))
            write(stream);
        File.Move(temp, path, overwrite: true);
    }
}
