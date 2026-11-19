namespace NovelShelf.Ebook;

public sealed record BookContent(
    string Identifier,
    string Title,
    string? Author,
    string Language,
    string? Description,
    DateTimeOffset Modified,
    byte[]? CoverJpeg,
    IReadOnlyList<BookChapter> Chapters,
    BookStyle? Style = null)
{
    public BookStyle EffectiveStyle => Style ?? BookStyle.Default;

    public string ChapterBody(BookChapter chapter) => EffectiveStyle.ShowsHeading
        ? $"<h1>{System.Security.SecurityElement.Escape(chapter.Title)}</h1>\n{chapter.XhtmlBody}"
        : chapter.XhtmlBody;
}

public sealed record BookChapter(string Title, string XhtmlBody);
