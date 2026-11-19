using System.Globalization;
using System.Security;
using System.Text;

namespace NovelShelf.Ebook.Mobi;

public sealed record MobiChapter(string Title, IReadOnlyList<string> Chunks)
{
    public MobiChapter(string title, string body) : this(title, [body])
    {
    }
}

public sealed class MobiBook
{
    public required string Title { get; init; }
    public IReadOnlyList<string> Authors { get; init; } = [];
    public IReadOnlyList<string> Contributors { get; init; } = [];
    public string? Publisher { get; init; }
    public string? Subject { get; init; }
    public string? Description { get; init; }
    public DateTimeOffset CreatedDate { get; init; }
    public DateTimeOffset? PublishedDate { get; init; }
    public string DocType { get; init; } = "EBOK";
    public string Language { get; init; } = "und";
    public bool FixedLayout { get; init; }
    public bool RightToLeft { get; init; }
    public bool Vertical { get; init; }
    public (int Width, int Height)? OriginalResolution { get; init; }
    public IReadOnlyList<MobiChapter> Chapters { get; init; } = [];
    public IReadOnlyList<string> CssFlows { get; init; } = [];
    public IReadOnlyList<byte[]> Images { get; init; } = [];
    public byte[]? CoverImage { get; init; }
    public byte[]? ThumbImage { get; init; }
    public uint UniqueId { get; init; }

    public string Asin => UniqueId.ToString("x15", CultureInfo.InvariantCulture);

    public string ThumbnailFileName => $"thumbnail_{Asin}_EBOK_portrait.jpg";

    public void Write(Stream output) => Realize().WriteTo(output);

    public static string EmbedUri(int imageIndex, string mime = "image/jpeg") =>
        $"kindle:embed:{MobiEncoding.To32(imageIndex + 1)}?mime={mime}";

    internal PalmDatabase Realize()
    {
        if (Chapters.Count == 0 || Chapters.All(c => c.Chunks.All(string.IsNullOrEmpty)))
            throw new InvalidOperationException("Um livro AZW3 precisa de ao menos um capítulo com texto.");

        var db = new PalmDatabase(Title, CreatedDate);
        var (html, chunks, chapters) = ChaptersToText();
        var css = CssFlows.Select(Encoding.UTF8.GetBytes).ToList();
        byte[] text = [.. html, .. css.SelectMany(c => c)];
        var textRecords = TextRecords.Split(text, chapters);

        var nullRecord = CreateNullRecord();
        db.Add([]);

        nullRecord.TextRecordCount = (ushort)textRecords.Count;
        nullRecord.TextLength = (uint)text.Length;
        foreach (var record in textRecords)
            db.Add(record);

        var lastLength = textRecords.Count > 0 ? textRecords[^1].Length : 0;
        if (lastLength % 4 != 0)
            db.Add(new byte[lastLength % 4]);
        nullRecord.FirstNonBookIndex = (uint)(db.LastIndex + 1);

        var (chunkIndex, chunkCncx) = ChunkIndexRecord(chunks);
        nullRecord.ChunkIndex = (uint)db.Add(HeaderIndexRecord(TagxTables.Chunk,
            MobiEncoding.Decimal(text.Length, 10), chunks.Count, cncxCount: 1));
        db.Add(chunkIndex);
        db.Add(chunkCncx);

        nullRecord.SkeletonIndex = (uint)db.Add(HeaderIndexRecord(TagxTables.Skeleton,
            $"SKEL{MobiEncoding.Decimal(chunks.Count - 1, 10)}", chunks.Count, cncxCount: 0));
        db.Add(SkeletonIndexRecord(chunks));

        var (ncxIndex, ncxCncx) = NcxIndexRecord(chapters);
        nullRecord.NcxIndex = (uint)db.Add(HeaderIndexRecord(TagxTables.NcxSingle,
            MobiEncoding.Decimal(chapters.Count - 1, 3), chapters.Count, cncxCount: 1));
        db.Add(ncxIndex);
        db.Add(ncxCncx);

        var images = Images.ToList();
        if (CoverImage is not null)
            images.Add(CoverImage);
        if (ThumbImage is not null)
            images.Add(ThumbImage);
        if (images.Count > 0)
        {
            nullRecord.FirstImageIndex = (uint)(db.LastIndex + 1);
            nullRecord.Exth.AddInt(ExthType.Kf8CountResources, images.Count);
        }
        foreach (var image in images)
            db.Add(Jfif.Normalize(image));

        db.Add(FixedRecords.Fdst([html.Length, .. css.Select(c => c.Length)]));
        nullRecord.FdstEntryCount = (uint)(CssFlows.Count + 1);
        nullRecord.LastContentRecord = (ushort)db.LastIndex;

        db.Add(FixedRecords.Flis());
        nullRecord.FlisRecordCount = 1;
        nullRecord.FlisRecordNumber = (uint)db.LastIndex;

        db.Add(FixedRecords.Fcis((uint)text.Length));
        nullRecord.FcisRecordCount = 1;
        nullRecord.FcisRecordNumber = (uint)db.LastIndex;

        db.Add(FixedRecords.Eof);
        db.Records[0] = nullRecord.ToBytes();
        return db;
    }

    private NullRecord CreateNullRecord()
    {
        var record = new NullRecord(Title)
        {
            UniqueId = UniqueId,
            Locale = Locales.LocaleCode(Language),
        };
        var exth = record.Exth;
        var lastImageId = Images.Count;

        exth.AddString(ExthType.Title, Title);
        exth.AddString(ExthType.UpdatedTitle, Title);
        exth.AddString(ExthType.Author, Authors);
        exth.AddString(ExthType.Contributor, Contributors);
        exth.AddString(ExthType.Publisher, Publisher);
        exth.AddString(ExthType.Subject, Subject);
        exth.AddString(ExthType.Description, Description);
        exth.AddString(ExthType.Asin, Asin);
        exth.AddString(ExthType.Language, Locales.BaseLanguage(Language));
        if (PublishedDate is { } published)
            exth.AddString(ExthType.PublishingDate,
                published.ToString("yyyy'-'MM'-'dd'T'HH':'mm':'ss'.'ffffff'+07:00'", CultureInfo.InvariantCulture));
        exth.AddString(ExthType.DocType, string.IsNullOrEmpty(DocType) ? "EBOK" : DocType);
        if (FixedLayout)
            exth.AddString(ExthType.FixedLayout, "true");
        if (OriginalResolution is { } resolution)
            exth.AddString(ExthType.OriginalResolution, $"{resolution.Width}x{resolution.Height}");
        if (RightToLeft)
        {
            exth.AddString(ExthType.PrimaryWritingMode, Vertical ? "vertical-rl" : "horizontal-rl");
            exth.AddString(ExthType.PageProgressionDirection, "rtl");
        }
        if (CoverImage is not null)
        {
            exth.AddInt(ExthType.CoverOffset, lastImageId);
            exth.AddInt(ExthType.HasFakeCover, 0);
            exth.AddString(ExthType.Kf8CoverUri, $"kindle:embed:{MobiEncoding.To32(lastImageId + 1)}");
            lastImageId++;
        }
        if (ThumbImage is not null)
            exth.AddInt(ExthType.ThumbOffset, lastImageId);

        return record;
    }

    private (byte[] Html, List<ChunkInfo> Chunks, List<ChapterInfo> Chapters) ChaptersToText()
    {
        var text = new MemoryStream();
        var chunks = new List<ChunkInfo>();
        var chapters = new List<ChapterInfo>();
        var chunkId = 0;

        foreach (var chapter in Chapters)
        {
            var chapterStart = (int)text.Length;
            foreach (var chunk in chapter.Chunks)
            {
                var (head, insertAt) = Skeleton(chunkId);
                var body = Encoding.UTF8.GetBytes(chunk);
                var preStart = (int)text.Length;
                chunks.Add(new ChunkInfo(
                    PreStart: preStart,
                    PreLength: head.Length,
                    InsertPosition: preStart + insertAt,
                    ContentStart: preStart + head.Length,
                    ContentLength: body.Length));
                text.Write(head);
                text.Write(body);
                chunkId++;
            }
            chapters.Add(new ChapterInfo(chapter.Title, chapterStart, (int)text.Length - chapterStart));
        }

        return (text.ToArray(), chunks, chapters);
    }

    private (byte[] Head, int InsertAt) Skeleton(int chunkId)
    {
        var sb = new StringBuilder();
        sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        sb.Append("<html xmlns=\"http://www.w3.org/1999/xhtml\">\n");
        sb.Append("  <head>\n");
        sb.Append("    <title>").Append(SecurityElement.Escape(Title)).Append("</title>\n");
        sb.Append("    <meta http-equiv=\"Content-Type\" content=\"text/html; charset=utf-8\"/>");
        if (FixedLayout && OriginalResolution is { } viewport)
            sb.Append("\n    <meta name=\"viewport\" content=\"width=").Append(viewport.Width)
                .Append(", height=").Append(viewport.Height).Append("\"/>");
        for (var i = 0; i < CssFlows.Count; i++)
            sb.Append("\n    <link rel=\"stylesheet\" type=\"text/css\" href=\"kindle:flow:")
                .Append(MobiEncoding.To32(i + 1)).Append("?mime=text/css\"/>");
        sb.Append("\n  </head>\n");
        sb.Append("  <body aid=\"").Append(MobiEncoding.To32(chunkId)).Append("\">");
        var insertAt = Encoding.UTF8.GetByteCount(sb.ToString());
        sb.Append("\n  </body>\n</html>");
        return (Encoding.UTF8.GetBytes(sb.ToString()), insertAt);
    }

    private static byte[] HeaderIndexRecord(TagxTag[] tagx, string label, int entryCount, uint cncxCount)
    {
        var entry = new BigEndianBuffer()
            .Bytes(MobiEncoding.IndxString(label))
            .U16((ushort)entryCount)
            .Zeros(3)
            .ToArray();
        return new IndexRecord
        {
            Tagx = tagx,
            Type = 2,
            Entries = [entry],
            SubEntryCount = (uint)entryCount,
            CncxCount = cncxCount,
        }.ToBytes();
    }

    private static (byte[] Index, byte[] Cncx) ChunkIndexRecord(List<ChunkInfo> chunks)
    {
        var entries = new List<byte[]>();
        var cncx = new List<byte[]>();
        var cncxOffset = 0;
        var control = TagxTables.ControlByte(TagxTables.Chunk);
        for (var i = 0; i < chunks.Count; i++)
        {
            var label = MobiEncoding.CncxString($"P-//*[@aid='{MobiEncoding.To32(i)}']");
            cncx.Add(label);
            entries.Add(new BigEndianBuffer()
                .Bytes(MobiEncoding.IndxString(MobiEncoding.Decimal(chunks[i].InsertPosition, 10)))
                .U8(control)
                .Bytes(MobiEncoding.Vwi(cncxOffset))
                .Bytes(MobiEncoding.Vwi(i))
                .Bytes(MobiEncoding.Vwi(i))
                .Bytes(MobiEncoding.Vwi(0))
                .Bytes(MobiEncoding.Vwi(chunks[i].ContentLength))
                .ToArray());
            cncxOffset += label.Length;
        }

        return (new IndexRecord { HeaderType = 1, Entries = entries }.ToBytes(), new CncxRecord(cncx).ToBytes());
    }

    private static byte[] SkeletonIndexRecord(List<ChunkInfo> chunks)
    {
        var control = TagxTables.ControlByte(TagxTables.Skeleton);
        var entries = chunks.Select((chunk, i) => new BigEndianBuffer()
            .Bytes(MobiEncoding.IndxString($"SKEL{MobiEncoding.Decimal(i, 10)}"))
            .U8(control)
            .Bytes(MobiEncoding.Vwi(1))
            .Bytes(MobiEncoding.Vwi(1))
            .Bytes(MobiEncoding.Vwi(chunk.PreStart))
            .Bytes(MobiEncoding.Vwi(chunk.PreLength))
            .Bytes(MobiEncoding.Vwi(chunk.PreStart))
            .Bytes(MobiEncoding.Vwi(chunk.PreLength))
            .ToArray()).ToList();

        return new IndexRecord { HeaderType = 1, Entries = entries }.ToBytes();
    }

    private static (byte[] Index, byte[] Cncx) NcxIndexRecord(List<ChapterInfo> chapters)
    {
        var entries = new List<byte[]>();
        var cncx = new List<byte[]>();
        var cncxOffset = 0;
        var control = TagxTables.ControlByte(TagxTables.Chunk);
        foreach (var chapter in chapters)
        {
            var label = MobiEncoding.CncxString(chapter.Title);
            cncx.Add(label);
            entries.Add(new BigEndianBuffer()
                .Bytes(MobiEncoding.IndxString(chapter.Start.ToString("x3", CultureInfo.InvariantCulture)))
                .U8(control)
                .Bytes(MobiEncoding.Vwi(chapter.Start))
                .Bytes(MobiEncoding.Vwi(chapter.Length))
                .Bytes(MobiEncoding.Vwi(cncxOffset))
                .Bytes(MobiEncoding.Vwi(0))
                .ToArray());
            cncxOffset += label.Length;
        }

        return (new IndexRecord { HeaderType = 1, Entries = entries }.ToBytes(), new CncxRecord(cncx).ToBytes());
    }
}
