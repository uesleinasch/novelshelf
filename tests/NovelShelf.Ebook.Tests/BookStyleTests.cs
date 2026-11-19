using System.IO.Compression;
using NovelShelf.Ebook.Epub;

namespace NovelShelf.Ebook.Tests;

public sealed class BookStyleTests
{
    private static BookContent Book(BookStyle? style) => new(
        Identifier: "urn:uuid:3f2a",
        Title: "Livro",
        Author: null,
        Language: "pt-BR",
        Description: null,
        Modified: new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero),
        CoverJpeg: null,
        Chapters: [new BookChapter("Capítulo 1 - Início", "<p>Texto</p>")],
        Style: style);

    [Fact]
    public void Default_css_keeps_the_classic_look()
    {
        var css = BookStyle.Default.ToCss();

        Assert.Contains("text-indent: 1.2em; margin: 0 0 0.6em; text-align: justify;", css);
    }

    [Fact]
    public void Custom_style_changes_paragraph_css_with_invariant_numbers()
    {
        var css = new BookStyle { Indent = 0, ParagraphSpacing = 1.5, Justify = false }.ToCss();

        Assert.Contains("text-indent: 0em; margin: 0 0 1.5em; text-align: left;", css);
    }

    [Fact]
    public void Json_round_trips_and_normalizes()
    {
        var style = new BookStyle { Indent = 9, ParagraphSpacing = -1, Heading = ChapterHeading.NumberOnly };

        var json = style.ToJson();
        var back = BookStyle.FromJson(json)!;

        Assert.Contains("\"NumberOnly\"", json);
        Assert.Equal(3, back.Indent);
        Assert.Equal(0, back.ParagraphSpacing);
        Assert.Equal(ChapterHeading.NumberOnly, back.Heading);
        Assert.Null(BookStyle.FromJson("{quebrado"));
        Assert.Null(BookStyle.FromJson(null));
    }

    [Fact]
    public void Chapter_title_follows_heading_option()
    {
        Assert.Equal("Cap 3 - Fim", BookStyle.Default.ChapterTitle(3, "Cap 3 - Fim"));
        Assert.Equal("Capítulo 3", new BookStyle { Heading = ChapterHeading.NumberOnly }.ChapterTitle(3, "Cap 3 - Fim"));
    }

    [Fact]
    public void Epub_uses_style_css_and_can_hide_heading()
    {
        var hidden = new BookStyle { Indent = 0.5, Heading = ChapterHeading.Hidden };
        using var ms = new MemoryStream();
        EpubWriter.Write(Book(hidden), ms);
        using var zip = new ZipArchive(new MemoryStream(ms.ToArray()));

        string Read(string name)
        {
            using var reader = new StreamReader(zip.GetEntry(name)!.Open());
            return reader.ReadToEnd();
        }

        Assert.Contains("text-indent: 0.5em", Read("OEBPS/style.css"));
        Assert.Contains("font-family: serif", Read("OEBPS/style.css"));
        Assert.DoesNotContain("<h1>", Read("OEBPS/chapter-00001.xhtml"));
    }
}
