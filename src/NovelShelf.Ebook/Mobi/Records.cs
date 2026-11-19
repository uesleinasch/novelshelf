using System.Text;

namespace NovelShelf.Ebook.Mobi;

internal enum ExthType : uint
{
    Title = 99,
    Author = 100,
    Publisher = 101,
    Description = 103,
    Subject = 105,
    PublishingDate = 106,
    Contributor = 108,
    Asin = 113,
    FixedLayout = 122,
    Kf8CountResources = 125,
    Kf8CoverUri = 129,
    CoverOffset = 201,
    ThumbOffset = 202,
    HasFakeCover = 203,
    DocType = 501,
    UpdatedTitle = 503,
    Language = 524,
    PrimaryWritingMode = 525,
    PageProgressionDirection = 527,
}

internal sealed class ExthSection
{
    private const int HeaderLength = 12;
    private readonly List<(ExthType Type, byte[] Data)> _entries = [];

    public void AddString(ExthType type, params IEnumerable<string?> values)
    {
        foreach (var value in values)
        {
            if (!string.IsNullOrEmpty(value))
                _entries.Add((type, Encoding.UTF8.GetBytes(value)));
        }
    }

    public void AddInt(ExthType type, int value) =>
        _entries.Add((type, new BigEndianBuffer().U32((uint)value).ToArray()));

    private int LengthWithoutPadding => HeaderLength + _entries.Sum(e => e.Data.Length + 8);

    public int Length => LengthWithoutPadding + MobiEncoding.InvMod(LengthWithoutPadding, 4);

    public void WriteTo(BigEndianBuffer b)
    {
        b.Ascii("EXTH").U32((uint)LengthWithoutPadding).U32((uint)_entries.Count);
        foreach (var (type, data) in _entries)
            b.U32((uint)type).U32((uint)(data.Length + 8)).Bytes(data);
        b.Zeros(MobiEncoding.InvMod(LengthWithoutPadding, 4));
    }
}

internal sealed class NullRecord(string fullName)
{
    private const int PalmDocHeaderLength = 16;
    private const int Kf8HeaderLength = 264;
    private const int PaddingLength = 8192;

    public ExthSection Exth { get; } = new();
    public uint TextLength { get; set; }
    public ushort TextRecordCount { get; set; }
    public uint UniqueId { get; set; }
    public uint Locale { get; set; }
    public uint FirstNonBookIndex { get; set; }
    public uint FirstImageIndex { get; set; } = uint.MaxValue;
    public ushort LastContentRecord { get; set; } = ushort.MaxValue;
    public uint FdstEntryCount { get; set; }
    public uint FcisRecordNumber { get; set; }
    public uint FcisRecordCount { get; set; }
    public uint FlisRecordNumber { get; set; }
    public uint FlisRecordCount { get; set; }
    public uint NcxIndex { get; set; } = uint.MaxValue;
    public uint ChunkIndex { get; set; } = uint.MaxValue;
    public uint SkeletonIndex { get; set; } = uint.MaxValue;

    public byte[] ToBytes()
    {
        var name = Encoding.UTF8.GetBytes(fullName);
        var b = new BigEndianBuffer();

        b.U16(1).U16(0).U32(TextLength).U16(TextRecordCount).U16(0x1000).U16(0).U16(0);

        b.Ascii("MOBI").U32(Kf8HeaderLength).U32(2).U32(65001).U32(UniqueId).U32(8);
        for (var i = 0; i < 10; i++)
            b.U32(uint.MaxValue);
        b.U32(FirstNonBookIndex)
            .U32((uint)(PalmDocHeaderLength + Kf8HeaderLength + Exth.Length))
            .U32((uint)name.Length)
            .U32(Locale).U32(0).U32(0).U32(8)
            .U32(FirstImageIndex)
            .U32(0).U32(0).U32(0).U32(0)
            .U32(0b1010000)
            .Zeros(32)
            .U32(uint.MaxValue).U32(uint.MaxValue).U32(0).U32(0)
            .Zeros(12)
            .U16(0).U16(LastContentRecord)
            .U32(FdstEntryCount)
            .U32(FcisRecordNumber).U32(FcisRecordCount)
            .U32(FlisRecordNumber).U32(FlisRecordCount)
            .U64(0).U32(uint.MaxValue).U32(0).U32(uint.MaxValue).U32(uint.MaxValue)
            .U32(0b11)
            .U32(NcxIndex);
        b.U32(ChunkIndex).U32(SkeletonIndex).U32(uint.MaxValue).U32(uint.MaxValue).Zeros(16);

        Exth.WriteTo(b);
        b.Bytes(name).Zeros(PaddingLength);
        return b.ToArray();
    }
}

internal enum TagxTag : uint
{
    EntryPosition = 0x01010100,
    EntryLength = 0x02010200,
    EntryNameOffset = 0x03010400,
    EntryDepthLevel = 0x04010800,
    SkeletonChunkCount = 0x01010300,
    SkeletonGeometry = 0x06020C00,
    ChunkCncxOffset = 0x02010100,
    ChunkFileNumber = 0x03010200,
    ChunkSequenceNumber = 0x04010400,
    ChunkGeometry = 0x06020800,
    End = 0x00000001,
}

internal static class TagxTables
{
    public static readonly TagxTag[] NcxSingle =
        [TagxTag.EntryPosition, TagxTag.EntryLength, TagxTag.EntryNameOffset, TagxTag.EntryDepthLevel, TagxTag.End];

    public static readonly TagxTag[] Skeleton =
        [TagxTag.SkeletonChunkCount, TagxTag.SkeletonGeometry, TagxTag.End];

    public static readonly TagxTag[] Chunk =
        [TagxTag.ChunkCncxOffset, TagxTag.ChunkFileNumber, TagxTag.ChunkSequenceNumber, TagxTag.ChunkGeometry, TagxTag.End];

    private static readonly Dictionary<byte, int> BitmaskShift = new()
    {
        [1] = 0, [2] = 1, [3] = 0, [4] = 2, [8] = 3, [12] = 2, [16] = 4, [32] = 5, [48] = 4, [64] = 6, [128] = 7, [192] = 6,
    };

    public static byte ControlByte(TagxTag[] table)
    {
        byte answer = 0;
        foreach (var tag in table)
        {
            var raw = (uint)tag;
            var valuesPerEntry = (byte)(raw >> 16);
            var bitmask = (byte)(raw >> 8);
            var endFlag = (byte)raw;
            if (endFlag == 1)
                return answer;

            var values = tag switch
            {
                TagxTag.SkeletonGeometry => 4,
                TagxTag.ChunkGeometry or TagxTag.SkeletonChunkCount => 2,
                _ => 1,
            };
            var entries = (byte)(values / valuesPerEntry);
            answer |= (byte)(bitmask & (byte)(entries << BitmaskShift[bitmask]));
        }
        throw new InvalidOperationException("Tabela TAGX sem marcador de fim.");
    }
}

internal sealed class IndexRecord
{
    private const int IndxHeaderLength = 192;
    private const int TagxHeaderLength = 12;

    public uint Type { get; init; }
    public uint HeaderType { get; init; }
    public TagxTag[] Tagx { get; init; } = [];
    public required List<byte[]> Entries { get; init; }
    public uint SubEntryCount { get; init; }
    public uint CncxCount { get; init; }

    public byte[] ToBytes()
    {
        var offset = IndxHeaderLength;
        var tagxLength = TagxHeaderLength + Tagx.Length * 4;
        if (Tagx.Length > 0)
            offset += tagxLength;

        var entriesLength = 0;
        var offsets = new List<ushort>();
        foreach (var entry in Entries)
        {
            offsets.Add((ushort)offset);
            offset += entry.Length;
            entriesLength += entry.Length;
        }

        var b = new BigEndianBuffer();
        b.Ascii("INDX").U32(IndxHeaderLength).Zeros(4)
            .U32(HeaderType).U32(Type)
            .U32((uint)(offset + entriesLength % 4))
            .U32((uint)Entries.Count)
            .U32(65001).U32(uint.MaxValue)
            .U32(SubEntryCount)
            .U32(0).U32(0).U32(0)
            .U32(CncxCount)
            .Zeros(124)
            .U32(Tagx.Length > 0 ? IndxHeaderLength : 0u)
            .Zeros(8);

        if (Tagx.Length > 0)
        {
            b.Ascii("TAGX").U32((uint)tagxLength).U32(1);
            foreach (var tag in Tagx)
                b.U32((uint)tag);
        }

        foreach (var entry in Entries)
            b.Bytes(entry);

        b.Zeros(entriesLength % 4).Ascii("IDXT");
        foreach (var o in offsets)
            b.U16(o);
        b.Zeros(LengthWithoutPadding(tagxLength, entriesLength) % 4);
        return b.ToArray();
    }

    private int LengthWithoutPadding(int tagxLength, int entriesLength)
    {
        var length = IndxHeaderLength + 4 + Entries.Count * 2 + entriesLength + entriesLength % 4;
        if (Tagx.Length > 0)
            length += tagxLength;
        return length;
    }
}

internal sealed class CncxRecord(List<byte[]> entries)
{
    public byte[] ToBytes()
    {
        var b = new BigEndianBuffer();
        foreach (var entry in entries)
            b.Bytes(entry);
        b.Zeros(b.Length % 4);
        return b.ToArray();
    }
}

internal static class FixedRecords
{
    public static readonly byte[] Eof = [0xE9, 0x8E, 0x0D, 0x0A];

    public static byte[] Flis() => new BigEndianBuffer()
        .Ascii("FLIS").U32(8).U16(65).U16(0).U32(0).U32(uint.MaxValue).U16(1).U16(3).U32(3).U32(1).U32(uint.MaxValue)
        .ToArray();

    public static byte[] Fcis(uint textLength) => new BigEndianBuffer()
        .Ascii("FCIS").U32(20).U32(16).U32(2).U32(0).U32(textLength).U32(0).U32(40).U32(0).U32(40).U32(8)
        .U16(1).U16(1).U32(0)
        .ToArray();

    public static byte[] Fdst(IReadOnlyList<int> flowLengths)
    {
        var b = new BigEndianBuffer().Ascii("FDST").U32(12).U32((uint)flowLengths.Count);
        var offset = 0;
        foreach (var length in flowLengths)
        {
            b.U32((uint)offset).U32((uint)(offset + length));
            offset += length;
        }
        return b.ToArray();
    }
}
