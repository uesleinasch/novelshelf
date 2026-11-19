using System.Text;

namespace NovelShelf.Ebook.Mobi;

internal sealed class PalmDatabase(string name, DateTimeOffset date)
{
    private const int HeaderLength = 78;
    private const int RecordHeaderLength = 8;
    private static readonly DateTimeOffset PalmEpoch = new(1904, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public List<byte[]> Records { get; } = [];

    public int Add(byte[] record)
    {
        Records.Add(record);
        return Records.Count - 1;
    }

    public int LastIndex => Records.Count - 1;

    public void WriteTo(Stream output)
    {
        var nameBytes = new byte[32];
        var encoded = Encoding.UTF8.GetBytes(name.TrimEnd('\0'));
        Array.Copy(encoded, nameBytes, Math.Min(encoded.Length, 31));
        var palmTime = (uint)(date - PalmEpoch).TotalSeconds;

        var b = new BigEndianBuffer()
            .Bytes(nameBytes)
            .U16(0).U16(0)
            .U32(palmTime).U32(palmTime).U32(palmTime)
            .U32(0).U32(0).U32(0)
            .Ascii("BOOK").Ascii("MOBI")
            .U32((uint)(Records.Count * 2 - 1))
            .U32(0)
            .U16((ushort)Records.Count);

        var offset = HeaderLength + RecordHeaderLength * Records.Count + 2;
        for (var i = 0; i < Records.Count; i++)
        {
            b.U32((uint)offset).U8(0).U8(0).U16((ushort)(i * 2));
            offset += Records[i].Length;
        }
        b.Zeros(2);

        output.Write(b.ToArray());
        foreach (var record in Records)
            output.Write(record);
    }
}
