using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelShelf.App.Services;
using NovelShelf.Core.Comics;
using NovelShelf.Suwayomi;

namespace NovelShelf.App.ViewModels;

public sealed partial class ComicCardViewModel : ViewModelBase
{
    public ComicCardViewModel(ComicSummary summary, CoverLoader covers)
    {
        Summary = summary;
        _ = LoadCoverAsync(covers);
    }

    public ComicSummary Summary { get; }
    public string Title => Summary.Title;
    public string? Author => Summary.Author;

    [ObservableProperty]
    private Bitmap? _cover;

    private async Task LoadCoverAsync(CoverLoader covers) => Cover = await covers.LoadAsync(Summary.ThumbnailUrl);
}

public sealed partial class SourceCardViewModel : ViewModelBase
{
    public SourceCardViewModel(ComicSourceInfo source, CoverLoader covers)
    {
        Source = source;
        _ = LoadIconAsync(covers);
    }

    public ComicSourceInfo Source { get; }
    public string Name => Source.Name;
    public string Initial => Source.Name.Length > 0 ? Source.Name[..1].ToUpperInvariant() : "?";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasIcon))]
    private Bitmap? _icon;

    public bool HasIcon => Icon is not null;

    private async Task LoadIconAsync(CoverLoader covers) => Icon = await covers.LoadAsync(Source.IconUrl);
}

public sealed partial class ExtensionItemViewModel(SuwayomiExtension extension, Func<ExtensionItemViewModel, Task> act) : ViewModelBase
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Details), nameof(ActionText), nameof(IsInstalled))]
    private SuwayomiExtension _extension = extension;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ActCommand))]
    private bool _isBusy;

    public string Name => Extension.Name;
    public bool IsInstalled => Extension.IsInstalled;

    public string Details
    {
        get
        {
            var parts = new List<string> { Extension.Language, $"v{Extension.VersionName}" };
            if (Extension.ContentWarning == ContentWarning.Nsfw)
                parts.Add("adulto");
            else if (Extension.ContentWarning == ContentWarning.Mixed)
                parts.Add("pode ter conteúdo adulto");
            if (Extension.IsObsolete)
                parts.Add("obsoleta");
            return string.Join(" · ", parts);
        }
    }

    public string ActionText => !Extension.IsInstalled ? "Instalar" : Extension.HasUpdate ? "Atualizar" : "Remover";

    private bool CanAct() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct))]
    private Task ActAsync() => act(this);
}

public enum ComicsMode
{
    Sources,
    Browse,
    Manage,
}

public sealed partial class ComicsViewModel(AppServices services, Action<long> openInLibrary) : ViewModelBase
{
    private const int MaxExtensionRows = 300;
    private readonly List<SuwayomiExtension> _extensions = [];
    private ComicListing _listing;
    private int _page;
    private int _browseVersion;
    private string _lastQuery = "";

    public ObservableCollection<SourceCardViewModel> SourceCards { get; } = [];
    public ObservableCollection<ComicCardViewModel> Results { get; } = [];
    public ObservableCollection<ExtensionItemViewModel> Extensions { get; } = [];

    public static long DownloadSizeMb => SuwayomiInstaller.DownloadSize / 1_000_000;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsInstall))]
    private bool _isInstalled = services.Manga.IsInstalled;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(NeedsInstall))]
    [NotifyCanExecuteChangedFor(nameof(InstallCommand))]
    private bool _isInstalling;

    [ObservableProperty]
    private string? _installStep;

    [ObservableProperty]
    private double _installPercent;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(ShowSources), nameof(ShowBrowse), nameof(ShowManage))]
    private bool _isStarting;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsReady), nameof(ShowSources), nameof(ShowBrowse), nameof(ShowManage))]
    private bool _isRunning;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowSources), nameof(ShowBrowse), nameof(ShowManage), nameof(PageTitle))]
    private ComicsMode _mode;

    [ObservableProperty]
    private string? _error;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanBrowseLatest), nameof(PageTitle))]
    private ComicSourceInfo? _selectedSource;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand))]
    private string _query = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsPopular), nameof(IsLatest), nameof(ListingLabel))]
    private ComicListing _currentListing;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(SearchCommand), nameof(PopularCommand), nameof(LatestCommand), nameof(LoadMoreCommand))]
    private bool _isBusy;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(LoadMoreCommand))]
    private bool _hasMore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    private ComicCardViewModel? _selected;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasDetails))]
    [NotifyCanExecuteChangedFor(nameof(AddToLibraryCommand))]
    private ComicDetails? _details;

    [ObservableProperty]
    private string _extensionFilter = "";

    [ObservableProperty]
    private bool _allLanguages;

    [ObservableProperty]
    private bool _showAdult = services.Settings.ShowAdultSources;

    [ObservableProperty]
    private bool _isLoadingExtensions;

    public bool NeedsInstall => !IsInstalled;
    public bool IsReady => IsRunning && !IsStarting;
    public bool ShowSources => IsReady && Mode == ComicsMode.Sources;
    public bool ShowBrowse => IsReady && Mode == ComicsMode.Browse;
    public bool ShowManage => IsReady && Mode == ComicsMode.Manage;
    public bool HasSelection => Selected is not null;
    public bool HasDetails => Details is not null;
    public bool HasSources => SourceCards.Count > 0;
    public bool CanBrowseLatest => SelectedSource?.SupportsLatest == true;
    public bool IsPopular => CurrentListing == ComicListing.Popular;
    public bool IsLatest => CurrentListing == ComicListing.Latest;

    public string PageTitle => Mode switch
    {
        ComicsMode.Browse when SelectedSource is { } source => source.Name,
        ComicsMode.Manage => "Gerenciar fontes",
        _ => "Mangás, manhwas e HQs",
    };

    public string ListingLabel => CurrentListing switch
    {
        ComicListing.Latest => "Atualizados recentemente",
        ComicListing.Search => $"Resultados para “{_lastQuery}”",
        _ => "Populares",
    };

    public async Task ActivateAsync()
    {
        IsInstalled = services.Manga.IsInstalled;
        if (IsInstalled && !IsReady)
            await StartAsync();
    }

    private async Task StartAsync()
    {
        Error = null;
        IsStarting = true;
        try
        {
            await services.Manga.EnsureStartedAsync();
            IsRunning = true;
            await LoadSourcesAsync();
            if (SourceCards.Count == 0)
                await ManageSourcesAsync();
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            IsRunning = false;
            Error = e.Message;
        }
        finally
        {
            IsStarting = false;
        }
    }

    private bool CanInstall() => !IsInstalling;

    [RelayCommand(CanExecute = nameof(CanInstall), IncludeCancelCommand = true)]
    private async Task InstallAsync(CancellationToken ct)
    {
        Error = null;
        IsInstalling = true;
        try
        {
            var progress = new Progress<InstallProgress>(p =>
            {
                InstallStep = p.Total > 1
                    ? $"{p.Step}: {p.Done / 1_000_000} de {p.Total / 1_000_000} MB"
                    : p.Step;
                InstallPercent = p.Total > 0 ? 100.0 * p.Done / p.Total : 0;
            });
            await services.Manga.InstallAsync(progress, ct);
            IsInstalled = true;
            InstallStep = null;
        }
        catch (OperationCanceledException)
        {
            InstallStep = null;
            Error = "Instalação cancelada.";
        }
        catch (Exception e) when (e is HttpRequestException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Error = $"Não deu para instalar: {e.Message}";
        }
        finally
        {
            IsInstalling = false;
        }

        if (IsInstalled)
            await StartAsync();
    }

    [RelayCommand]
    private Task RetryAsync() => StartAsync();

    private async Task LoadSourcesAsync()
    {
        var sources = await services.Manga.Client.GetSourcesAsync();
        SourceCards.Clear();
        foreach (var source in sources.Where(s => ShowAdult || !s.IsNsfw))
            SourceCards.Add(new SourceCardViewModel(source, services.Covers));
        OnPropertyChanged(nameof(HasSources));
    }

    [RelayCommand]
    private Task OpenSourceAsync(SourceCardViewModel card)
    {
        SelectedSource = card.Source;
        Query = "";
        Mode = ComicsMode.Browse;
        return BrowseAsync(ComicListing.Popular, 1);
    }

    [RelayCommand]
    private void BackToSources()
    {
        _browseVersion++;
        IsBusy = false;
        Error = null;
        Results.Clear();
        Selected = null;
        HasMore = false;
        Mode = ComicsMode.Sources;
    }

    [RelayCommand]
    private async Task ManageSourcesAsync()
    {
        Mode = ComicsMode.Manage;
        if (_extensions.Count == 0)
            await LoadExtensionsAsync(refresh: true);
    }

    private bool CanBrowse() => SelectedSource is not null && !IsBusy;
    private bool CanSearch() => CanBrowse() && !string.IsNullOrWhiteSpace(Query);
    private bool CanLoadMore() => HasMore && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanSearch))]
    private Task SearchAsync() => BrowseAsync(ComicListing.Search, 1);

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private Task PopularAsync()
    {
        Query = "";
        return BrowseAsync(ComicListing.Popular, 1);
    }

    [RelayCommand(CanExecute = nameof(CanBrowse))]
    private Task LatestAsync()
    {
        Query = "";
        return BrowseAsync(ComicListing.Latest, 1);
    }

    [RelayCommand(CanExecute = nameof(CanLoadMore))]
    private Task LoadMoreAsync() => BrowseAsync(_listing, _page + 1);

    private async Task BrowseAsync(ComicListing listing, int page)
    {
        if (SelectedSource is not { } source)
            return;
        var version = ++_browseVersion;
        Error = null;
        IsBusy = true;
        CurrentListing = listing;
        _lastQuery = Query.Trim();
        OnPropertyChanged(nameof(ListingLabel));
        if (page == 1)
        {
            Results.Clear();
            Selected = null;
            HasMore = false;
        }
        try
        {
            var result = await services.Manga.Client.BrowseAsync(source.Id, listing, Query.Trim(), page);
            if (version != _browseVersion)
                return;
            var known = Results.Select(r => r.Summary.Key).ToHashSet();
            foreach (var item in result.Items.Where(i => known.Add(i.Key)))
                Results.Add(new ComicCardViewModel(item, services.Covers));
            _listing = listing;
            _page = page;
            HasMore = result.HasNextPage && result.Items.Count > 0;
            if (Results.Count == 0)
                Error = listing == ComicListing.Search ? "Nada encontrado com esse nome nesta fonte." : "A fonte não devolveu nenhum título.";
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            if (version == _browseVersion)
                Error = e.Message;
        }
        finally
        {
            if (version == _browseVersion)
                IsBusy = false;
        }
    }

    partial void OnSelectedChanged(ComicCardViewModel? value)
    {
        Details = null;
        if (value is not null)
            _ = LoadDetailsAsync(value);
    }

    private async Task LoadDetailsAsync(ComicCardViewModel card)
    {
        try
        {
            var details = await services.Manga.Client.GetComicAsync(card.Summary.SourceId, card.Summary.Key);
            if (Selected == card)
                Details = details;
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            if (Selected == card)
                Error = e.Message;
        }
    }

    [RelayCommand(CanExecute = nameof(HasDetails))]
    private async Task AddToLibraryAsync()
    {
        if (Details is not { } details)
            return;
        var comic = services.Library.UpsertComic(details);
        services.Covers.Forget(comic.Id);
        if (details.ThumbnailUrl is { } thumbnail)
        {
            try
            {
                services.Covers.Store(comic.Id, await services.Manga.Client.GetBytesAsync(thumbnail));
            }
            catch (Exception e) when (e is HttpRequestException or TaskCanceledException or IOException)
            {
            }
        }
        openInLibrary(comic.Id);
    }

    [RelayCommand]
    private Task RefreshExtensionsAsync() => LoadExtensionsAsync(refresh: true);

    private async Task LoadExtensionsAsync(bool refresh)
    {
        Error = null;
        IsLoadingExtensions = true;
        try
        {
            var list = await services.Manga.Client.GetExtensionsAsync(refresh);
            _extensions.Clear();
            _extensions.AddRange(list);
            ApplyExtensionFilter();
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            Error = e.Message;
        }
        finally
        {
            IsLoadingExtensions = false;
        }
    }

    partial void OnExtensionFilterChanged(string value) => ApplyExtensionFilter();
    partial void OnAllLanguagesChanged(bool value) => ApplyExtensionFilter();

    partial void OnShowAdultChanged(bool value)
    {
        services.Settings.ShowAdultSources = value;
        services.Settings.Save();
        ApplyExtensionFilter();
        if (IsReady)
            _ = LoadSourcesAsync();
    }

    private void ApplyExtensionFilter()
    {
        var filter = ExtensionFilter.Trim();
        var rows = _extensions
            .Where(e => AllLanguages || e.Language is "pt-BR" or "pt" or "all")
            .Where(e => ShowAdult || e.ContentWarning != ContentWarning.Nsfw)
            .Where(e => filter.Length == 0 || e.Name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            .OrderByDescending(e => e.IsInstalled)
            .ThenBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .Take(MaxExtensionRows);
        Extensions.Clear();
        foreach (var extension in rows)
            Extensions.Add(new ExtensionItemViewModel(extension, ToggleExtensionAsync));
    }

    private async Task ToggleExtensionAsync(ExtensionItemViewModel item)
    {
        var extension = item.Extension;
        item.IsBusy = true;
        Error = null;
        try
        {
            var updated = extension switch
            {
                { IsInstalled: false } => await services.Manga.Client.SetExtensionAsync(extension.PkgName, install: true),
                { HasUpdate: true } => await services.Manga.Client.SetExtensionAsync(extension.PkgName, update: true),
                _ => await services.Manga.Client.SetExtensionAsync(extension.PkgName, install: false),
            };
            item.Extension = updated;
            var index = _extensions.FindIndex(e => e.PkgName == updated.PkgName);
            if (index >= 0)
                _extensions[index] = updated;
            await LoadSourcesAsync();
        }
        catch (Exception e) when (e is InvalidOperationException or HttpRequestException or TaskCanceledException)
        {
            Error = e.Message;
        }
        finally
        {
            item.IsBusy = false;
        }
    }
}
