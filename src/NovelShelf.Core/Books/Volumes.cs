using System.Security.Cryptography;
using System.Text;
using NovelShelf.Core.Library;

namespace NovelShelf.Core.Books;

public sealed record VolumeSpec(int Number, int First, int Count)
{
    public int Through => First + Count;
    public bool IsWholeWork => Number == 0;
    public ChapterRange Range => new(First, Through);
}

public static class Volumes
{
    public static IReadOnlyList<VolumeSpec> Split(int chapterCount, int volumeSize)
    {
        if (volumeSize <= 0 || chapterCount <= volumeSize)
            return volumeSize <= 0 ? [new VolumeSpec(0, 0, chapterCount)] : [new VolumeSpec(1, 0, chapterCount)];
        var volumes = new List<VolumeSpec>();
        for (var first = 0; first < chapterCount; first += volumeSize)
            volumes.Add(new VolumeSpec(volumes.Count + 1, first, Math.Min(volumeSize, chapterCount - first)));
        return volumes;
    }

    public static IReadOnlyList<VolumeSpec> Changed(IReadOnlyList<VolumeSpec> plan, int sentChapterCount, bool everything,
        IReadOnlyCollection<ChapterRange>? resend = null) =>
        everything
            ? plan
            : plan.Where(v => v.Through > sentChapterCount || (resend?.Any(r => r.Overlaps(v.First, v.Through)) ?? false)).ToList();

    public static string FileName(string baseName, VolumeSpec volume) =>
        volume.IsWholeWork ? $"{baseName}.azw3" : $"{baseName}-vol{volume.Number:D2}.azw3";

    public static string Title(string title, VolumeSpec volume) =>
        volume.IsWholeWork ? title : $"{title} — Vol. {volume.Number:D2} (cap. {volume.First + 1}–{volume.Through})";

    public static string BookKey(string bookId, VolumeSpec volume) =>
        volume.IsWholeWork ? bookId : $"{bookId}-vol{volume.Number}";

    public static string Identifier(string bookKey) =>
        $"urn:uuid:{(Guid.TryParseExact(bookKey, "N", out var id) ? id : new Guid(MD5.HashData(Encoding.UTF8.GetBytes(bookKey))))}";

    public static byte[]? CoverFor(byte[]? cover, VolumeSpec volume) =>
        cover is null || volume.IsWholeWork ? cover : NovelShelf.Comics.CoverImage.WithVolumeBadge(cover, volume.Number);

    public static bool IsFileOf(string baseName, string fileName) =>
        fileName == $"{baseName}.azw3"
        || (fileName.StartsWith($"{baseName}-vol", StringComparison.Ordinal)
            && fileName.EndsWith(".azw3", StringComparison.Ordinal)
            && fileName[(baseName.Length + 4)..^5] is { Length: > 0 } digits
            && digits.All(char.IsAsciiDigit));

    public static string Describe(int chapterCount, int volumeSize)
    {
        var plan = Split(chapterCount, volumeSize);
        if (plan[0].IsWholeWork)
            return chapterCount == 0 ? "Um livro só." : $"Um livro só com {chapterCount} capítulos.";
        return plan.Count == 1
            ? $"1 volume (cap. 1–{plan[0].Through}). Novos volumes começam a cada {volumeSize} capítulos."
            : $"{plan.Count} volumes: " + string.Join(", ", plan.Select(v => $"{v.First + 1}–{v.Through}")) + ".";
    }
}
