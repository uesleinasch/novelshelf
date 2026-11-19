using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using NovelShelf.Core.Library;

namespace NovelShelf.App.Services;

public sealed class CoverLoader(AppPaths paths)
{
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _cache = new();

    public Task<Bitmap?> LoadAsync(Uri? url, long? libraryId = null)
    {
        if (url is null && libraryId is null)
            return Task.FromResult<Bitmap?>(null);
        var key = libraryId is { } id ? $"lib:{id}" : url!.ToString();
        return _cache.GetOrAdd(key, _ => LoadCoreAsync(url, libraryId));
    }

    private async Task<Bitmap?> LoadCoreAsync(Uri? url, long? libraryId)
    {
        try
        {
            if (libraryId is { } id)
            {
                var cached = Path.Combine(paths.Covers, $"{id}.jpg");
                if (File.Exists(cached))
                    return Decode(await File.ReadAllBytesAsync(cached));
            }
            if (url is null)
                return null;
            return Decode(await _http.GetByteArrayAsync(url));
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException or ArgumentException)
        {
            return null;
        }
    }

    private static Bitmap Decode(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes);
        return Bitmap.DecodeToWidth(stream, 240);
    }
}
