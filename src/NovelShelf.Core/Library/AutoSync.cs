namespace NovelShelf.Core.Library;

public static class AutoSync
{
    public const int UpdateBatch = 1000;

    public static IReadOnlyList<LibraryNovel> UpdateCandidates(IEnumerable<LibraryNovel> works, bool includeComics) =>
        works.Where(w => w.CaughtUp && (includeComics || w.Kind == WorkKind.Novel)).ToList();

    public static IReadOnlyList<LibraryNovel> PendingForKindle(IEnumerable<LibraryNovel> works) =>
        works.Where(w => LibraryFilter.KindleStateOf(w) == KindleState.Pending).ToList();
}
