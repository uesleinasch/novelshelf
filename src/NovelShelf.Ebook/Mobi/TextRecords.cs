namespace NovelShelf.Ebook.Mobi;

internal readonly record struct ChunkInfo(int PreStart, int PreLength, int InsertPosition, int ContentStart, int ContentLength);

internal readonly record struct ChapterInfo(string Title, int Start, int Length);

internal static class TextRecords
{
    public const int MaxSize = 4096;

    public static List<byte[]> Split(byte[] text, IReadOnlyList<ChapterInfo> chapters)
    {
        var records = new List<byte[]>();
        for (var from = 0; from < text.Length; from += MaxSize)
        {
            var to = Math.Min(from + MaxSize, text.Length);
            var b = new BigEndianBuffer().Bytes(text.AsSpan(from, to - from));

            var overlap = MultibyteOverlap(text, to);
            b.Bytes(text.AsSpan(to, overlap)).U8((byte)overlap);

            b.Bytes(IndexingTrail(chapters, from, to));
            records.Add(b.ToArray());
        }
        return records;
    }

    // Matches Calibre: when a record ends mid UTF-8 sequence, the rest of the
    // character is copied after the data and its size goes in the multibyte entry.
    private static int MultibyteOverlap(byte[] text, int end)
    {
        var count = 0;
        while (end + count < text.Length && count < 3 && (text[end + count] & 0xC0) == 0x80)
            count++;
        return count;
    }

    private static byte[] IndexingTrail(IReadOnlyList<ChapterInfo> chapters, int from, int to)
    {
        Strand? strand = null;
        for (var i = 0; i < chapters.Count; i++)
        {
            var chapter = chapters[i];
            var end = chapter.Start + chapter.Length;
            if (chapter.Start <= from && end >= to)
            {
                var atExactBoundary = chapter.Start == from || end == to;
                strand = new Strand(i) { DoesSpan = !atExactBoundary };
                break;
            }

            if ((from <= chapter.Start && chapter.Start <= to) || (from <= end && end <= to))
            {
                strand ??= new Strand(i);
                strand.Siblings++;
            }
        }

        var data = strand?.Encode() ?? [];
        return WithSize(data);
    }

    private static byte[] WithSize(byte[] data)
    {
        for (var sizeBytes = 1; ; sizeBytes++)
        {
            var size = MobiEncoding.Vwi(data.Length + sizeBytes);
            if (size.Length == sizeBytes)
                return [.. data, .. size];
        }
    }

    private sealed class Strand(int index)
    {
        private const int TbsType = 8;

        public bool DoesSpan { get; init; }
        public byte Siblings { get; set; }

        public byte[] Encode()
        {
            var value = index << 3;
            if (DoesSpan)
                value |= 0b0001;
            value |= 0b0010;
            if (Siblings > 1)
                value |= 0b0100;

            var b = new BigEndianBuffer().Bytes(MobiEncoding.Vwi(value)).Bytes(MobiEncoding.Vwi(TbsType));
            if (Siblings > 1)
                b.U8(Siblings);
            if (DoesSpan)
                b.Bytes(MobiEncoding.Vwi(0));
            return b.ToArray();
        }
    }
}
