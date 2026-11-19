using System.Net;

namespace NovelShelf.Core.Net;

public sealed class PoliteHttpClient : IDisposable
{
    private readonly HttpClient _http;
    private readonly TimeSpan _minInterval;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly TimeProvider _time;
    private DateTimeOffset _lastRequest = DateTimeOffset.MinValue;

    public PoliteHttpClient(TimeSpan minInterval, HttpMessageHandler? handler = null, TimeProvider? time = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd($"Mozilla/5.0 (X11; Linux x86_64) NovelShelf/{typeof(PoliteHttpClient).Assembly.GetName().Version?.ToString(2) ?? "0"}");
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pt-BR,pt;q=0.9");
        _minInterval = minInterval;
        _time = time ?? TimeProvider.System;
    }

    public Task<string> GetStringAsync(Uri url, CancellationToken ct = default) =>
        SendAsync(url, r => r.Content.ReadAsStringAsync(ct), ct);

    public Task<byte[]> GetBytesAsync(Uri url, CancellationToken ct = default) =>
        SendAsync(url, r => r.Content.ReadAsByteArrayAsync(ct), ct);

    private async Task<T> SendAsync<T>(Uri url, Func<HttpResponseMessage, Task<T>> read, CancellationToken ct)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; ; attempt++)
        {
            await WaitTurnAsync(ct);
            using var response = await _http.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
                return await read(response);

            var retryable = response.StatusCode is HttpStatusCode.TooManyRequests or >= HttpStatusCode.InternalServerError;
            if (!retryable || attempt == maxAttempts)
                throw new HttpRequestException($"GET {url} falhou: {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);

            var delay = response.Headers.RetryAfter?.Delta ?? TimeSpan.FromSeconds(Math.Pow(2, attempt));
            await Task.Delay(delay, _time, ct);
        }
    }

    private async Task WaitTurnAsync(CancellationToken ct)
    {
        await _gate.WaitAsync(ct);
        try
        {
            var wait = _lastRequest + _minInterval - _time.GetUtcNow();
            if (wait > TimeSpan.Zero)
                await Task.Delay(wait, _time, ct);
            _lastRequest = _time.GetUtcNow();
        }
        finally
        {
            _gate.Release();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _gate.Dispose();
    }
}
