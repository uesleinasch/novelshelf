namespace NovelShelf.Ebook;

public sealed record BookContent(
    string Identifier,
    string Title,
    string? Author,
    string Language,
    string? Description,
    DateTimeOffset Modified,
    byte[]? CoverJpeg,
    IReadOnlyList<BookChapter> Chapters);

public sealed record BookChapter(string Title, string XhtmlBody);
