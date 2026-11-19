using System.ComponentModel;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using NovelShelf.App.ViewModels;

namespace NovelShelf.App.Views;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        Nav.SelectionChanged += OnNavigationChanged;
        DataContextChanged += (_, _) =>
        {
            if (DataContext is MainViewModel vm)
            {
                vm.PropertyChanged += OnViewModelChanged;
                SyncSelection(vm);
            }
        };
    }

    private MainViewModel? ViewModel => DataContext as MainViewModel;

    private void OnNavigationChanged(object? sender, FANavigationViewSelectionChangedEventArgs e)
    {
        if (ViewModel is not { } vm || e.SelectedItem is not FANavigationViewItem item)
            return;
        vm.Current = item.Tag switch
        {
            "search" => vm.Search,
            "comics" => vm.Comics,
            "downloads" => vm.Downloads,
            "kindle" => vm.Kindle,
            _ => vm.Library,
        };
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.Current) && ViewModel is { } vm)
            SyncSelection(vm);
    }

    private void SyncSelection(MainViewModel vm)
    {
        var tag = vm.Current switch
        {
            SearchViewModel => "search",
            ComicsViewModel => "comics",
            DownloadsViewModel => "downloads",
            KindleViewModel or BookStyleViewModel { IsForNovel: false } => "kindle",
            _ => "library",
        };
        var item = Nav.MenuItems.Concat(Nav.FooterMenuItems)
            .OfType<FANavigationViewItem>()
            .FirstOrDefault(i => (string?)i.Tag == tag);
        if (item is not null && !ReferenceEquals(Nav.SelectedItem, item))
            Nav.SelectedItem = item;
    }
}
