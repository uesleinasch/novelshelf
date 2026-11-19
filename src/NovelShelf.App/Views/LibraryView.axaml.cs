using System.ComponentModel;
using Avalonia.Controls;
using NovelShelf.App.ViewModels;

namespace NovelShelf.App.Views;

public partial class LibraryView : UserControl
{
    private LibraryViewModel? _viewModel;

    public LibraryView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelChanged;
        _viewModel = DataContext as LibraryViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelChanged;
        ApplyLayout();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.IsGridView))
            ApplyLayout();
    }

    private void ApplyLayout() =>
        Root.ColumnDefinitions = ColumnDefinitions.Parse(_viewModel?.IsGridView == true ? "*,440" : "320,*");
}
