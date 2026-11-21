using System.Globalization;

namespace NovelShelf.Core.Library;

public enum KindleState
{
    NeverSent,
    Pending,
    UpToDate,
}

public enum LibrarySort
{
    Updated,
    Title,
    Added,
}

public sealed record LibraryFilter(
    WorkKind? Kind = null,
    string? Site = null,
    KindleState? Kindle = null,
    string? Text = null,
    LibrarySort Sort = LibrarySort.Updated)
{
    private static readonly CompareInfo Compare = CultureInfo.GetCultureInfo("pt-BR").CompareInfo;
    private const CompareOptions Loose = CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace;

    public bool IsActive => Kind is not null || Site is not null || Kindle is not null || !string.IsNullOrWhiteSpace(Text);

    public static KindleState KindleStateOf(LibraryNovel novel) =>
        novel.SentAt is null
            ? KindleState.NeverSent
            : novel.DownloadedChapters > novel.SentChapterCount || novel.NeedsResend || novel.VolumeLayoutChanged || novel.ResendMarked
                ? KindleState.Pending
                : KindleState.UpToDate;

    public IReadOnlyList<T> Apply<T>(IEnumerable<T> items, Func<T, LibraryNovel> novelOf, Func<T, string> siteOf)
    {
        var text = Text?.Trim();
        var matches = items.Where(item =>
        {
            var novel = novelOf(item);
            return (Kind is null || novel.Kind == Kind)
                && (Site is null || string.Equals(siteOf(item), Site, StringComparison.OrdinalIgnoreCase))
                && (Kindle is null || KindleStateOf(novel) == Kindle)
                && (string.IsNullOrEmpty(text) || Contains(novel.Title, text) || Contains(novel.Author, text));
        });

        var sorted = Sort switch
        {
            LibrarySort.Title => matches.OrderBy(i => novelOf(i).Title, Compare.GetStringComparer(Loose)),
            LibrarySort.Added => matches.OrderByDescending(i => novelOf(i).AddedAt),
            _ => matches.OrderByDescending(i => novelOf(i).UpdatedAt),
        };
        return sorted.ThenBy(i => novelOf(i).Id).ToList();
    }

    private static bool Contains(string? value, string text) =>
        value is not null && Compare.IndexOf(value, text, Loose) >= 0;
}
