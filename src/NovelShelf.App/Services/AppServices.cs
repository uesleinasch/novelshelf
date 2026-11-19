using NovelShelf.Comics;
using NovelShelf.Core.Books;
using NovelShelf.Core.Comics;
using NovelShelf.Core.Downloading;
using NovelShelf.Core.Library;
using NovelShelf.Core.Net;
using NovelShelf.Core.Sources;
using NovelShelf.Devices;
using NovelShelf.Sources.NovelMania;
using NovelShelf.Suwayomi;

namespace NovelShelf.App.Services;

public sealed class AppServices
{
    public AppServices(AppPaths paths)
    {
        paths.EnsureCreated();
        Paths = paths;
        Settings = AppSettings.Load(paths.Settings);
        Http = new PoliteHttpClient(TimeSpan.FromSeconds(1.5));
        Sources = new SourceRegistry([new NovelManiaSource(Http)]);
        Library = new LibraryStore(paths.Database);
        Downloader = new ChapterDownloader(Library, Sources);
        Exporter = new BookExporter(Library, paths, Http);
        Locator = new KindleLocator();
        Sender = new KindleSender();
        Ejector = new KindleEjector();

        var mangaPaths = new SuwayomiPaths(Path.Combine(paths.Root, "suwayomi"));
        var downloads = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        downloads.DefaultRequestHeaders.UserAgent.ParseAdd("NovelShelf");
        var local = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
        var installer = new SuwayomiInstaller(mangaPaths, downloads);
        var server = new SuwayomiServer(mangaPaths, installer, local);
        var client = new SuwayomiClient(local, () => server.BaseUrl);
        Manga = new MangaBackend(installer, server, client);
        ComicDownloader = new ComicDownloader(Library, client, paths);
        ComicExporter = new ComicExporter(Library, paths, ComicDownloader);
        Covers = new CoverLoader(paths);
    }

    public AppPaths Paths { get; }
    public AppSettings Settings { get; }
    public PoliteHttpClient Http { get; }
    public SourceRegistry Sources { get; }
    public LibraryStore Library { get; }
    public ChapterDownloader Downloader { get; }
    public BookExporter Exporter { get; }
    public KindleLocator Locator { get; }
    public KindleSender Sender { get; }
    public KindleEjector Ejector { get; }
    public MangaBackend Manga { get; }
    public ComicDownloader ComicDownloader { get; }
    public ComicExporter ComicExporter { get; }
    public CoverLoader Covers { get; }

    public KindleProfile KindleProfile => KindleProfile.Get(Settings.KindleProfile);
}
