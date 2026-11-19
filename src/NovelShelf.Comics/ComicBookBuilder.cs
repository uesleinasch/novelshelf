using NovelShelf.Ebook.Mobi;

namespace NovelShelf.Comics;

public sealed record ComicChapter(string Title, IReadOnlyList<byte[]> Pages);

public sealed record ComicBookInfo(
    string Title,
    string? Author,
    string? Description,
    string Language,
    DateTimeOffset Modified,
    uint UniqueId,
    byte[]? CoverJpeg);

public static class ComicBookBuilder
{
    private const string PageCss = """
        @page { margin: 0; }
        html, body { margin: 0; padding: 0; width: 100%; height: 100%; }
        div { display: none; }
        img { display: block; position: absolute; top: 0; left: 0; width: 100%; height: 100%; margin: 0; padding: 0; }
        """;

    public static MobiBook Build(ComicBookInfo info, IReadOnlyList<ComicChapter> chapters, KindleProfile profile, bool rightToLeft)
    {
        var images = new List<byte[]>();
        var mobiChapters = new List<MobiChapter>();
        foreach (var chapter in chapters)
        {
            if (chapter.Pages.Count == 0)
                continue;
            var chunks = new List<string>(chapter.Pages.Count);
            foreach (var page in chapter.Pages)
            {
                chunks.Add($"<div>.</div><img src=\"{MobiBook.EmbedUri(images.Count)}\" width=\"{profile.Width}\" height=\"{profile.Height}\" alt=\"\"/>");
                images.Add(page);
            }
            mobiChapters.Add(new MobiChapter(chapter.Title, chunks));
        }
        if (images.Count == 0)
            throw new InvalidOperationException($"'{info.Title}' não tem páginas para converter.");

        return new MobiBook
        {
            Title = info.Title,
            Authors = info.Author is null ? [] : [info.Author],
            Description = info.Description,
            Language = info.Language,
            CreatedDate = info.Modified,
            UniqueId = info.UniqueId,
            FixedLayout = true,
            RightToLeft = rightToLeft,
            OriginalResolution = (profile.Width, profile.Height),
            Comic = true,
            CssFlows = [PageCss],
            Images = images,
            CoverImage = info.CoverJpeg ?? images[0],
            Chapters = mobiChapters,
        };
    }
}
