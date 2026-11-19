using System.Collections.Concurrent;
using System.Net;

namespace NovelShelf.Core.Net;

public sealed class RateLimitedException(string message, DateTimeOffset retryAt)
    : HttpRequestException(message, null, HttpStatusCode.TooManyRequests)
{
    public DateTimeOffset RetryAt { get; } = retryAt;
}

public sealed record RateLimitNotice(string Host, DateTimeOffset Until);

public sealed class PoliteHttpClient : IDisposable
{
    public static readonly TimeSpan MaxInterval = TimeSpan.FromSeconds(30);
    public static readonly TimeSpan GiveUpPause = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan[] RateLimitWaits =
        [TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5)];
    private const int SuccessesToSpeedUp = 20;
    private const int ServerErrorAttempts = 4;

    private sealed class Host(TimeSpan interval)
    {
        public readonly SemaphoreSlim Gate = new(1, 1);
        public DateTimeOffset Last = DateTimeOffset.MinValue;
        public DateTimeOffset BlockedUntil = DateTimeOffset.MinValue;
        public TimeSpan Interval = interval;
        public int Successes;
    }

    private readonly HttpClient _http;
    private readonly TimeSpan _minInterval;
    private readonly TimeProvider _time;
    private readonly Func<TimeSpan, CancellationToken, Task> _sleep;
    private readonly ConcurrentDictionary<string, Host> _hosts = new(StringComparer.OrdinalIgnoreCase);

    public PoliteHttpClient(TimeSpan minInterval, HttpMessageHandler? handler = null, TimeProvider? time = null,
        Func<TimeSpan, CancellationToken, Task>? sleep = null)
    {
        _http = handler is null ? new HttpClient() : new HttpClient(handler);
        _http.Timeout = TimeSpan.FromSeconds(30);
        _http.DefaultRequestHeaders.UserAgent.ParseAdd(UserAgent);
        _http.DefaultRequestHeaders.AcceptLanguage.ParseAdd("pt-BR,pt;q=0.9");
        _minInterval = minInterval;
        _time = time ?? TimeProvider.System;
        _sleep = sleep ?? ((delay, ct) => Task.Delay(delay, _time, ct));
    }

    public static string UserAgent { get; } =
        $"Mozilla/5.0 (X11; Linux x86_64) NovelShelf/{typeof(PoliteHttpClient).Assembly.GetName().Version?.ToString(2) ?? "0"}";

    public event Action<RateLimitNotice>? RateLimited;

    public TimeSpan IntervalFor(string host) => _hosts.TryGetValue(host, out var h) ? h.Interval : _minInterval;

    public Task<string> GetStringAsync(Uri url, CancellationToken ct = default) =>
        SendAsync(url, r => r.Content.ReadAsStringAsync(ct), ct);

    public Task<byte[]> GetBytesAsync(Uri url, CancellationToken ct = default) =>
        SendAsync(url, r => r.Content.ReadAsByteArrayAsync(ct), ct);

    private async Task<T> SendAsync<T>(Uri url, Func<HttpResponseMessage, Task<T>> read, CancellationToken ct)
    {
        var host = _hosts.GetOrAdd(url.Host, _ => new Host(_minInterval));
        var limited = 0;
        var serverErrors = 0;
        while (true)
        {
            await WaitTurnAsync(host, ct);
            using var response = await _http.GetAsync(url, ct);
            if (response.IsSuccessStatusCode)
            {
                Succeeded(host);
                return await read(response);
            }

            if (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.ServiceUnavailable)
            {
                var now = _time.GetUtcNow();
                if (limited == RateLimitWaits.Length)
                {
                    var retryAt = Block(host, now + GiveUpPause);
                    throw new RateLimitedException(
                        $"{url.Host} pediu pausa (muitas requisições). Tento de novo às {retryAt.ToLocalTime():HH:mm}.", retryAt);
                }
                var until = Block(host, now + (RetryAfter(response, now) ?? RateLimitWaits[limited]));
                limited++;
                RateLimited?.Invoke(new RateLimitNotice(url.Host, until));
                continue;
            }

            serverErrors++;
            if ((int)response.StatusCode < 500 || serverErrors == ServerErrorAttempts)
                throw new HttpRequestException($"GET {url} falhou: {(int)response.StatusCode} {response.ReasonPhrase}", null, response.StatusCode);
            await _sleep(TimeSpan.FromSeconds(Math.Pow(2, serverErrors)), ct);
        }
    }

    private static TimeSpan? RetryAfter(HttpResponseMessage response, DateTimeOffset now)
    {
        var header = response.Headers.RetryAfter;
        var wait = header?.Delta ?? (header?.Date is { } date ? date - now : null);
        return wait is { } w && w > TimeSpan.Zero ? (w > TimeSpan.FromHours(1) ? TimeSpan.FromHours(1) : w) : null;
    }

    private DateTimeOffset Block(Host host, DateTimeOffset until)
    {
        lock (host)
        {
            host.Successes = 0;
            host.Interval = TimeSpan.FromTicks(Math.Min(MaxInterval.Ticks, Math.Max(host.Interval.Ticks * 2, TimeSpan.FromSeconds(1).Ticks)));
            if (until > host.BlockedUntil)
                host.BlockedUntil = until;
            return host.BlockedUntil;
        }
    }

    private void Succeeded(Host host)
    {
        lock (host)
        {
            if (host.Interval <= _minInterval || ++host.Successes < SuccessesToSpeedUp)
                return;
            host.Successes = 0;
            var faster = TimeSpan.FromTicks(host.Interval.Ticks * 3 / 4);
            host.Interval = faster < _minInterval ? _minInterval : faster;
        }
    }

    private async Task WaitTurnAsync(Host host, CancellationToken ct)
    {
        await host.Gate.WaitAsync(ct);
        try
        {
            DateTimeOffset ready;
            lock (host)
                ready = host.Last + host.Interval > host.BlockedUntil ? host.Last + host.Interval : host.BlockedUntil;
            var wait = ready - _time.GetUtcNow();
            if (wait > TimeSpan.Zero)
                await _sleep(wait, ct);
            lock (host)
                host.Last = _time.GetUtcNow();
        }
        finally
        {
            host.Gate.Release();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        foreach (var host in _hosts.Values)
            host.Gate.Dispose();
    }
}
