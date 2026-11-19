using System.Collections.Concurrent;
using Avalonia.Media.Imaging;
using NovelShelf.Comics;
using NovelShelf.Core.Library;
using NovelShelf.Core.Net;

namespace NovelShelf.App.Services;

public sealed class CoverLoader(AppPaths paths)
{
    private readonly HttpClient _http = CreateClient();
    private readonly CoverCache _files = new(paths);
    private readonly ConcurrentDictionary<string, Task<Bitmap?>> _cache = new();

    private static HttpClient CreateClient()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd(PoliteHttpClient.UserAgent);
        return http;
    }

    public Task<Bitmap?> LoadAsync(Uri? url, long? libraryId = null)
    {
        if (url is null && libraryId is null)
            return Task.FromResult<Bitmap?>(null);
        var key = libraryId is { } id ? $"lib:{id}:{url}" : url!.ToString();
        return _cache.GetOrAdd(key, _ => LoadCoreAsync(url, libraryId));
    }

    public void Forget(long libraryId)
    {
        foreach (var key in _cache.Keys.Where(k => k.StartsWith($"lib:{libraryId}:", StringComparison.Ordinal)).ToList())
            _cache.TryRemove(key, out _);
        _files.Forget(libraryId);
    }

    public void Store(long libraryId, byte[] image)
    {
        Forget(libraryId);
        if (CoverImage.ToJpeg(image) is { } jpeg)
            _files.Save(libraryId, null, jpeg);
    }

    private async Task<Bitmap?> LoadCoreAsync(Uri? url, long? libraryId)
    {
        try
        {
            if (libraryId is { } id && _files.TryRead(id, url) is { } cached)
                return Decode(cached);
            if (url is null)
                return null;
            var bytes = await _http.GetByteArrayAsync(url);
            if (libraryId is { } owner && CoverImage.ToJpeg(bytes) is { } jpeg)
                _files.Save(owner, url, jpeg);
            return Decode(bytes);
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
