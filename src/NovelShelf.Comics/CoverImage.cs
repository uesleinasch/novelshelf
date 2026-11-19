namespace NovelShelf.Comics;

public static class CoverImage
{
    public static bool IsJpeg(ReadOnlySpan<byte> bytes) => bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xD8;

    public static byte[]? ToJpeg(byte[] bytes, int quality = 90)
    {
        if (IsJpeg(bytes))
            return bytes;
        return Raster.Decode(bytes, color: true)?.EncodeJpeg(quality);
    }
}
