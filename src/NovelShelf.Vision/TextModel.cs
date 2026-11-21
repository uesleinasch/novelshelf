using System.Security.Cryptography;

namespace NovelShelf.Vision;

public sealed record ModelProgress(long Done, long Total);

public sealed class TextModel(string folder, HttpClient http)
{
    public const string FileName = "comic-text-and-bubble-detector-v4-s-int8.onnx";
    public const long Size = 11_120_765;
    public const string Sha256 = "5fe9e4f576e49d4e7e8b0e029d6d3cdc252abd4694113e1cae120e62c931ea79";
    public static readonly Uri Url =
        new("https://huggingface.co/ogkalu/comic-text-and-bubble-detector/resolve/main/detector-v4-s_int8.onnx");

    private readonly Lock _gate = new();
    private OnnxTextRegionDetector? _detector;

    public string Path => System.IO.Path.Combine(folder, FileName);
    public bool IsInstalled => File.Exists(Path);

    public OnnxTextRegionDetector? Detector
    {
        get
        {
            if (!IsInstalled)
                return null;
            lock (_gate)
                return _detector ??= new OnnxTextRegionDetector(Path, Sha256[..12]);
        }
    }

    public async Task InstallAsync(IProgress<ModelProgress>? progress = null, CancellationToken ct = default)
    {
        if (IsInstalled)
            return;
        Directory.CreateDirectory(folder);
        var temp = Path + ".part";
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        try
        {
            using var response = await http.GetAsync(Url, HttpCompletionOption.ResponseHeadersRead, ct);
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
                    progress?.Report(new ModelProgress(done, Size));
                }
            }
            var actual = Convert.ToHexStringLower(hash.GetHashAndReset());
            if (actual != Sha256)
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
