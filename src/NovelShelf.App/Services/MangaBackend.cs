using NovelShelf.Suwayomi;

namespace NovelShelf.App.Services;

public sealed class MangaBackend(SuwayomiInstaller installer, SuwayomiServer server, SuwayomiClient client)
{
    private Task? _starting;

    public SuwayomiServer Server => server;
    public SuwayomiClient Client => client;
    public bool IsInstalled => installer.IsInstalled;
    public bool IsReady => server.State == SuwayomiState.Running && _starting is null;

    public Task InstallAsync(IProgress<InstallProgress> progress, CancellationToken ct) => installer.InstallAsync(progress, ct);

    public Task EnsureStartedAsync()
    {
        if (server.State == SuwayomiState.Running && _starting is null)
            return Task.CompletedTask;
        return _starting ??= StartAsync();
    }

    private async Task StartAsync()
    {
        try
        {
            await server.StartAsync();
            await client.EnsureNoxStoreAsync();
        }
        finally
        {
            _starting = null;
        }
    }
}
