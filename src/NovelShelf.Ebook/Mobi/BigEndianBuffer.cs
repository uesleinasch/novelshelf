using System.Buffers.Binary;
using System.Text;

namespace NovelShelf.Ebook.Mobi;

internal sealed class BigEndianBuffer
{
    private readonly MemoryStream _stream = new();

    public int Length => (int)_stream.Length;

    public BigEndianBuffer U8(byte value)
    {
        _stream.WriteByte(value);
        return this;
    }

    public BigEndianBuffer U16(ushort value)
    {
        Span<byte> b = stackalloc byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(b, value);
        _stream.Write(b);
        return this;
    }

    public BigEndianBuffer U32(uint value)
    {
        Span<byte> b = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(b, value);
        _stream.Write(b);
        return this;
    }

    public BigEndianBuffer U64(ulong value)
    {
        Span<byte> b = stackalloc byte[8];
        BinaryPrimitives.WriteUInt64BigEndian(b, value);
        _stream.Write(b);
        return this;
    }

    public BigEndianBuffer Bytes(ReadOnlySpan<byte> value)
    {
        _stream.Write(value);
        return this;
    }

    public BigEndianBuffer Ascii(string value) => Bytes(Encoding.ASCII.GetBytes(value));

    public BigEndianBuffer Zeros(int count)
    {
        for (var i = 0; i < count; i++)
            _stream.WriteByte(0);
        return this;
    }

    public byte[] ToArray() => _stream.ToArray();
}
