using System.Security.Cryptography;

namespace NovelShelf.Vision;

public sealed record ModelProgress(long Done, long Total);

public abstract class DownloadableModel<TDetector>(string folder, HttpClient http, string fileName, long size, string sha256, Uri url)
    where TDetector : class, IDisposable
{
    private readonly Lock _gate = new();
    private TDetector? _detector;

    public string Path => System.IO.Path.Combine(folder, fileName);
    public bool IsInstalled => File.Exists(Path);

    public TDetector? Detector
    {
        get
        {
            if (!IsInstalled)
                return null;
            lock (_gate)
                return _detector ??= Create(Path, sha256[..12]);
        }
    }

    protected abstract TDetector Create(string path, string id);

    public async Task InstallAsync(IProgress<ModelProgress>? progress = null, CancellationToken ct = default)
    {
        if (IsInstalled)
            return;
        Directory.CreateDirectory(folder);
        var temp = Path + ".part";
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            using var response = await http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            await using (var source = await response.Content.ReadAsStreamAsync(ct))
            await using (var target = File.Create(temp))
            {
                var buffer = new byte[81920];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    hash.AppendData(buffer, 0, read);
                    done += read;
                    progress?.Report(new ModelProgress(done, size));
                }
            }
            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (actual != sha256)
                throw new InvalidOperationException("O modelo baixado não confere com o SHA-256 esperado. Tente de novo mais tarde.");
            File.Move(temp, Path, overwrite: true);
        }
        finally
        {
            if (File.Exists(temp))
                File.Delete(temp);
        }
    }

    public void Remove()
    {
        lock (_gate)
        {
            _detector?.Dispose();
            _detector = null;
        }
        if (IsInstalled)
            File.Delete(Path);
    }
}
