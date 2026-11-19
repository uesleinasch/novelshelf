namespace NovelShelf.Devices;

public enum KindleConnection
{
    MassStorage,
    Mtp,
}

public sealed record KindleDevice(string Name, string RootPath, KindleConnection Connection)
{
    public string DocumentsPath => Path.Combine(RootPath, "documents");
    public string ThumbnailsPath => Path.Combine(RootPath, "system", "thumbnails");
}

public sealed record SendRequest(string LocalFile, string TargetFileName, byte[]? ThumbnailJpeg, string? ThumbnailFileName);

public sealed record SendResult(string TargetPath, bool ThumbnailWritten);
