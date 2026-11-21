using System.Net;
using NovelShelf.Vision;

namespace NovelShelf.Tests.Vision;

public sealed class TextModelTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novelshelf-model-").FullName;

    [Fact]
    public async Task A_download_that_does_not_match_the_hash_is_thrown_away()
    {
        using var http = new HttpClient(new Fixed("não é um modelo"u8.ToArray()));
        var model = new TextModel(_dir, http);

        await Assert.ThrowsAsync<InvalidOperationException>(() => model.InstallAsync(ct: TestContext.Current.CancellationToken));

        Assert.False(model.IsInstalled);
        Assert.Null(model.Detector);
        Assert.Empty(Directory.GetFiles(_dir));
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    private sealed class Fixed(byte[] body) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(body) });
    }
}
