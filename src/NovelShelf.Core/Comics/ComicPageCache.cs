using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using NovelShelf.Comics;
using NovelShelf.Core.Library;

namespace NovelShelf.Core.Comics;

public sealed class ComicPageCache(AppPaths paths)
{
    private const string FolderName = "cache";

    public string FolderOf(long comicId) =>
        Path.Combine(paths.Comics, comicId.ToString(CultureInfo.InvariantCulture), FolderName);

    public long SizeOf(long comicId)
    {
        var folder = FolderOf(comicId);
        return Directory.Exists(folder)
            ? new DirectoryInfo(folder).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length)
            : 0;
    }

    public void Clear(long comicId)
    {
        var folder = FolderOf(comicId);
        if (Directory.Exists(folder))
            Directory.Delete(folder, recursive: true);
    }

    public Session Open(long comicId, ComicPageOptions options)
    {
        var root = FolderOf(comicId);
        var settings = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(options.Fingerprint)))[..16];
        if (Directory.Exists(root))
        {
            foreach (var stale in Directory.EnumerateDirectories(root).Where(d => Path.GetFileName(d) != settings))
            {
                try
                {
                    Directory.Delete(stale, recursive: true);
                }
                catch (IOException)
                {
                }
            }
        }
        return new Session(Path.Combine(root, settings));
    }

    public sealed class Session(string folder)
    {
        public IConvertedPageStore For(string chapterKey) => new ChapterStore(Path.Combine(folder, ComicDownloader.FolderFor(chapterKey)));
    }

    private sealed class ChapterStore(string folder) : IConvertedPageStore
    {
        private static readonly byte[] Magic = "NSPC"u8.ToArray();

        private string PathOf(string slot) => Path.Combine(folder, slot + ".bin");

        public List<byte[]>? Find(string slot, byte[] key)
        {
            var path = PathOf(slot);
            if (!File.Exists(path))
                return null;
            try
            {
                using var reader = new BinaryReader(File.OpenRead(path));
                if (!reader.ReadBytes(Magic.Length).AsSpan().SequenceEqual(Magic)
                    || !reader.ReadBytes(key.Length).AsSpan().SequenceEqual(key))
                    return null;
                var count = reader.ReadInt32();
                var screens = new List<byte[]>(count);
                for (var i = 0; i < count; i++)
                {
                    var length = reader.ReadInt32();
                    var data = reader.ReadBytes(length);
                    if (data.Length != length)
                        return null;
                    screens.Add(data);
                }
                return screens;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                return null;
            }
        }

        public void Save(string slot, byte[] key, IReadOnlyList<byte[]> screens)
        {
            var path = PathOf(slot);
            var temp = path + ".tmp";
            try
            {
                Directory.CreateDirectory(folder);
                using (var writer = new BinaryWriter(File.Create(temp)))
                {
                    writer.Write(Magic);
                    writer.Write(key);
                    writer.Write(screens.Count);
                    foreach (var screen in screens)
                    {
                        writer.Write(screen.Length);
                        writer.Write(screen);
                    }
                }
                File.Move(temp, path, overwrite: true);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (File.Exists(temp))
                    File.Delete(temp);
            }
        }
    }
}
