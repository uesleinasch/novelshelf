using Avalonia.Controls;
using Avalonia.Controls.Templates;
using NovelShelf.App.ViewModels;
using NovelShelf.App.Views;

namespace NovelShelf.App;

public sealed class ViewLocator : IDataTemplate
{
    public Control? Build(object? param) => param switch
    {
        SearchViewModel => new SearchView(),
        LibraryViewModel => new LibraryView(),
        KindleViewModel => new KindleView(),
        ComicsViewModel => new ComicsView(),
        ComicLayoutViewModel => new ComicLayoutView(),
        _ => new TextBlock { Text = $"Sem view para {param?.GetType().Name}" },
    };

    public bool Match(object? data) => data is ViewModelBase;
}
