namespace NovelShelf.Ebook.Mobi;

internal static class Jfif
{
    private static readonly byte[] Header =
    [
        0xFF, 0xD8,
        0xFF, 0xE0,
        0x00, 0x10,
        0x4A, 0x46, 0x49, 0x46, 0x00,
        0x01, 0x02,
        0x00,
        0x00, 0x01,
        0x00, 0x01,
        0x00, 0x00,
    ];

    public static byte[] Normalize(byte[] jpeg)
    {
        if (jpeg.Length < 4 || jpeg[0] != 0xFF || jpeg[1] != 0xD8)
            throw new ArgumentException("Imagem não é JPEG.", nameof(jpeg));

        var hasJfif = jpeg.Length > 11 && jpeg[2] == 0xFF && jpeg[3] == 0xE0
                      && jpeg.AsSpan(6, 5).SequenceEqual("JFIF\0"u8);
        return hasJfif ? jpeg : [.. Header, .. jpeg.AsSpan(2)];
    }
}
