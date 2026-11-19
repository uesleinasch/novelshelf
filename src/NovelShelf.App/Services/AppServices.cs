using NovelShelf.Core.Books;
using NovelShelf.Core.Downloading;
using NovelShelf.Core.Library;
using NovelShelf.Core.Net;
using NovelShelf.Core.Sources;
using NovelShelf.Devices;
using NovelShelf.Sources.NovelMania;

namespace NovelShelf.App.Services;

public sealed class AppServices
{
    public AppServices(AppPaths paths)
    {
        paths.EnsureCreated();
        Paths = paths;
        Http = new PoliteHttpClient(TimeSpan.FromSeconds(1.5));
        Sources = new SourceRegistry([new NovelManiaSource(Http)]);
        Library = new LibraryStore(paths.Database);
        Downloader = new ChapterDownloader(Library, Sources);
        Exporter = new BookExporter(Library, paths, Http);
        Locator = new KindleLocator();
        Sender = new KindleSender();
        Covers = new CoverLoader(paths);
    }

    public AppPaths Paths { get; }
    public PoliteHttpClient Http { get; }
    public SourceRegistry Sources { get; }
    public LibraryStore Library { get; }
    public ChapterDownloader Downloader { get; }
    public BookExporter Exporter { get; }
    public KindleLocator Locator { get; }
    public KindleSender Sender { get; }
    public CoverLoader Covers { get; }
}
