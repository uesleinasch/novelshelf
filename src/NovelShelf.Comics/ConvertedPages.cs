using System.Security.Cryptography;
using System.Text;

namespace NovelShelf.Comics;

public interface IConvertedPageStore
{
    List<byte[]>? Find(string slot, byte[] key);
    void Save(string slot, byte[] key, IReadOnlyList<byte[]> screens);
}

public static class ConvertedPageKey
{
    public static byte[] Page(byte[] page, PageCuts? cuts)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(page);
        if (cuts is { IsEmpty: false })
            hash.AppendData(Encoding.UTF8.GetBytes(cuts.ToJson()));
        return hash.GetHashAndReset();
    }

    public static byte[] Strip(IReadOnlyList<byte[]> pages)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var page in pages)
        {
            hash.AppendData(BitConverter.GetBytes(page.Length));
            hash.AppendData(page);
        }
        return hash.GetHashAndReset();
    }
}
