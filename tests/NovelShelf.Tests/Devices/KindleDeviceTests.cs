using NovelShelf.Devices;

namespace NovelShelf.Tests.Devices;

public sealed class KindleDeviceTests : IDisposable
{
    private readonly string _root = Directory.CreateTempSubdirectory("novelshelf-kindle-").FullName;

    [Fact]
    public void FindGvfsMtp_detects_kindle_storage_with_documents_folder()
    {
        var uid = Environment.GetEnvironmentVariable("UID") ?? File.ReadLines("/proc/self/status")
            .First(l => l.StartsWith("Uid:")).Split('\t', StringSplitOptions.RemoveEmptyEntries)[1];
        var gvfs = $"/run/user/{uid}/gvfs";
        var mount = $"{gvfs}/mtp:host=Amazon_Kindle_G000XX";
        var storage = $"{mount}/Internal Storage";
        var tree = new Dictionary<string, string[]>
        {
            [gvfs] = [mount, $"{gvfs}/smb-share:server=nas"],
            [mount] = [storage],
            [storage] = [$"{storage}/documents", $"{storage}/system"],
        };
        var locator = new KindleLocator(listDirectories: p => tree.GetValueOrDefault(p, []));

        var device = Assert.Single(locator.FindGvfsMtp());

        Assert.Equal(KindleConnection.Mtp, device.Connection);
        Assert.Equal(storage, device.RootPath);
        Assert.Equal("Amazon Kindle", device.Name);
    }

    [Fact]
    public async Task SendAsync_to_mass_storage_overwrites_same_file_and_writes_thumbnail()
    {
        var ct = TestContext.Current.CancellationToken;
        Directory.CreateDirectory(Path.Combine(_root, "Kindle", "documents"));
        Directory.CreateDirectory(Path.Combine(_root, "Kindle", "system", "thumbnails"));
        var device = new KindleDevice("Kindle", Path.Combine(_root, "Kindle"), KindleConnection.MassStorage);
        var book = Path.Combine(_root, "book.azw3");
        var sender = new KindleSender();

        await File.WriteAllTextAsync(book, "v1", ct);
        await sender.SendAsync(device, new SendRequest(book, "novelmania-x.azw3", null, null), ct);
        await File.WriteAllTextAsync(book, "v2", ct);
        var result = await sender.SendAsync(device, new SendRequest(book, "novelmania-x.azw3", [1, 2, 3], "thumbnail_x_EBOK_portrait.jpg"), ct);

        Assert.Equal("v2", await File.ReadAllTextAsync(result.TargetPath, ct));
        Assert.Single(Directory.GetFiles(Path.Combine(device.DocumentsPath, KindleSender.Folder)));
        Assert.True(result.ThumbnailWritten);
        Assert.Equal([1, 2, 3], await File.ReadAllBytesAsync(Path.Combine(device.ThumbnailsPath, "thumbnail_x_EBOK_portrait.jpg"), ct));
    }

    [Fact]
    public async Task SendAsync_over_mtp_removes_existing_file_before_gio_copy()
    {
        var ct = TestContext.Current.CancellationToken;
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Fluxo gio só existe no Linux.");
        var storage = Path.Combine(_root, "mtp");
        var target = Path.Combine(storage, "documents", KindleSender.Folder, "b.azw3");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        await File.WriteAllTextAsync(target, "old", ct);
        var runner = new RecordingRunner();

        await new KindleSender(runner).SendAsync(
            new KindleDevice("Kindle", storage, KindleConnection.Mtp),
            new SendRequest("/tmp/b.azw3", "b.azw3", null, null), ct);

        Assert.Equal(["remove " + target, "copy /tmp/b.azw3 " + target], runner.Calls);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);

    private sealed class RecordingRunner : IProcessRunner
    {
        public List<string> Calls { get; } = [];

        public Task<(int ExitCode, string Output)> RunAsync(string file, IReadOnlyList<string> args, CancellationToken ct = default)
        {
            Calls.Add(string.Join(' ', args));
            return Task.FromResult((0, ""));
        }
    }
}
