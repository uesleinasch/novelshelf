namespace NovelShelf.Core.Library;

public sealed record LibraryNovel(
    long Id,
    string SourceId,
    string Slug,
    Uri Url,
    string Title,
    string? Author,
    string? Synopsis,
    Uri? CoverUrl,
    string? Status,
    IReadOnlyList<string> Genres,
    int? RemoteChapterCount,
    Uri? FirstChapterUrl,
    string BookId,
    int DownloadedChapters,
    int SentChapterCount,
    DateTimeOffset AddedAt,
    DateTimeOffset UpdatedAt,
    DateTimeOffset? SentAt,
    WorkKind Kind = WorkKind.Novel,
    string? RemoteKey = null,
    string? SourceName = null,
    bool RightToLeft = false,
    bool NeedsResend = false,
    bool CaughtUp = false,
    int VolumeSize = 0,
    int? SentVolumeSize = null,
    IReadOnlyList<ChapterRange>? ResendRanges = null)
{
    public bool VolumeLayoutChanged => SentAt is not null && (SentVolumeSize ?? 0) != VolumeSize;

    public bool ResendMarked => SentAt is not null && ResendRanges is { Count: > 0 };
}

public sealed record ChapterRange(int First, int Through)
{
    public bool Overlaps(int first, int through) => First < through && first < Through;

    public static ChapterRange FromChapters(int firstChapter, int lastChapter) => new(firstChapter - 1, lastChapter);

    public static IReadOnlyList<ChapterRange> Merge(IEnumerable<ChapterRange> ranges)
    {
        var merged = new List<ChapterRange>();
        foreach (var range in ranges.Select(r => r with { First = Math.Max(0, r.First) }).Where(r => r.Through > r.First).OrderBy(r => r.First))
        {
            if (merged.Count > 0 && range.First <= merged[^1].Through)
                merged[^1] = merged[^1] with { Through = Math.Max(merged[^1].Through, range.Through) };
            else
                merged.Add(range);
        }
        return merged;
    }

    public static string? Format(IReadOnlyList<ChapterRange> ranges) =>
        ranges.Count == 0 ? null : string.Join(',', ranges.Select(r => $"{r.First}-{r.Through}"));

    public static IReadOnlyList<ChapterRange> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];
        var ranges = new List<ChapterRange>();
        foreach (var part in text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var bounds = part.Split('-');
            if (bounds.Length == 2 && int.TryParse(bounds[0], out var first) && int.TryParse(bounds[1], out var through))
                ranges.Add(new ChapterRange(first, through));
        }
        return Merge(ranges);
    }
}

public enum WorkKind
{
    Novel,
    Comic,
}

public sealed record StoredComicChapter(
    long NovelId,
    string Key,
    string Title,
    double Number,
    int Order,
    int PageCount,
    string Folder,
    DateTimeOffset DownloadedAt);

public sealed record StoredChapter(
    long NovelId,
    int Index,
    Uri Url,
    string Title,
    string Html,
    Uri? NextUrl,
    DateTimeOffset DownloadedAt);

public sealed record QueuedDownload(long NovelId, int Requested, int Done, bool Paused);
