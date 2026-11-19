using System.IO.Compression;
using System.Xml.Linq;
using NovelShelf.Ebook;
using NovelShelf.Ebook.Epub;

namespace NovelShelf.Ebook.Tests;

public sealed class EpubWriterTests
{
    private static BookContent Sample(int chapters, byte[]? cover = null) => new(
        Identifier: "urn:uuid:3f2a",
        Title: "Livro & <Teste>",
        Author: "Fulano",
        Language: "pt-BR",
        Description: "Sinopse",
        Modified: new DateTimeOffset(2026, 11, 18, 12, 0, 0, TimeSpan.Zero),
        CoverJpeg: cover,
        Chapters: Enumerable.Range(1, chapters)
            .Select(i => new BookChapter($"Capítulo {i}", $"<p>Texto <em>{i}</em> &amp; mais</p>\n<p>linha<br />quebra</p>"))
            .ToList());

    private static byte[] Write(BookContent book)
    {
        using var ms = new MemoryStream();
        EpubWriter.Write(book, ms);
        return ms.ToArray();
    }

    [Fact]
    public void Mimetype_is_first_and_stored()
    {
        using var zip = new ZipArchive(new MemoryStream(Write(Sample(1))));

        var first = zip.Entries[0];
        Assert.Equal("mimetype", first.FullName);
        Assert.Equal(first.Length, first.CompressedLength);
    }

    [Fact]
    public void All_xml_entries_are_well_formed()
    {
        using var zip = new ZipArchive(new MemoryStream(Write(Sample(3, cover: [0xFF, 0xD8, 0xFF, 0xD9]))));

        var xmlEntries = zip.Entries.Where(e => e.FullName.EndsWith(".xhtml") || e.FullName.EndsWith(".opf")
                                                || e.FullName.EndsWith(".ncx") || e.FullName.EndsWith(".xml")).ToList();
        Assert.Equal(8, xmlEntries.Count);
        foreach (var entry in xmlEntries)
        {
            using var s = entry.Open();
            XDocument.Load(s);
        }
    }

    [Fact]
    public void Opf_lists_chapters_in_spine_order()
    {
        using var zip = new ZipArchive(new MemoryStream(Write(Sample(3))));
        using var s = zip.GetEntry("OEBPS/content.opf")!.Open();
        var opf = XDocument.Load(s);
        XNamespace ns = "http://www.idpf.org/2007/opf";

        var spine = opf.Descendants(ns + "itemref").Select(x => (string)x.Attribute("idref")!).ToList();

        Assert.Equal(["c1", "c2", "c3"], spine);
        Assert.Equal("urn:uuid:3f2a", opf.Descendants().First(e => e.Name.LocalName == "identifier").Value);
    }

    [Fact]
    public void Output_is_deterministic()
    {
        var book = Sample(5);

        Assert.Equal(Write(book), Write(book));
    }
}
