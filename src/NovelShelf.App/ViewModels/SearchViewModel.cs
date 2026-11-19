using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelShelf.App.Services;
using NovelShelf.Core.Sources;

namespace NovelShelf.App.ViewModels;

public sealed partial class NovelCardViewModel : ViewModelBase
{
    public NovelCardViewModel(NovelSummary summary, CoverLoader covers)
    {
        Summary = summary;
        _ = LoadCoverAsync(covers);
    }

    public NovelSummary Summary { get; }
    public string Title => Summary.Title;
    public string? Author => Summary.Author;

    [ObservableProperty]
    private Bitmap? _cover;

    private async Task LoadCoverAsync(CoverLoader covers) => Cover = await covers.LoadAsync(Summary.CoverUrl);
}

public sealed partial class SearchViewModel(AppServices services, Action<long> openInLibrary) : ViewModelBase
{
    public ObservableCollection<NovelCardViewModel> Results { get; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _query = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private NovelCardViewModel? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    private NovelDetails? _details;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string? _error;

    public bool HasSelection => Selected is not null;
    public bool HasDetails => Details is not null;

    public INovelSource Source => services.Sources.All[0];

    private bool CanSearch() => !string.IsNullOrWhiteSpace(Query);

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private async Task SearchAsync(CancellationToken ct)
    {
        Error = null;
        IsBusy = true;
        try
        {
            if (Uri.TryCreate(Query.Trim(), UriKind.Absolute, out var url) && services.Sources.FindFor(url) is { } source)
            {
                var details = await source.GetNovelAsync(url, ct);
                Results.Clear();
                var card = new NovelCardViewModel(new NovelSummary(details.SourceId, details.Url, details.Title, details.Author, details.CoverUrl), services.Covers);
                Results.Add(card);
                Selected = card;
                return;
            }

            var found = await Source.SearchAsync(Query, ct);
            Results.Clear();
            foreach (var summary in found)
                Results.Add(new NovelCardViewModel(summary, services.Covers));
            if (found.Count == 0)
                Error = "Nenhuma novel encontrada.";
        }
        catch (Exception e) when (e is HttpRequestException or FormatException or ArgumentException)
        {
            Error = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    partial void OnSelectedChanged(NovelCardViewModel? value)
    {
        Details = null;
        if (value is not null)
            _ = LoadDetailsAsync(value);
    }

    private async Task LoadDetailsAsync(NovelCardViewModel card)
    {
        try
        {
            var source = services.Sources.Get(card.Summary.SourceId);
            var details = await source.GetNovelAsync(card.Summary.Url);
            if (Selected == card)
                Details = details;
        }
        catch (Exception e) when (e is HttpRequestException or FormatException)
        {
            Error = e.Message;
        }
    }

    [RelayCommand]
    private void AddToLibrary()
    {
        if (Details is null)
            return;
        var novel = services.Library.Upsert(Details);
        openInLibrary(novel.Id);
    }
}
