using System.ComponentModel;
using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
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
        {
            _viewModel.PropertyChanged -= OnViewModelChanged;
            _viewModel.ConfirmResend = null;
        }
        _viewModel = DataContext as LibraryViewModel;
        if (_viewModel is not null)
        {
            _viewModel.PropertyChanged += OnViewModelChanged;
            _viewModel.ConfirmResend = ConfirmResendAsync;
        }
        ApplyLayout();
    }

    private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(LibraryViewModel.IsGridView))
            ApplyLayout();
    }

    private void ApplyLayout() =>
        Root.ColumnDefinitions = ColumnDefinitions.Parse(_viewModel?.IsGridView == true ? "*,440" : "320,*");

    private async Task<bool> ConfirmResendAsync(ResendViewModel resend)
    {
        var dialog = new FAContentDialog
        {
            Title = "Reenviar ao Kindle",
            Content = new ResendView { DataContext = resend },
            PrimaryButtonText = "Marcar para reenviar",
            CloseButtonText = "Cancelar",
            DefaultButton = FAContentDialogButton.Primary,
            IsPrimaryButtonEnabled = resend.CanApply,
        };
        void Sync(object? sender, PropertyChangedEventArgs e) => dialog.IsPrimaryButtonEnabled = resend.CanApply;
        resend.PropertyChanged += Sync;
        try
        {
            return await dialog.ShowAsync(TopLevel.GetTopLevel(this)) == FAContentDialogResult.Primary;
        }
        finally
        {
            resend.PropertyChanged -= Sync;
        }
    }
}
