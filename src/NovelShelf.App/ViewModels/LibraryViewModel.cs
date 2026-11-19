using System.Collections.ObjectModel;
using System.Diagnostics;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelShelf.App.Services;
using NovelShelf.Core.Books;
using NovelShelf.Core.Downloading;
using NovelShelf.Core.Library;
using NovelShelf.Devices;

namespace NovelShelf.App.ViewModels;

public sealed partial class LibraryItemViewModel : ViewModelBase
{
    public LibraryItemViewModel(LibraryNovel novel, CoverLoader covers)
    {
        _novel = novel;
        _ = LoadCoverAsync(covers);
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Progress), nameof(KindleStatus), nameof(PendingForKindle))]
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
            : $"Kindle em dia ({Novel.SentChapterCount} capítulos)";

    private async Task LoadCoverAsync(CoverLoader covers) => Cover = await covers.LoadAsync(Novel.CoverUrl, Novel.Id);
}

public sealed partial class LibraryViewModel(AppServices services, KindleViewModel kindle) : ViewModelBase
{
    public ObservableCollection<LibraryItemViewModel> Novels { get; } = [];
    public KindleViewModel Kindle => kindle;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(SendToKindleCommand), nameof(ExportCommand), nameof(RemoveCommand))]
    private LibraryItemViewModel? _selected;

    [ObservableProperty]
    private double _chaptersToDownload = 50;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(DownloadCommand), nameof(SendToKindleCommand), nameof(ExportCommand), nameof(RemoveCommand))]
    private bool _isBusy;

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
    public bool HasNovels => Novels.Count > 0;

    public void Reload(long? select = null)
    {
        var keep = select ?? Selected?.Novel.Id;
        Novels.Clear();
        foreach (var novel in services.Library.GetNovels())
            Novels.Add(new LibraryItemViewModel(novel, services.Covers));
        Selected = Novels.FirstOrDefault(n => n.Novel.Id == keep) ?? Novels.FirstOrDefault();
        OnPropertyChanged(nameof(HasNovels));
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
            var novel = await services.Sources.Get(item.Novel.SourceId).GetNovelAsync(item.Novel.Url, ct);
            services.Library.Upsert(novel);
            var result = await services.Downloader.DownloadNextAsync(item.Novel.Id, count, progress, ct);
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
        await RunAsync("Gerando livro", async () =>
        {
            var book = await services.Exporter.ExportAsync(item.Novel.Id);
            Success("Livro gerado", $"EPUB e AZW3 com {book.ChapterCount} capítulos em {services.Paths.Books}");
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
            var book = await services.Exporter.ExportAsync(item.Novel.Id);
            var result = await services.Sender.SendAsync(device,
                new SendRequest(book.Azw3Path, book.KindleFileName, book.CoverJpeg, book.ThumbnailFileName));
            services.Library.MarkSent(item.Novel.Id, book.ChapterCount);
            Refresh(item);
            Success("Enviado", $"{book.ChapterCount} capítulos em {device.Name}: {result.TargetPath}. Ejete o Kindle para ele atualizar a biblioteca.");
        });
    }

    [RelayCommand]
    private void OpenBooksFolder()
    {
        Process.Start(new ProcessStartInfo(services.Paths.Books) { UseShellExecute = true });
    }

    [RelayCommand(CanExecute = nameof(CanAct))]
    private void Remove()
    {
        services.Library.DeleteNovel(Selected!.Novel.Id);
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
