using CommunityToolkit.Mvvm.ComponentModel;
using NovelShelf.App.Services;

namespace NovelShelf.App.ViewModels;

public sealed partial class MainViewModel : ViewModelBase
{
    public MainViewModel(AppServices services)
    {
        Kindle = new KindleViewModel(services);
        Library = new LibraryViewModel(services, Kindle);
        Search = new SearchViewModel(services, OpenInLibrary);
        Comics = new ComicsViewModel(services, OpenInLibrary);
        Library.Reload();
        _current = Library.HasNovels ? Library : Search;
        _ = Kindle.DetectAsync();
    }

    public SearchViewModel Search { get; }
    public LibraryViewModel Library { get; }
    public KindleViewModel Kindle { get; }
    public ComicsViewModel Comics { get; }

    [ObservableProperty]
    private ViewModelBase _current;

    partial void OnCurrentChanged(ViewModelBase value)
    {
        if (value is ComicsViewModel comics)
            _ = comics.ActivateAsync();
    }

    private void OpenInLibrary(long novelId)
    {
        Library.Reload(novelId);
        Current = Library;
    }
}
