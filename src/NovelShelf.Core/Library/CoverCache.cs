namespace NovelShelf.Core.Library;

public sealed class CoverCache(AppPaths paths)
{
    public string PathFor(long workId) => Path.Combine(paths.Covers, $"{workId}.jpg");

    private string SourcePathFor(long workId) => Path.Combine(paths.Covers, $"{workId}.src");

    public byte[]? TryRead(long workId, Uri? source)
    {
        var file = PathFor(workId);
        if (!File.Exists(file))
            return null;
        if (source is not null)
        {
            var sourceFile = SourcePathFor(workId);
            if (!File.Exists(sourceFile) || File.ReadAllText(sourceFile).Trim() != source.ToString())
                return null;
        }
        return File.ReadAllBytes(file);
    }

    public void Save(long workId, Uri? source, byte[] jpeg)
    {
        Directory.CreateDirectory(paths.Covers);
        var file = PathFor(workId);
        var temp = file + ".tmp";
        File.WriteAllBytes(temp, jpeg);
        File.Move(temp, file, overwrite: true);
        if (source is null)
            File.Delete(SourcePathFor(workId));
        else
            File.WriteAllText(SourcePathFor(workId), source.ToString());
    }

    public void Forget(long workId)
    {
        File.Delete(PathFor(workId));
        File.Delete(SourcePathFor(workId));
    }
}
