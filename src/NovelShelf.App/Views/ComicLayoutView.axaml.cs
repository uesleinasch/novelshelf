using Avalonia.Controls;
using FluentAvalonia.UI.Controls;
using NovelShelf.App.ViewModels;

namespace NovelShelf.App.Views;

public partial class ComicLayoutView : UserControl
{
    public ComicLayoutView() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ComicLayoutViewModel vm)
            vm.Confirm = ConfirmAsync;
    }

    private async Task<bool> ConfirmAsync(string title, string message)
    {
        var dialog = new FAContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap, MaxWidth = 420 },
            PrimaryButtonText = "Salvar mesmo assim",
            CloseButtonText = "Cancelar",
            DefaultButton = FAContentDialogButton.Close,
        };
        return await dialog.ShowAsync(TopLevel.GetTopLevel(this)) == FAContentDialogResult.Primary;
    }
}
