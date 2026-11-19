using System.Text;
using NovelShelf.Core.Books;
using NovelShelf.Core.Library;
using NovelShelf.Core.Net;
using NovelShelf.Core.Sources;
using NovelShelf.Devices;

namespace NovelShelf.Tests.Library;

public sealed class VolumeTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory("novelshelf-volumes-").FullName;

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Split_keeps_whole_work_when_off_and_cuts_fixed_size_volumes()
    {
        Assert.Equal([new VolumeSpec(0, 0, 250)], Volumes.Split(250, 0));
        Assert.Equal([new VolumeSpec(1, 0, 80)], Volumes.Split(80, 100));
        Assert.Equal([new VolumeSpec(1, 0, 100), new VolumeSpec(2, 100, 100), new VolumeSpec(3, 200, 50)], Volumes.Split(250, 100));
        Assert.Equal([new VolumeSpec(1, 0, 100), new VolumeSpec(2, 100, 100)], Volumes.Split(200, 100));
    }

    [Fact]
    public void Only_volumes_with_unsent_chapters_change()
    {
        var plan = Volumes.Split(250, 100);

        Assert.Equal([3], Volumes.Changed(plan, 220, everything: false).Select(v => v.Number));
        Assert.Equal([3], Volumes.Changed(plan, 200, everything: false).Select(v => v.Number));
        Assert.Equal([2, 3], Volumes.Changed(plan, 150, everything: false).Select(v => v.Number));
        Assert.Empty(Volumes.Changed(plan, 250, everything: false));
        Assert.Equal(3, Volumes.Changed(plan, 250, everything: true).Count);
    }

    [Fact]
    public void Names_titles_and_ids_are_stable_per_volume()
    {
        var whole = new VolumeSpec(0, 0, 10);
        var third = new VolumeSpec(3, 200, 50);
        const string bookId = "0123456789abcdef0123456789abcdef";

        Assert.Equal("novelmania-x.azw3", Volumes.FileName("novelmania-x", whole));
        Assert.Equal("novelmania-x-vol03.azw3", Volumes.FileName("novelmania-x", third));
        Assert.Equal("Obra", Volumes.Title("Obra", whole));
        Assert.Equal("Obra — Vol. 03 (cap. 201–250)", Volumes.Title("Obra", third));
        Assert.Equal("urn:uuid:01234567-89ab-cdef-0123-456789abcdef", Volumes.Identifier(Volumes.BookKey(bookId, whole)));
        Assert.Equal(Volumes.Identifier(Volumes.BookKey(bookId, third)), Volumes.Identifier(Volumes.BookKey(bookId, third)));
        Assert.NotEqual(Volumes.Identifier(Volumes.BookKey(bookId, third)), Volumes.Identifier(Volumes.BookKey(bookId, new VolumeSpec(2, 100, 100))));
        Assert.NotEqual(BookExporter.UniqueIdFor(Volumes.BookKey(bookId, third)), BookExporter.UniqueIdFor(bookId));
    }

    [Theory]
    [InlineData("novelmania-x.azw3", true)]
    [InlineData("novelmania-x-vol01.azw3", true)]
    [InlineData("novelmania-x-vol12.azw3", true)]
    [InlineData("novelmania-x-volume.azw3", false)]
    [InlineData("novelmania-x-vol.azw3", false)]
    [InlineData("novelmania-xy.azw3", false)]
    [InlineData("novelmania-x-2.azw3", false)]
    [InlineData("novelmania-x.epub", false)]
    public void Recognizes_files_of_a_work(string file, bool expected)
    {
        Assert.Equal(expected, Volumes.IsFileOf("novelmania-x", file));
    }

    [Fact]
    public async Task Exporter_writes_each_volume_with_its_chapters()
    {
        var ct = TestContext.Current.CancellationToken;
        var paths = new AppPaths(_dir);
        paths.EnsureCreated();
        var store = new LibraryStore(paths.Database);
        var novel = store.Upsert(new NovelDetails("fake", "longa", new Uri("https://fake.test/longa"), "Longa", null, null, null, [], null, 5, null));
        for (var i = 0; i < 5; i++)
            store.AddChapter(novel.Id, i, new ChapterPage(new Uri($"https://fake.test/c/{i}"), $"Cap {i + 1}", $"<p>texto {i + 1}</p>", null));
        store.SetVolumeSize(novel.Id, 2);
        using var http = new PoliteHttpClient(TimeSpan.Zero);
        var exporter = new BookExporter(store, paths, http);

        var all = await exporter.ExportVolumesAsync(novel.Id, ct: ct);
        var changed = await exporter.ExportVolumesAsync(novel.Id, select: plan => Volumes.Changed(plan, 4, everything: false), ct: ct);

        Assert.Equal(["fake-longa-vol01.azw3", "fake-longa-vol02.azw3", "fake-longa-vol03.azw3"], all.Books.Select(b => b.KindleFileName));
        Assert.Equal([2, 2, 1], all.Books.Select(b => b.ChapterCount));
        Assert.Equal(5, all.TotalChapters);
        Assert.All(all.Books, b => Assert.True(File.Exists(b.Azw3Path) && File.Exists(b.EpubPath)));
        Assert.Equal([3], changed.Books.Select(b => b.Volume));
        Assert.Contains("Vol. 03 (cap. 5–5)", Encoding.UTF8.GetString(await File.ReadAllBytesAsync(changed.Books[0].Azw3Path, ct)));
    }

    [Fact]
    public void Changing_volume_size_of_a_sent_work_needs_resend_until_sent()
    {
        var store = new LibraryStore(Path.Combine(_dir, "library.db"));
        var novel = store.Upsert(new NovelDetails("fake", "a", new Uri("https://fake.test/a"), "A", null, null, null, [], null, 3, null));
        store.MarkSent(novel.Id, 0);
        Assert.False(store.GetNovel(novel.Id)!.VolumeLayoutChanged);

        store.SetVolumeSize(novel.Id, 100);
        var changed = store.GetNovel(novel.Id)!;
        Assert.True(changed.VolumeLayoutChanged);
        Assert.Equal(KindleState.Pending, LibraryFilter.KindleStateOf(changed));

        store.MarkSent(novel.Id, 0);
        var sent = store.GetNovel(novel.Id)!;
        Assert.Equal(100, sent.SentVolumeSize);
        Assert.False(sent.VolumeLayoutChanged);
    }

    [Fact]
    public async Task Sender_removes_only_matching_books_from_its_folder()
    {
        var device = new KindleDevice("Kindle", Path.Combine(_dir, "Kindle"), KindleConnection.MassStorage);
        var folder = Path.Combine(device.DocumentsPath, KindleSender.Folder);
        Directory.CreateDirectory(folder);
        foreach (var name in new[] { "fake-a.azw3", "fake-a-vol01.azw3", "fake-ab.azw3", "outro.azw3" })
            await File.WriteAllTextAsync(Path.Combine(folder, name), "x", TestContext.Current.CancellationToken);

        var removed = await new KindleSender().RemoveAsync(device,
            name => Volumes.IsFileOf("fake-a", name) && name != "fake-a-vol01.azw3", TestContext.Current.CancellationToken);

        Assert.Equal(["fake-a.azw3"], removed);
        Assert.Equal(["fake-a-vol01.azw3", "fake-ab.azw3", "outro.azw3"], Directory.GetFiles(folder).Select(Path.GetFileName).Order());
    }
}
