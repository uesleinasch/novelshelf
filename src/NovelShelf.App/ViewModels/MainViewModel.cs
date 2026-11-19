using CommunityToolkit.Mvvm.ComponentModel;
using NovelShelf.App.Services;

namespace NovelShelf.App.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    private readonly AppServices _services;

    public MainViewModel(AppServices services)
    {
        _services = services;
        Kindle = new KindleViewModel(services, () => OpenBookStyle(null));
        Library = new LibraryViewModel(services, Kindle, OpenLayout);
        Downloads = new DownloadsViewModel(services);
        services.Downloads.Updated += Library.OnDownloadsUpdated;
        Search = new SearchViewModel(services, OpenInLibrary);
        Comics = new ComicsViewModel(services, OpenInLibrary);
        Library.Reload();
        _current = Library.HasNovels ? Library : Search;
        _ = Kindle.DetectAsync();
        services.Downloads.RestorePending();
    }

    public SearchViewModel Search { get; }
    public LibraryViewModel Library { get; }
    public KindleViewModel Kindle { get; }
    public ComicsViewModel Comics { get; }
    public DownloadsViewModel Downloads { get; }

    [ObservableProperty]
    private ViewModelBase _current;

    partial void OnCurrentChanged(ViewModelBase value)
    {
        if (value is ComicsViewModel comics)
            _ = comics.ActivateAsync();
    }

    private void OpenLayout(long novelId)
    {
        if (_services.Library.GetNovel(novelId) is not { } work)
            return;
        void Back()
        {
            Library.Reload(novelId);
            Current = Library;
        }
        Current = work.Kind == Core.Library.WorkKind.Comic
            ? new ComicLayoutViewModel(_services, work, Back)
            : OpenBookStyle(work, Back);
    }

    private BookStyleViewModel OpenBookStyle(Core.Library.LibraryNovel? novel, Action? back = null)
    {
        var page = new BookStyleViewModel(_services, novel, back ?? (() =>
        {
            Library.Reload();
            Current = Kindle;
        }));
        Current = page;
        return page;
    }

    private void OpenInLibrary(long novelId)
    {
        Library.Reload(novelId);
        Current = Library;
    }
}
