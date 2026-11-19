using System.Net;
using System.Net.Http.Headers;
using NovelShelf.Core.Net;

namespace NovelShelf.Tests.Net;

public sealed class PoliteHttpClientTests
{
    private sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 11, 19, 22, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Script(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<Uri> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Requests.Add(request.RequestUri!);
            return Task.FromResult(respond(request));
        }
    }

    private readonly Clock _clock = new();
    private readonly List<TimeSpan> _sleeps = [];

    private PoliteHttpClient Client(HttpMessageHandler handler) => new(TimeSpan.FromSeconds(1.5), handler, _clock, (delay, _) =>
    {
        _sleeps.Add(delay);
        _clock.Now += delay;
        return Task.CompletedTask;
    });

    private static HttpResponseMessage Ok() => new(HttpStatusCode.OK) { Content = new StringContent("ok") };

    private static HttpResponseMessage TooMany(TimeSpan? delta = null, DateTimeOffset? date = null)
    {
        var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests);
        if (delta is { } d)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(d);
        if (date is { } at)
            response.Headers.RetryAfter = new RetryConditionHeaderValue(at);
        return response;
    }

    private static Func<HttpRequestMessage, HttpResponseMessage> Sequence(params Func<HttpResponseMessage>[] responses)
    {
        var i = 0;
        return _ => responses[Math.Min(i++, responses.Length - 1)]();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Too_many_requests_waits_retry_after_and_slows_the_site_down()
    {
        using var http = Client(new Script(Sequence(() => TooMany(TimeSpan.FromSeconds(20)), Ok)));
        var notices = new List<RateLimitNotice>();
        http.RateLimited += notices.Add;
        var start = _clock.Now;

        Assert.Equal("ok", await http.GetStringAsync(new Uri("https://a.test/1"), Ct));

        Assert.Equal(start + TimeSpan.FromSeconds(20), _clock.Now);
        Assert.Equal(TimeSpan.FromSeconds(3), http.IntervalFor("a.test"));
        Assert.Equal([new RateLimitNotice("a.test", start + TimeSpan.FromSeconds(20))], notices);
    }

    [Fact]
    public async Task Without_retry_after_it_backs_off_and_understands_dates()
    {
        var start = _clock.Now;
        using var http = Client(new Script(Sequence(
            () => TooMany(),
            () => TooMany(date: start + TimeSpan.FromMinutes(3)),
            Ok)));

        await http.GetStringAsync(new Uri("https://a.test/1"), Ct);

        Assert.Equal(start + TimeSpan.FromMinutes(3), _clock.Now);
        Assert.Equal(TimeSpan.FromSeconds(15), _sleeps[0]);
    }

    [Fact]
    public async Task Persistent_blocking_gives_up_with_a_retry_time()
    {
        var script = new Script(_ => TooMany());
        using var http = Client(script);

        var error = await Assert.ThrowsAsync<RateLimitedException>(() => http.GetStringAsync(new Uri("https://a.test/1"), Ct));

        Assert.Equal(6, script.Requests.Count);
        Assert.Equal(_clock.Now + PoliteHttpClient.GiveUpPause, error.RetryAt);
        Assert.Equal(HttpStatusCode.TooManyRequests, error.StatusCode);
        Assert.Equal(PoliteHttpClient.MaxInterval, http.IntervalFor("a.test"));
    }

    [Fact]
    public async Task Speed_comes_back_after_a_run_of_successes()
    {
        using var http = Client(new Script(Sequence(() => TooMany(TimeSpan.FromSeconds(1)), Ok)));
        await http.GetStringAsync(new Uri("https://a.test/0"), Ct);
        Assert.Equal(TimeSpan.FromSeconds(3), http.IntervalFor("a.test"));

        for (var i = 1; i < 20; i++)
            await http.GetStringAsync(new Uri($"https://a.test/{i}"), Ct);
        Assert.Equal(TimeSpan.FromSeconds(2.25), http.IntervalFor("a.test"));

        for (var i = 0; i < 40; i++)
            await http.GetStringAsync(new Uri($"https://a.test/x{i}"), Ct);
        Assert.Equal(TimeSpan.FromSeconds(1.5), http.IntervalFor("a.test"));
    }

    [Fact]
    public async Task Each_site_has_its_own_pace_and_pause()
    {
        using var http = Client(new Script(r => r.RequestUri!.Host == "a.test" && r.RequestUri.AbsolutePath == "/1" ? TooMany(TimeSpan.FromMinutes(1)) : Ok()));
        var blocked = http.GetStringAsync(new Uri("https://a.test/1"), Ct);
        await Assert.ThrowsAsync<RateLimitedException>(() => blocked);
        _sleeps.Clear();

        await http.GetStringAsync(new Uri("https://b.test/1"), Ct);

        Assert.Empty(_sleeps);
        Assert.Equal(TimeSpan.FromSeconds(1.5), http.IntervalFor("b.test"));
    }

    [Fact]
    public async Task Server_errors_retry_briefly_and_client_errors_fail_at_once()
    {
        var failing = new Script(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError));
        using (var http = Client(failing))
            await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync(new Uri("https://a.test/1"), Ct));
        Assert.Equal(4, failing.Requests.Count);

        var missing = new Script(_ => new HttpResponseMessage(HttpStatusCode.NotFound));
        using (var http = Client(missing))
        {
            var error = await Assert.ThrowsAsync<HttpRequestException>(() => http.GetStringAsync(new Uri("https://a.test/1"), Ct));
            Assert.Equal(HttpStatusCode.NotFound, error.StatusCode);
        }
        Assert.Single(missing.Requests);
    }
}
