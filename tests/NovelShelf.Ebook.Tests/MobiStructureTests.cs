using System.Text;
using System.Xml.Linq;
using NovelShelf.Ebook.Mobi;

namespace NovelShelf.Ebook.Tests;

public sealed class MobiStructureTests
{
    private static Kf8Reader Read(MobiBook book)
    {
        using var ms = new MemoryStream();
        book.Write(ms);
        return new Kf8Reader(ms.ToArray());
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(3, 3)]
    [InlineData(60, 40)]
    public void Text_records_reassemble_to_declared_length_and_valid_utf8(int chapters, int paragraphs)
    {
        var reader = Read(MobiBookTests.Sample(chapters, paragraphs));

        Assert.Equal(reader.TextLength, reader.Text.Length);
        var decoded = new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(reader.Text);
        Assert.Contains("coração", decoded);
        Assert.EndsWith("p { text-indent: 1em; }", decoded);
    }

    [Theory]
    [InlineData(1, 3)]
    [InlineData(60, 40)]
    public void Skeletons_and_fragments_rebuild_into_well_formed_xhtml_per_chapter(int chapters, int paragraphs)
    {
        var reader = Read(MobiBookTests.Sample(chapters, paragraphs));

        var parts = reader.RebuildParts();

        Assert.Equal(chapters, parts.Count);
        for (var i = 0; i < parts.Count; i++)
        {
            var doc = XDocument.Parse(parts[i]);
            XNamespace ns = "http://www.w3.org/1999/xhtml";
            var body = doc.Root!.Element(ns + "body")!;
            Assert.Equal($"Capítulo {i + 1}", body.Element(ns + "h1")!.Value);
            Assert.Equal(paragraphs, body.Elements(ns + "p").Count());
            Assert.Equal("Coração &amp; Espada", doc.Root.Element(ns + "head")!.Element(ns + "title")!.ToString()
                .Replace("<title xmlns=\"http://www.w3.org/1999/xhtml\">", "").Replace("</title>", ""));
        }
    }

    [Fact]
    public void Ncx_lists_chapter_titles_in_order()
    {
        var book = MobiBookTests.Sample(12);
        var reader = Read(book);

        var entries = reader.ReadIndex(reader.NcxIndex);
        var titles = reader.ReadCncxStrings(reader.NcxIndex + 2, entries.Select(e => e.Tags[3][0]));

        Assert.Equal(book.Chapters.Select(c => c.Title), titles);
        var starts = entries.Select(e => e.Tags[1][0]).ToList();
        Assert.Equal(starts.Order(), starts);
    }

    [Fact]
    public void Exth_carries_kindle_metadata()
    {
        var reader = Read(MobiBookTests.Sample(2, cover: true));

        Assert.Equal("Coração & Espada", reader.ExthString(503));
        Assert.Equal("Autora Teste", reader.ExthString(100));
        Assert.Equal("EBOK", reader.ExthString(501));
        Assert.Equal("000000000c0ffee", reader.ExthString(113));
        Assert.Equal("pt", reader.ExthString(524));
        Assert.Equal("kindle:embed:0001", reader.ExthString(129));
        var cover = reader.Record((int)reader.FirstImageIndex);
        Assert.Equal("JFIF\0"u8.ToArray(), cover[6..11]);
    }

    [Fact]
    public void Fdst_splits_html_and_css_flows()
    {
        var reader = Read(MobiBookTests.Sample(2));

        var fdst = reader.Record(reader.FdstIndex);
        Assert.Equal("FDST", Encoding.ASCII.GetString(fdst, 0, 4));
        var flows = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(fdst.AsSpan(8));
        Assert.Equal(2u, flows);
        var cssEnd = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(fdst.AsSpan(24));
        Assert.Equal((uint)reader.TextLength, cssEnd);
    }
}
