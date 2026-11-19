using System.Globalization;
using System.IO.Compression;
using System.Security;
using System.Text;

namespace NovelShelf.Ebook.Epub;

public static class EpubWriter
{
    private const string Css = """
        body { font-family: serif; line-height: 1.5; margin: 0 0.5em; }
        h1 { font-size: 1.4em; text-align: center; margin: 1.5em 0 1em; }
        p { text-indent: 1.2em; margin: 0 0 0.6em; text-align: justify; }
        .cover { text-align: center; margin: 0; padding: 0; }
        .cover img { max-width: 100%; max-height: 100%; }
        """;

    public static void Write(BookContent book, Stream output)
    {
        if (book.Chapters.Count == 0)
            throw new ArgumentException("Livro sem capítulos.", nameof(book));

        using var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true);
        var stamp = book.Modified;

        Add(zip, "mimetype", "application/epub+zip", stamp, CompressionLevel.NoCompression);
        Add(zip, "META-INF/container.xml", """
            <?xml version="1.0" encoding="UTF-8"?>
            <container version="1.0" xmlns="urn:oasis:names:tc:opendocument:xmlns:container">
              <rootfiles>
                <rootfile full-path="OEBPS/content.opf" media-type="application/oebps-package+xml"/>
              </rootfiles>
            </container>
            """, stamp);
        Add(zip, "OEBPS/style.css", Css, stamp);

        if (book.CoverJpeg is { } cover)
        {
            var entry = zip.CreateEntry("OEBPS/cover.jpg", CompressionLevel.NoCompression);
            entry.LastWriteTime = stamp;
            using (var s = entry.Open())
                s.Write(cover);
            Add(zip, "OEBPS/cover.xhtml", Page(book.Language, "Capa",
                """<div class="cover"><img src="cover.jpg" alt="Capa"/></div>"""), stamp);
        }

        for (var i = 0; i < book.Chapters.Count; i++)
        {
            var chapter = book.Chapters[i];
            Add(zip, $"OEBPS/{ChapterFile(i)}", Page(book.Language, chapter.Title,
                $"<h1>{Escape(chapter.Title)}</h1>\n{chapter.XhtmlBody}"), stamp);
        }

        Add(zip, "OEBPS/nav.xhtml", Nav(book), stamp);
        Add(zip, "OEBPS/toc.ncx", Ncx(book), stamp);
        Add(zip, "OEBPS/content.opf", Opf(book), stamp);
    }

    private static string ChapterFile(int index) => $"chapter-{index + 1:D5}.xhtml";

    private static string Page(string lang, string title, string body) => $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <!DOCTYPE html>
        <html xmlns="http://www.w3.org/1999/xhtml" xmlns:epub="http://www.idpf.org/2007/ops" xml:lang="{lang}" lang="{lang}">
        <head><meta charset="UTF-8"/><title>{Escape(title)}</title><link rel="stylesheet" type="text/css" href="style.css"/></head>
        <body>
        {body}
        </body>
        </html>
        """;

    private static string Nav(BookContent book)
    {
        var items = new StringBuilder();
        for (var i = 0; i < book.Chapters.Count; i++)
            items.Append($"""<li><a href="{ChapterFile(i)}">{Escape(book.Chapters[i].Title)}</a></li>""").Append('\n');

        return Page(book.Language, "Sumário", $"""
            <nav epub:type="toc" id="toc"><h1>Sumário</h1>
            <ol>
            {items}</ol>
            </nav>
            """);
    }

    private static string Ncx(BookContent book)
    {
        var points = new StringBuilder();
        for (var i = 0; i < book.Chapters.Count; i++)
        {
            points.Append($"""
                <navPoint id="nav-{i + 1}" playOrder="{i + 1}"><navLabel><text>{Escape(book.Chapters[i].Title)}</text></navLabel><content src="{ChapterFile(i)}"/></navPoint>
                """).Append('\n');
        }

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <ncx xmlns="http://www.daisy.org/z3986/2005/ncx/" version="2005-1">
            <head><meta name="dtb:uid" content="{Escape(book.Identifier)}"/><meta name="dtb:depth" content="1"/></head>
            <docTitle><text>{Escape(book.Title)}</text></docTitle>
            <navMap>
            {points}</navMap>
            </ncx>
            """;
    }

    private static string Opf(BookContent book)
    {
        var manifest = new StringBuilder();
        var spine = new StringBuilder();

        manifest.Append("""<item id="nav" href="nav.xhtml" media-type="application/xhtml+xml" properties="nav"/>""").Append('\n');
        manifest.Append("""<item id="ncx" href="toc.ncx" media-type="application/x-dtbncx+xml"/>""").Append('\n');
        manifest.Append("""<item id="css" href="style.css" media-type="text/css"/>""").Append('\n');
        if (book.CoverJpeg is not null)
        {
            manifest.Append("""<item id="cover-image" href="cover.jpg" media-type="image/jpeg" properties="cover-image"/>""").Append('\n');
            manifest.Append("""<item id="cover" href="cover.xhtml" media-type="application/xhtml+xml"/>""").Append('\n');
            spine.Append("""<itemref idref="cover" linear="no"/>""").Append('\n');
        }
        for (var i = 0; i < book.Chapters.Count; i++)
        {
            manifest.Append($"""<item id="c{i + 1}" href="{ChapterFile(i)}" media-type="application/xhtml+xml"/>""").Append('\n');
            spine.Append($"""<itemref idref="c{i + 1}"/>""").Append('\n');
        }

        var modified = book.Modified.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
        var author = book.Author is null ? "" : $"<dc:creator>{Escape(book.Author)}</dc:creator>";
        var description = book.Description is null ? "" : $"<dc:description>{Escape(book.Description)}</dc:description>";
        var coverMeta = book.CoverJpeg is null ? "" : """<meta name="cover" content="cover-image"/>""";

        return $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <package xmlns="http://www.idpf.org/2007/opf" version="3.0" unique-identifier="book-id" xml:lang="{book.Language}">
            <metadata xmlns:dc="http://purl.org/dc/elements/1.1/">
            <dc:identifier id="book-id">{Escape(book.Identifier)}</dc:identifier>
            <dc:title>{Escape(book.Title)}</dc:title>
            {author}
            {description}
            <dc:language>{book.Language}</dc:language>
            <meta property="dcterms:modified">{modified}</meta>
            {coverMeta}
            </metadata>
            <manifest>
            {manifest}</manifest>
            <spine toc="ncx">
            {spine}</spine>
            </package>
            """;
    }

    private static void Add(ZipArchive zip, string path, string content, DateTimeOffset stamp,
        CompressionLevel level = CompressionLevel.Optimal)
    {
        var entry = zip.CreateEntry(path, level);
        entry.LastWriteTime = stamp;
        using var stream = entry.Open();
        stream.Write(Encoding.UTF8.GetBytes(content));
    }

    private static string Escape(string value) => SecurityElement.Escape(value);
}
