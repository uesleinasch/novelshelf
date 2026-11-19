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
    int ChapterCount);

public sealed class BookExporter(LibraryStore store, AppPaths paths, PoliteHttpClient http)
{
    private const string KindleCss = """
        h1 { font-size: 1.4em; text-align: center; margin: 1.5em 0 1em; }
        p { text-indent: 1.2em; margin: 0 0 0.6em; text-align: justify; }
        """;

    public async Task<ExportedBook> ExportAsync(long novelId, CancellationToken ct = default)
    {
        var novel = store.GetNovel(novelId) ?? throw new KeyNotFoundException($"Novel {novelId} não está na biblioteca.");
        var chapters = store.GetChapters(novelId);
        if (chapters.Count == 0)
            throw new InvalidOperationException($"'{novel.Title}' ainda não tem capítulos baixados.");

        var cover = await GetCoverAsync(novel, ct);
        var content = new BookContent(
            Identifier: $"urn:uuid:{Guid.ParseExact(novel.BookId, "N")}",
            Title: novel.Title,
            Author: novel.Author,
            Language: "pt-BR",
            Description: novel.Synopsis,
            Modified: chapters.Max(c => c.DownloadedAt),
            CoverJpeg: cover,
            Chapters: chapters.Select(c => new BookChapter(c.Title, c.Html)).ToList());

        var baseName = FileNameFor(novel);
        var epubPath = Path.Combine(paths.Books, baseName + ".epub");
        var azw3Path = Path.Combine(paths.Books, baseName + ".azw3");

        WriteAtomically(epubPath, s => EpubWriter.Write(content, s));
        var mobi = ToMobi(content, UniqueIdFor(novel.BookId));
        WriteAtomically(azw3Path, mobi.Write);

        return new ExportedBook(epubPath, azw3Path, baseName + ".azw3", mobi.ThumbnailFileName, cover, chapters.Count);
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
        CssFlows = [KindleCss],
        Chapters = content.Chapters
            .Select(c => new MobiChapter(c.Title, $"<h1>{System.Security.SecurityElement.Escape(c.Title)}</h1>\n{c.XhtmlBody}"))
            .ToList(),
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
