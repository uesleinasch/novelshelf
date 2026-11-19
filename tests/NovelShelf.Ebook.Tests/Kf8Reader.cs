using System.Buffers.Binary;
using System.Text;

namespace NovelShelf.Ebook.Tests;

public sealed record Kf8Entry(string Label, IReadOnlyDictionary<int, int[]> Tags);

public sealed class Kf8Reader
{
    private readonly byte[] _data;
    private readonly List<int> _offsets = [];

    public Kf8Reader(byte[] data)
    {
        _data = data;
        var count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(76));
        for (var i = 0; i < count; i++)
            _offsets.Add((int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(78 + i * 8)));

        var r0 = Record(0);
        TextLength = (int)U32(r0, 4);
        TextRecordCount = BinaryPrimitives.ReadUInt16BigEndian(r0.AsSpan(8));
        ExtraFlags = BinaryPrimitives.ReadUInt16BigEndian(r0.AsSpan(0xF2));
        FdstIndex = (int)U32(r0, 0xC0);
        NcxIndex = (int)U32(r0, 0xF4);
        ChunkIndex = (int)U32(r0, 0xF8);
        SkeletonIndex = (int)U32(r0, 0xFC);
        FirstImageIndex = U32(r0, 0x6C);
        Exth = ReadExth(r0);
        Text = ReadText();
    }

    public int RecordCount => _offsets.Count;
    public int TextLength { get; }
    public int TextRecordCount { get; }
    public int ExtraFlags { get; }
    public int FdstIndex { get; }
    public int NcxIndex { get; }
    public int ChunkIndex { get; }
    public int SkeletonIndex { get; }
    public uint FirstImageIndex { get; }
    public byte[] Text { get; }
    public ILookup<uint, byte[]> Exth { get; }

    public byte[] Record(int i)
    {
        var start = _offsets[i];
        var end = i + 1 < _offsets.Count ? _offsets[i + 1] : _data.Length;
        return _data[start..end];
    }

    public string ExthString(uint type) => Encoding.UTF8.GetString(Exth[type].Single());

    private byte[] ReadText()
    {
        var text = new MemoryStream();
        for (var i = 1; i <= TextRecordCount; i++)
        {
            var record = Record(i);
            var size = record.Length;
            for (var flags = ExtraFlags >> 1; flags != 0; flags >>= 1)
            {
                if ((flags & 1) != 0)
                    size -= TrailingEntrySize(record.AsSpan(0, size));
            }
            if ((ExtraFlags & 1) != 0)
                size -= (record[size - 1] & 3) + 1;
            text.Write(record, 0, size);
        }
        return text.ToArray();
    }

    private static int TrailingEntrySize(ReadOnlySpan<byte> data)
    {
        var num = 0;
        foreach (var v in data[Math.Max(0, data.Length - 4)..])
        {
            if ((v & 0x80) != 0)
                num = 0;
            num = (num << 7) | (v & 0x7F);
        }
        return num;
    }

    public List<Kf8Entry> ReadIndex(int headerIndex)
    {
        var header = Record(headerIndex);
        var tagxOffset = (int)U32(header, 180);
        var tagx = new List<(int Tag, int Values, int Mask, int End)>();
        var tagxLength = (int)U32(header, tagxOffset + 4);
        for (var p = tagxOffset + 12; p < tagxOffset + tagxLength; p += 4)
            tagx.Add((header[p], header[p + 1], header[p + 2], header[p + 3]));

        var entries = new List<Kf8Entry>();
        var dataRecords = (int)U32(header, 24);
        for (var r = 1; r <= dataRecords; r++)
        {
            var record = Record(headerIndex + r);
            var idxtStart = (int)U32(record, 20);
            var count = (int)U32(record, 24);
            Assert.Equal("IDXT", Encoding.ASCII.GetString(record, idxtStart, 4));
            for (var e = 0; e < count; e++)
            {
                var pos = BinaryPrimitives.ReadUInt16BigEndian(record.AsSpan(idxtStart + 4 + e * 2));
                var labelLength = record[pos];
                var label = Encoding.UTF8.GetString(record, pos + 1, labelLength);
                var p = pos + 1 + labelLength;
                var control = record[p++];
                var tags = new Dictionary<int, int[]>();
                foreach (var (tag, values, mask, end) in tagx)
                {
                    if (end == 1)
                        continue;
                    var n = (control & mask) >> System.Numerics.BitOperations.TrailingZeroCount(mask);
                    if (n == 0)
                        continue;
                    var list = new int[n * values];
                    for (var k = 0; k < list.Length; k++)
                        list[k] = ReadVwi(record, ref p);
                    tags[tag] = list;
                }
                entries.Add(new Kf8Entry(label, tags));
            }
        }
        return entries;
    }

    public List<string> ReadCncxStrings(int cncxRecord, IEnumerable<int> offsets)
    {
        var record = Record(cncxRecord);
        return offsets.Select(o =>
        {
            var p = o;
            var length = ReadVwiForward(record, ref p);
            return Encoding.UTF8.GetString(record, p, length);
        }).ToList();
    }

    public List<string> RebuildParts()
    {
        var skeletons = ReadIndex(SkeletonIndex);
        var fragments = ReadIndex(ChunkIndex);
        var parts = new List<string>();
        var fragPtr = 0;
        foreach (var skel in skeletons)
        {
            var fragCount = skel.Tags[1][0];
            var start = skel.Tags[6][0];
            var length = skel.Tags[6][1];
            var basePtr = start + length;
            var skeleton = Text[start..basePtr];
            for (var i = 0; i < fragCount; i++)
            {
                var frag = fragments[fragPtr++];
                var insertPos = int.Parse(frag.Label) - start;
                var fragLength = frag.Tags[6][1];
                skeleton = [.. skeleton[..insertPos], .. Text[basePtr..(basePtr + fragLength)], .. skeleton[insertPos..]];
                basePtr += fragLength;
            }
            parts.Add(Encoding.UTF8.GetString(skeleton));
        }
        return parts;
    }

    private ILookup<uint, byte[]> ReadExth(byte[] r0)
    {
        var mobiHeaderLength = (int)U32(r0, 20);
        var p = 16 + mobiHeaderLength;
        Assert.Equal("EXTH", Encoding.ASCII.GetString(r0, p, 4));
        var count = (int)U32(r0, p + 8);
        var list = new List<(uint, byte[])>();
        p += 12;
        for (var i = 0; i < count; i++)
        {
            var type = U32(r0, p);
            var length = (int)U32(r0, p + 4);
            list.Add((type, r0[(p + 8)..(p + length)]));
            p += length;
        }
        return list.ToLookup(x => x.Item1, x => x.Item2);
    }

    private static int ReadVwi(byte[] data, ref int p)
    {
        var value = 0;
        while (true)
        {
            var b = data[p++];
            value = (value << 7) | (b & 0x7F);
            if ((b & 0x80) != 0)
                return value;
        }
    }

    private static int ReadVwiForward(byte[] data, ref int p) => ReadVwi(data, ref p);

    private static uint U32(byte[] data, int offset) => BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(offset));
}
