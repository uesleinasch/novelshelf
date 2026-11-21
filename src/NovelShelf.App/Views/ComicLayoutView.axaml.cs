using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using FluentAvalonia.UI.Controls;
using NovelShelf.App.ViewModels;

namespace NovelShelf.App.Views;

public partial class ComicLayoutView : UserControl
{
    public ComicLayoutView()
    {
        InitializeComponent();
        DeleteShapeButton.Click += (_, _) => CutArea.DeleteSelected();
        foreach (var (button, shape) in new[] { (AddPanelButton, CutShape.Panel), (AddRowButton, CutShape.Row) })
        {
            button.Click += (_, _) => CutArea.Add(shape);
            button.AddHandler(PointerMovedEvent, (_, e) => DragShape(shape, e), handledEventsToo: true);
            button.AddHandler(PointerReleasedEvent, (_, e) => DropShape(shape, e), handledEventsToo: true);
        }
    }

    private void DragShape(CutShape shape, PointerEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            CutArea.ShowPreview(shape, e.GetPosition(CutArea));
    }

    private void DropShape(CutShape shape, PointerReleasedEventArgs e)
    {
        var point = e.GetPosition(CutArea);
        if (new Rect(CutArea.Bounds.Size).Contains(point))
            CutArea.Add(shape, point);
        else
            CutArea.HidePreview();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (DataContext is ComicLayoutViewModel vm)
            vm.Confirm = ConfirmAsync;
    }

    private void OnPreviewPressed(object? sender, Avalonia.Input.PointerPressedEventArgs e)
    {
        if (DataContext is ComicLayoutViewModel vm)
            vm.OpenCutEditorFromPreview();
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
