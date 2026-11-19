using System.Globalization;
using System.Text;

namespace NovelShelf.Ebook.Mobi;

internal static class MobiEncoding
{
    private const string Base32Digits = "0123456789ABCDEFGHIJKLMNOPQRSTUV";

    public static string To32(int value)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(value);
        var sb = new StringBuilder();
        do
        {
            sb.Insert(0, Base32Digits[value % 32]);
            value /= 32;
        } while (value > 0);
        return sb.ToString().PadLeft(4, '0');
    }

    public static byte[] Vwi(int value)
    {
        var buffer = new List<byte>();
        var x = value;
        do
        {
            buffer.Add((byte)(x & 0x7F));
            x >>= 7;
        } while (x != 0);
        buffer[0] |= 0x80;
        buffer.Reverse();
        return buffer.ToArray();
    }

    public static byte[] IndxString(string label)
    {
        var bytes = Encoding.UTF8.GetBytes(label);
        return [(byte)bytes.Length, .. bytes];
    }

    public static byte[] CncxString(string label)
    {
        var bytes = Encoding.UTF8.GetBytes(label);
        return [.. Vwi(bytes.Length), .. bytes];
    }

    public static string Decimal(int value, int width) =>
        value.ToString(CultureInfo.InvariantCulture).PadLeft(width, '0');

    public static int InvMod(int a, int n)
    {
        var mod = a % n;
        return n - (mod == 0 ? n : mod);
    }
}
