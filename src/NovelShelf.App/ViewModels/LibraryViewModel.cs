using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelShelf.App.Services;
using NovelShelf.Core.Books;
using NovelShelf.Core.Comics;
using NovelShelf.Core.Downloading;
using NovelShelf.Core.Library;
using NovelShelf.Devices;

namespace NovelShelf.App.ViewModels;

public sealed partial class LibraryItemViewModel : ViewModelBase
{
    private readonly LibraryStore _store;

    public LibraryItemViewModel(LibraryNovel novel, CoverLoader covers, LibraryStore store, LibraryViewModel owner)
    {
        Owner = owner;
        SiteName = owner.SiteNameFor(novel);
        _novel = novel;
        _store = store;
        _rightToLeft = novel.RightToLeft;
        _ = LoadCoverAsync(covers);
    }

    public LibraryViewModel Owner { get; }
    public string SiteName { get; }

    public bool IsComic => Novel.Kind == WorkKind.Comic;

    [ObservableProperty]
    private bool _rightToLeft;

    partial void OnRightToLeftChanged(bool value) => _store.SetRightToLeft(Novel.Id, value);

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Progress), nameof(KindleStatus), nameof(KindleBadge), nameof(PendingForKindle))]
    private LibraryNovel _novel;

    [ObservableProperty]
    private Bitmap? _cover;

    public string Progress => Novel.RemoteChapterCount is { } total
        ? $"{Novel.DownloadedChapters} de {total} capítulos baixados"
        : $"{Novel.DownloadedChapters} capítulos baixados";

    public int PendingForKindle => Math.Max(0, Novel.DownloadedChapters - Novel.SentChapterCount);

    public string KindleStatus => Novel.SentAt is null
        ? "Ainda não enviado ao Kindle"
        : PendingForKindle > 0
            ? $"Kindle tem {Novel.SentChapterCount} capítulos — {PendingForKindle} novos para enviar"
            : Novel.NeedsResend
                ? $"Kindle tem {Novel.SentChapterCount} capítulos — envie de novo para aplicar os ajustes"
                : $"Kindle em dia ({Novel.SentChapterCount} capítulos)";

    public string ExportMenuHeader => IsComic ? "Converter para Kindle" : "Gerar livro";

    public string KindleBadge => Novel.SentAt is null
        ? "Não enviado"
        : PendingForKindle > 0
            ? $"{PendingForKindle} novos para o Kindle"
            : Novel.NeedsResend
                ? "Ajustes novos para o Kindle"
                : "Kindle em dia";

    private async Task LoadCoverAsync(CoverLoader covers) => Cover = await covers.LoadAsync(Novel.CoverUrl, Novel.Id);
}

public sealed partial class LibraryViewModel(AppServices services, KindleViewModel kindle, Action<long> openLayout) : ViewModelBase
{
    private List<LibraryItemViewModel> _all = [];
    private bool _rebuildingSites;

    public ObservableCollection<LibraryItemViewModel> Novels { get; } = [];
    public KindleViewModel Kindle => kindle;

    public static IReadOnlyList<Choice<WorkKind?>> KindOptions { get; } =
    [
        new("Todos os tipos", null),
        new("Novels", WorkKind.Novel),
        new("Mangás e HQs", WorkKind.Comic),
    ];

    public static IReadOnlyList<Choice<KindleState?>> KindleOptions { get; } =
    [
        new("Qualquer situação", null),
        new("Novos para enviar", KindleState.Pending),
        new("Kindle em dia", KindleState.UpToDate),
        new("Nunca enviados", KindleState.NeverSent),
    ];

    public static IReadOnlyList<Choice<LibrarySort>> SortOptions { get; } =
    [
        new("Atualizados", LibrarySort.Updated),
        new("Título (A–Z)", LibrarySort.Title),
        new("Adicionados", LibrarySort.Added),
    ];

    public ObservableCollection<Choice<string?>> SiteOptions { get; } = [new("Todos os sites", null)];

    [ObservableProperty]
    private Choice<WorkKind?>? _kindFilter = KindOptions[0];

    [ObservableProperty]
    private Choice<string?>? _siteFilter;

    [ObservableProperty]
    private Choice<KindleState?>? _kindleFilter = KindleOptions[0];

    [ObservableProperty]
    private Choice<LibrarySort>? _sortOrder = SortOptions[0];

    [ObservableProperty]
    private string _searchText = "";

    partial void OnKindFilterChanged(Choice<WorkKind?>? value) => ApplyFilter();
    partial void OnSiteFilterChanged(Choice<string?>? value)
    {
        if (!_rebuildingSites)
            ApplyFilter();
    }
    partial void OnKindleFilterChanged(Choice<KindleState?>? value) => ApplyFilter();
    partial void OnSortOrderChanged(Choice<LibrarySort>? value) => ApplyFilter();
    partial void OnSearchTextChanged(string value) => ApplyFilter();

    private LibraryFilter CurrentFilter => new(KindFilter?.Value, SiteFilter?.Value, KindleFilter?.Value, SearchText,
        SortOrder?.Value ?? LibrarySort.Updated);

    public bool IsFiltered => CurrentFilter.IsActive;
    public bool HasNoMatches => _all.Count > 0 && Novels.Count == 0;

    [RelayCommand]
    private void ClearFilters()
    {
        KindFilter = KindOptions[0];
        SiteFilter = SiteOptions[0];
        KindleFilter = KindleOptions[0];
        SearchText = "";
    }

    public string SiteNameFor(LibraryNovel novel) => novel.Kind == WorkKind.Comic
        ? novel.SourceName ?? novel.SourceId
        : services.Sources.All.FirstOrDefault(s => s.Id == novel.SourceId)?.Name ?? novel.SourceId;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection), nameof(DownloadDescription), nameof(ExportHeader), nameof(ExportDescription), nameof(ExportButton))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(SendToKindleCommand), nameof(ExportCommand), nameof(RemoveCommand), nameof(AdjustLayoutCommand))]
    private LibraryItemViewModel? _selected;

    public string DownloadDescription => Selected?.IsComic == true
        ? "Baixa as páginas dos próximos capítulos para o PC."
        : "Continua do último capítulo salvo no PC.";

    public string ExportHeader => Selected?.IsComic == true ? "Converter para Kindle" : "Arquivos no PC";

    public string ExportDescription => Selected?.IsComic == true
        ? $"Corta as margens e ajusta as páginas à tela do {services.KindleProfile.Name}. Troque o modelo na página Kindle."
        : "Gera EPUB e AZW3 com todos os capítulos baixados.";

    public string ExportButton => Selected?.IsComic == true ? "Converter" : "Gerar livro";

    [ObservableProperty]
    private double _chaptersToDownload = 50;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(SendToKindleCommand), nameof(ExportCommand), nameof(RemoveCommand), nameof(EjectKindleCommand), nameof(AdjustLayoutCommand))]
    [NotifyCanExecuteChangedFor(nameof(DownloadItemCommand), nameof(SendItemToKindleCommand), nameof(ExportItemCommand), nameof(RemoveItemCommand))]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isGridView = services.Settings.LibraryGrid;

    partial void OnIsGridViewChanged(bool value)
    {
        services.Settings.LibraryGrid = value;
        services.Settings.Save();
    }

    [RelayCommand]
    private void ShowList() => IsGridView = false;

    [RelayCommand]
    private void ShowGrid() => IsGridView = true;

    [ObservableProperty]
    private double _progressValue;

    [ObservableProperty]
    private double _progressMaximum = 1;

    [ObservableProperty]
    private string? _statusTitle;

    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _statusIsError;

    public bool HasSelection => Selected is not null;
    public bool HasNovels => _all.Count > 0;

    public void Reload(long? select = null)
    {
        var keep = select ?? Selected?.Novel.Id;
        _all = services.Library.GetNovels()
            .Select(novel => new LibraryItemViewModel(novel, services.Covers, services.Library, this))
            .ToList();
        RebuildSites();
        if (select is not null && _all.FirstOrDefault(n => n.Novel.Id == select) is { } wanted
            && CurrentFilter.Apply([wanted], i => i.Novel, i => i.SiteName).Count == 0)
            ClearFilters();
        ApplyFilter(keep);
        OnPropertyChanged(nameof(HasNovels));
    }

    private void RebuildSites()
    {
        var current = SiteFilter?.Value;
        _rebuildingSites = true;
        try
        {
            SiteOptions.Clear();
            SiteOptions.Add(new Choice<string?>("Todos os sites", null));
            foreach (var site in _all.Select(i => i.SiteName).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase))
                SiteOptions.Add(new Choice<string?>(site, site));
            SiteFilter = SiteOptions.FirstOrDefault(o => o.Value == current) ?? SiteOptions[0];
        }
        finally
        {
            _rebuildingSites = false;
        }
    }

    private void ApplyFilter(long? keep = null)
    {
        keep ??= Selected?.Novel.Id;
        var visible = CurrentFilter.Apply(_all, i => i.Novel, i => i.SiteName);
        Novels.Clear();
        foreach (var item in visible)
            Novels.Add(item);
        Selected = Novels.FirstOrDefault(n => n.Novel.Id == keep) ?? Novels.FirstOrDefault();
        OnPropertyChanged(nameof(HasNoMatches));
        OnPropertyChanged(nameof(IsFiltered));
    }

    private bool CanAct() => Selected is not null && !IsBusy;

    [RelayCommand(CanExecute = nameof(CanAct), IncludeCancelCommand = true)]
    private async Task DownloadAsync(CancellationToken ct)
    {
        var item = Selected!;
        var count = Math.Max(1, (int)ChaptersToDownload);
        await RunAsync("Baixando capítulos", async () =>
        {
            ProgressMaximum = count;
            ProgressValue = 0;
            var progress = new Progress<DownloadProgress>(p =>
            {
                ProgressValue = p.Done;
                StatusMessage = $"{p.Done}/{p.Requested} — {p.ChapterTitle}";
            });
            DownloadResult result;
            if (item.IsComic)
            {
                StatusMessage = "Conectando à fonte...";
                await services.Manga.EnsureStartedAsync();
                services.Library.UpsertComic(await services.Manga.Client.GetComicAsync(item.Novel.SourceId, item.Novel.RemoteKey!, ct));
                result = await services.ComicDownloader.DownloadNextAsync(item.Novel.Id, count, progress, ct);
            }
            else
            {
                var novel = await services.Sources.Get(item.Novel.SourceId).GetNovelAsync(item.Novel.Url, ct);
                services.Library.Upsert(novel);
                result = await services.Downloader.DownloadNextAsync(item.Novel.Id, count, progress, ct);
            }
            Refresh(item);
            Success("Download concluído", result.ReachedEnd
                ? $"{result.Downloaded} capítulos baixados. Você chegou ao último capítulo publicado."
                : $"{result.Downloaded} capítulos baixados.");
        });
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task ExportAsync()
    {
        var item = Selected!;
        await RunAsync(item.IsComic ? "Convertendo para o Kindle" : "Gerando livro", async () =>
        {
            var book = await ExportBookAsync(item);
            Success(item.IsComic ? "Convertido" : "Livro gerado", book.EpubPath is null
                ? $"AZW3 com {book.ChapterCount} capítulos em {services.Paths.Books}"
                : $"EPUB e AZW3 com {book.ChapterCount} capítulos em {services.Paths.Books}");
        });
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private async Task SendToKindleAsync()
    {
        var item = Selected!;
        await RunAsync("Enviando para o Kindle", async () =>
        {
            var device = await kindle.EnsureDeviceAsync()
                ?? throw new InvalidOperationException("Nenhum Kindle encontrado. Conecte o cabo USB e desbloqueie a tela do Kindle.");
            var book = await ExportBookAsync(item);
            StatusMessage = $"Copiando para {device.Name}...";
            var result = await services.Sender.SendAsync(device,
                new SendRequest(book.Azw3Path, book.KindleFileName, book.CoverJpeg, book.ThumbnailFileName));
            services.Library.MarkSent(item.Novel.Id, book.ChapterCount);
            Refresh(item);
            Success("Enviado", $"{book.ChapterCount} capítulos em {device.Name}: {result.TargetPath}. Ejete o Kindle para ele atualizar a biblioteca.");
        });
    }

    private async Task<ExportedBook> ExportBookAsync(LibraryItemViewModel item)
    {
        if (!item.IsComic)
            return await services.Exporter.ExportAsync(item.Novel.Id, services.Settings.BookStyle);

        var progress = new Progress<ComicExportProgress>(p =>
        {
            ProgressMaximum = Math.Max(1, p.Total);
            ProgressValue = p.Done;
            StatusMessage = p.Done < p.Total ? $"Ajustando páginas: {p.Done + 1}/{p.Total} — {p.ChapterTitle}" : "Montando o AZW3...";
        });
        return await services.ComicExporter.ExportAsync(item.Novel.Id, services.KindleProfile, progress);
    }

    private bool CanEject() => !IsBusy;

    [RelayCommand(CanExecute = nameof(CanEject))]
    private Task DownloadItemAsync(LibraryItemViewModel item) => RunOnAsync(item, DownloadCommand);

    [RelayCommand(CanExecute = nameof(CanEject))]
    private Task SendItemToKindleAsync(LibraryItemViewModel item) => RunOnAsync(item, SendToKindleCommand);

    [RelayCommand(CanExecute = nameof(CanEject))]
    private Task ExportItemAsync(LibraryItemViewModel item) => RunOnAsync(item, ExportCommand);

    [RelayCommand(CanExecute = nameof(CanEject))]
    private void RemoveItem(LibraryItemViewModel item)
    {
        Selected = item;
        if (RemoveCommand.CanExecute(null))
            RemoveCommand.Execute(null);
    }

    private async Task RunOnAsync(LibraryItemViewModel item, IAsyncRelayCommand command)
    {
        Selected = item;
        if (command.CanExecute(null))
            await command.ExecuteAsync(null);
    }

    [RelayCommand(CanExecute = nameof(CanEject))]
    private async Task EjectKindleAsync()
    {
        await RunAsync("Ejetando o Kindle", async () =>
        {
            if (!kindle.IsConnected)
                await kindle.DetectAsync();
            Success("Kindle ejetado", await kindle.EjectSelectedAsync());
        });
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void AdjustLayout() => openLayout(Selected!.Novel.Id);

    [RelayCommand]
    private void OpenBooksFolder()
    {
        Process.Start(new ProcessStartInfo(services.Paths.Books) { UseShellExecute = true });
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Remove()
    {
        var item = Selected!;
        services.Library.DeleteNovel(item.Novel.Id);
        if (item.IsComic && Directory.Exists(services.ComicDownloader.ChapterRoot(item.Novel.Id)))
            Directory.Delete(services.ComicDownloader.ChapterRoot(item.Novel.Id), recursive: true);
        Reload();
    }

    private void Refresh(LibraryItemViewModel item)
    {
        if (services.Library.GetNovel(item.Novel.Id) is { } updated)
            item.Novel = updated;
    }

    private async Task RunAsync(string title, Func<Task> action)
    {
        IsBusy = true;
        StatusIsError = false;
        StatusTitle = title;
        StatusMessage = "";
        try
        {
            await action();
        }
        catch (OperationCanceledException)
        {
            Refresh(Selected!);
            Success("Cancelado", "Os capítulos já baixados continuam salvos.");
        }
        catch (Exception e) when (e is HttpRequestException or InvalidOperationException or IOException or UnauthorizedAccessException or FormatException)
        {
            StatusIsError = true;
            StatusTitle = "Erro";
            StatusMessage = e.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void Success(string title, string message)
    {
        StatusIsError = false;
        StatusTitle = title;
        StatusMessage = message;
    }
}
