using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelShelf.App.Services;
using NovelShelf.Comics;
using NovelShelf.Core.Library;

namespace NovelShelf.App.ViewModels;

public sealed partial class ComicLayoutViewModel : ViewModelBase
{
    private readonly AppServices _services;
    private readonly LibraryNovel _comic;
    private readonly Action _close;
    private readonly Dictionary<string, IReadOnlyList<byte[]>> _sources = [];
    private IReadOnlyList<byte[]> _rendered = [];
    private CancellationTokenSource? _render;
    private bool _loading = true;

    public ComicLayoutViewModel(AppServices services, LibraryNovel comic, Action close)
    {
        _services = services;
        _comic = comic;
        _close = close;
        foreach (var chapter in services.Library.GetComicChapters(comic.Id))
            Chapters.Add(chapter);
        Load(services.Library.GetComicLayout(comic.Id));
        _rightToLeft = comic.RightToLeft;
        _loading = false;
        _chapter = Chapters.FirstOrDefault();
        ScheduleRender(immediate: true);
    }

    public string Title => _comic.Title;
    public string ProfileName => _services.KindleProfile.Name;
    public ObservableCollection<StoredComicChapter> Chapters { get; } = [];
    public bool HasChapters => Chapters.Count > 0;

    [ObservableProperty]
    private StoredComicChapter? _chapter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand), nameof(NextPageCommand))]
    private int _pageIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageLabel), nameof(LastPageIndex))]
    [NotifyCanExecuteChangedFor(nameof(PreviousPageCommand), nameof(NextPageCommand))]
    private int _pageCount;

    [ObservableProperty]
    private Bitmap? _preview;

    [ObservableProperty]
    private bool _isRendering;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _messageIsError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStripOptions), nameof(ShowPageOptions), nameof(DetectedLabel))]
    private bool _detectedStrip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStripOptions), nameof(ShowPageOptions))]
    private int _modeIndex;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowManualMargins))]
    private int _marginIndex;

    [ObservableProperty]
    private double _marginLeft;

    [ObservableProperty]
    private double _marginRight;

    [ObservableProperty]
    private double _marginTop;

    [ObservableProperty]
    private double _marginBottom;

    [ObservableProperty]
    private int _wideIndex;

    [ObservableProperty]
    private double _overlap;

    [ObservableProperty]
    private double _cutSearch;

    [ObservableProperty]
    private double _gamma;

    [ObservableProperty]
    private bool _rightToLeft;

    public int LastPageIndex => Math.Max(0, PageCount - 1);
    public string PageLabel => PageCount == 0 ? "Sem páginas" : $"Tela {PageIndex + 1} de {PageCount}";
    public bool UsesStrip => ModeIndex == 2 || (ModeIndex == 0 && DetectedStrip);
    public bool ShowStripOptions => UsesStrip;
    public bool ShowPageOptions => !UsesStrip;
    public bool ShowManualMargins => MarginIndex == 2;
    public string DetectedLabel => DetectedStrip
        ? "Este capítulo parece webtoon (tira contínua)."
        : "Este capítulo parece páginas de mangá ou HQ.";

    private ComicLayout Layout => new ComicLayout
    {
        Mode = (ComicReadingMode)ModeIndex,
        Margins = (MarginMode)MarginIndex,
        MarginLeft = MarginLeft / 100,
        MarginRight = MarginRight / 100,
        MarginTop = MarginTop / 100,
        MarginBottom = MarginBottom / 100,
        WidePages = (WidePageMode)WideIndex,
        Overlap = Overlap / 100,
        CutSearch = CutSearch / 100,
        Gamma = Gamma,
    }.Normalized();

    private void Load(ComicLayout layout)
    {
        ModeIndex = (int)layout.Mode;
        MarginIndex = (int)layout.Margins;
        MarginLeft = Math.Round(layout.MarginLeft * 100);
        MarginRight = Math.Round(layout.MarginRight * 100);
        MarginTop = Math.Round(layout.MarginTop * 100);
        MarginBottom = Math.Round(layout.MarginBottom * 100);
        WideIndex = (int)layout.WidePages;
        Overlap = Math.Round(layout.Overlap * 100);
        CutSearch = Math.Round(layout.CutSearch * 100);
        Gamma = Math.Round(layout.Gamma, 2);
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading)
            return;
        switch (e.PropertyName)
        {
            case nameof(Chapter):
            case nameof(ModeIndex):
            case nameof(MarginIndex):
            case nameof(MarginLeft):
            case nameof(MarginRight):
            case nameof(MarginTop):
            case nameof(MarginBottom):
            case nameof(WideIndex):
            case nameof(Overlap):
            case nameof(CutSearch):
            case nameof(Gamma):
            case nameof(RightToLeft):
                Message = null;
                ScheduleRender(immediate: e.PropertyName == nameof(Chapter));
                break;
            case nameof(PageIndex):
                ShowPage();
                break;
        }
    }

    private void ScheduleRender(bool immediate)
    {
        _render?.Cancel();
        _render = new CancellationTokenSource();
        _ = RenderAsync(_render.Token, immediate ? 0 : 350);
    }

    private async Task RenderAsync(CancellationToken ct, int delay)
    {
        if (Chapter is not { } chapter)
            return;
        try
        {
            if (delay > 0)
                await Task.Delay(delay, ct);
            IsRendering = true;
            var pages = await SourcePagesAsync(chapter, ct);
            DetectedStrip = ComicPageProcessor.IsWebtoon(pages);
            OnPropertyChanged(nameof(UsesStrip));
            var options = new ComicPageOptions(_services.KindleProfile, RightToLeft, Layout);
            var rendered = await Task.Run(() => ComicPageProcessor.ProcessChapter(pages, options), ct);
            ct.ThrowIfCancellationRequested();
            _rendered = rendered;
            PageCount = rendered.Count;
            if (PageIndex > LastPageIndex)
                PageIndex = LastPageIndex;
            ShowPage();
            IsRendering = false;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e) when (e is IOException or InvalidOperationException)
        {
            IsRendering = false;
            MessageIsError = true;
            Message = e.Message;
        }
    }

    private async Task<IReadOnlyList<byte[]>> SourcePagesAsync(StoredComicChapter chapter, CancellationToken ct)
    {
        if (_sources.TryGetValue(chapter.Key, out var cached))
            return cached;
        var files = _services.ComicDownloader.PageFiles(_comic.Id, chapter);
        var pages = await Task.WhenAll(files.Select(f => File.ReadAllBytesAsync(f, ct)));
        _sources.Clear();
        _sources[chapter.Key] = pages;
        return pages;
    }

    private void ShowPage()
    {
        var old = Preview;
        if (_rendered.Count == 0)
        {
            Preview = null;
        }
        else
        {
            using var stream = new MemoryStream(_rendered[Math.Clamp(PageIndex, 0, _rendered.Count - 1)]);
            Preview = new Bitmap(stream);
        }
        old?.Dispose();
    }

    private bool CanGoBack() => PageIndex > 0;
    private bool CanGoForward() => PageIndex < PageCount - 1;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void PreviousPage() => PageIndex--;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void NextPage() => PageIndex++;

    [RelayCommand]
    private void Save()
    {
        _services.Library.SetComicLayout(_comic.Id, Layout);
        _services.Library.SetRightToLeft(_comic.Id, RightToLeft);
        MessageIsError = false;
        Message = "Ajustes salvos para esta obra. Converter e Enviar já usam o novo enquadramento.";
    }

    [RelayCommand]
    private void Reset()
    {
        Load(ComicLayout.Default);
        Message = null;
    }

    [RelayCommand]
    private void Close()
    {
        _render?.Cancel();
        Preview?.Dispose();
        _close();
    }
}
