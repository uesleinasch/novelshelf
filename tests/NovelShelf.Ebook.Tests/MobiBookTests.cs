using System.Buffers.Binary;
using System.Text;
using NovelShelf.Ebook.Mobi;

namespace NovelShelf.Ebook.Tests;

public sealed class MobiBookTests
{
    private static readonly byte[] TinyJpeg = Convert.FromBase64String(
        "/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP//////////////////////////////////////////////////////////////////////////////////////wgALCAABAAEBAREA/8QAFBABAAAAAAAAAAAAAAAAAAAAAP/aAAgBAQABPxA=");

    public static MobiBook Sample(int chapters, int paragraphs = 3, bool cover = false) => new()
    {
        Title = "Coração & Espada",
        Authors = ["Autora Teste"],
        Description = "Uma história de teste.",
        Language = "pt-BR",
        CreatedDate = new DateTimeOffset(2026, 11, 18, 0, 0, 0, TimeSpan.Zero),
        UniqueId = 0xC0FFEE,
        CoverImage = cover ? TinyJpeg : null,
        CssFlows = ["p { text-indent: 1em; }"],
        Chapters = Enumerable.Range(1, chapters).Select(c => new MobiChapter(
            $"Capítulo {c} – Ação",
            $"<h1>Capítulo {c}</h1>\n" + string.Concat(Enumerable.Range(1, paragraphs).Select(p =>
                $"<p>Parágrafo {p} do capítulo {c}: ação, coração, não, ç, ü, — “aspas” <em>ênfase</em> <strong>forte</strong>.</p>\n"))))
            .ToList(),
    };

    private static byte[] Bytes(MobiBook book)
    {
        using var ms = new MemoryStream();
        book.Write(ms);
        return ms.ToArray();
    }

    [Fact]
    public void Writes_palm_database_with_book_mobi_type_and_kf8_header()
    {
        var data = Bytes(Sample(1));

        Assert.Equal("BOOKMOBI", Encoding.ASCII.GetString(data, 60, 8));
        var firstRecord = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(78));
        Assert.Equal("MOBI", Encoding.ASCII.GetString(data, firstRecord + 16, 4));
        Assert.Equal(8u, BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(firstRecord + 16 + 20)));
    }

    [Fact]
    public void Output_is_deterministic()
    {
        Assert.Equal(Bytes(Sample(5, cover: true)), Bytes(Sample(5, cover: true)));
    }

    [Fact]
    public void Asin_and_thumbnail_name_derive_from_unique_id()
    {
        var book = Sample(1);

        Assert.Equal("000000000c0ffee", book.Asin);
        Assert.Equal("thumbnail_000000000c0ffee_EBOK_portrait.jpg", book.ThumbnailFileName);
    }

    [Fact]
    public void Book_without_text_is_rejected()
    {
        var book = new MobiBook { Title = "Vazio", Chapters = [new MobiChapter("x", "")] };

        Assert.Throws<InvalidOperationException>(() => Bytes(book));
    }

    [Fact]
    public void Dump_samples_for_external_validation()
    {
        var dir = Environment.GetEnvironmentVariable("NOVELSHELF_DUMP_DIR");
        Assert.SkipWhen(string.IsNullOrEmpty(dir), "Defina NOVELSHELF_DUMP_DIR para gravar amostras.");

        Directory.CreateDirectory(dir!);
        File.WriteAllBytes(Path.Combine(dir!, "one.azw3"), Bytes(Sample(1)));
        File.WriteAllBytes(Path.Combine(dir!, "three-cover.azw3"), Bytes(Sample(3, cover: true)));
        File.WriteAllBytes(Path.Combine(dir!, "sixty.azw3"), Bytes(Sample(60, paragraphs: 40, cover: true)));
    }
}
