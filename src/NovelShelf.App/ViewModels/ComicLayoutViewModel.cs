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
    private ComicLayout _saved;
    private bool _savedRightToLeft;
    private readonly Dictionary<string, IReadOnlyList<byte[]>> _sources = [];
    private IReadOnlyList<byte[]> _rendered = [];
    private IReadOnlyList<int> _screenPages = [];
    private CancellationTokenSource? _render;
    private bool _loading = true;

    public ComicLayoutViewModel(AppServices services, LibraryNovel comic, Action close)
    {
        _services = services;
        _comic = comic;
        _close = close;
        foreach (var chapter in services.Library.GetComicChapters(comic.Id))
            Chapters.Add(chapter);
        _saved = services.Library.GetComicLayout(comic.Id);
        Load(_saved);
        _rightToLeft = _savedRightToLeft = comic.RightToLeft;
        _loading = false;
        _chapter = Chapters.FirstOrDefault();
        RefreshCacheSize();
        ScheduleRender();
    }

    public string Title => _comic.Title;
    public string ProfileName => _services.KindleProfile.Name;
    public IReadOnlyList<KindleProfile> Profiles => KindleProfile.All;

    public KindleProfile Profile
    {
        get => _services.KindleProfile;
        set
        {
            if (value is null || value.Id == _services.Settings.KindleProfile)
                return;
            _services.Settings.KindleProfile = value.Id;
            _services.Settings.Save();
            ClearCache();
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProfileName));
            HasPendingChanges = true;
        }
    }
    public ObservableCollection<StoredComicChapter> Chapters { get; } = [];
    public bool HasChapters => Chapters.Count > 0;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(OpenCutEditorCommand))]
    private StoredComicChapter? _chapter;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEditingCuts))]
    [NotifyCanExecuteChangedFor(nameof(OpenCutEditorCommand))]
    private CutEditorViewModel? _cutEditor;

    public bool IsEditingCuts => CutEditor is not null;

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
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand), nameof(OpenCutEditorCommand))]
    private bool _isRendering;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ApplyCommand))]
    private bool _hasPendingChanges;

    [ObservableProperty]
    private string? _renderStatus;

    [ObservableProperty]
    private string? _message;

    [ObservableProperty]
    private bool _messageIsError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStripOptions), nameof(ShowPageOptions), nameof(DetectedLabel))]
    private bool _detectedStrip;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowStripOptions), nameof(ShowPageOptions), nameof(ShowLandscapeOptions), nameof(ShowVerticalMargins),
        nameof(ShowPanelOptions), nameof(ShowCutOptions))]
    [NotifyCanExecuteChangedFor(nameof(OpenCutEditorCommand))]
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

    [ObservableProperty]
    private bool _sharpen;

    [ObservableProperty]
    private int _turnIndex;

    [ObservableProperty]
    private double _tallBand;

    [ObservableProperty]
    private double _mergeBands;

    [ObservableProperty]
    private bool _pageBeforeBands;

    [ObservableProperty]
    private bool _avoidBubbles;

    [ObservableProperty]
    private string? _modelStatus;

    public string BubbleModelSize => $"{NovelShelf.Vision.TextModel.Size / 1_000_000.0:0} MB";

    partial void OnAvoidBubblesChanged(bool value)
    {
        if (value && !_loading && !_services.TextModel.IsInstalled)
            _ = InstallBubbleModelAsync();
    }

    private async Task InstallBubbleModelAsync()
    {
        try
        {
            ModelStatus = "Baixando o modelo de balões...";
            await _services.TextModel.InstallAsync(new Progress<NovelShelf.Vision.ModelProgress>(p =>
                ModelStatus = $"Baixando o modelo de balões: {p.Done / 1_000_000.0:0.0} de {p.Total / 1_000_000.0:0.0} MB"));
            ModelStatus = null;
            HasPendingChanges = true;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or InvalidOperationException or TaskCanceledException)
        {
            ModelStatus = null;
            AvoidBubbles = false;
            MessageIsError = true;
            Message = $"Não deu para baixar o modelo de balões: {e.Message}";
        }
    }

    private ComicPageOptions PreviewOptions() =>
        new(_services.KindleProfile, RightToLeft, Layout, TextDetector: _services.TextModel.Detector);

    public Func<string, string, Task<bool>>? Confirm { get; set; }

    public int LastPageIndex => Math.Max(0, PageCount - 1);
    public string PageLabel => PageCount == 0 ? "Sem páginas" : $"Tela {PageIndex + 1} de {PageCount}";
    public bool UsesStrip => ModeIndex == (int)ComicReadingMode.Strip || (ModeIndex == (int)ComicReadingMode.Auto && DetectedStrip);
    public bool ShowLandscapeOptions => ModeIndex == (int)ComicReadingMode.Landscape;
    public bool ShowStripOptions => UsesStrip;
    public bool ShowPanelOptions => ModeIndex == (int)ComicReadingMode.Panels;
    public bool ShowCutOptions => ShowLandscapeOptions || ShowPanelOptions;
    public bool ShowPageOptions => !UsesStrip && !ShowCutOptions;
    public bool ShowVerticalMargins => !UsesStrip;
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
        Sharpen = Sharpen,
        Turn = (LandscapeTurn)TurnIndex,
        TallBand = TallBand / 100,
        MergeBands = MergeBands / 100,
        PageBeforeBands = PageBeforeBands,
        AvoidBubbles = AvoidBubbles,
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
        Sharpen = layout.Sharpen;
        TurnIndex = (int)layout.Turn;
        TallBand = Math.Round(layout.TallBand * 100);
        MergeBands = Math.Round(layout.MergeBands * 100);
        PageBeforeBands = layout.PageBeforeBands;
        AvoidBubbles = layout.AvoidBubbles;
    }

    protected override void OnPropertyChanged(System.ComponentModel.PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (_loading)
            return;
        switch (e.PropertyName)
        {
            case nameof(Chapter):
                ScheduleRender();
                break;
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
            case nameof(Sharpen):
            case nameof(TurnIndex):
            case nameof(TallBand):
            case nameof(MergeBands):
            case nameof(PageBeforeBands):
            case nameof(AvoidBubbles):
                Message = null;
                HasPendingChanges = true;
                break;
            case nameof(PageIndex):
                ShowPage();
                break;
        }
    }

    private void ScheduleRender()
    {
        _render?.Cancel();
        _render = new CancellationTokenSource();
        _ = RenderAsync(_render.Token);
    }

    private bool CanApply() => HasPendingChanges && !IsRendering;

    [RelayCommand(CanExecute = nameof(CanApply))]
    private void Apply() => ScheduleRender();

    private async Task RenderAsync(CancellationToken ct)
    {
        if (Chapter is not { } chapter)
            return;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            IsRendering = true;
            HasPendingChanges = false;
            RenderStatus = "Gerando preview...";
            var pages = await SourcePagesAsync(chapter, ct);
            DetectedStrip = ComicPageProcessor.IsWebtoon(pages);
            OnPropertyChanged(nameof(UsesStrip));
            var options = PreviewOptions();
            var byScreen = options.EffectiveLayout.UsesPageCuts;
            var cuts = byScreen ? _services.Library.GetPageCuts(_comic.Id, chapter.Key) : null;
            var byPage = await Task.Run(() => byScreen
                ? ComicPageProcessor.ProcessChapterByPage(pages, options, cuts, ct)
                : [ComicPageProcessor.ProcessChapter(pages, options, ct)], ct);
            ct.ThrowIfCancellationRequested();
            _rendered = byPage.SelectMany(p => p).ToList();
            _screenPages = byScreen ? byPage.SelectMany((screens, page) => screens.Select(_ => page)).ToList() : [];
            var rendered = _rendered;
            PageCount = rendered.Count;
            if (PageIndex > LastPageIndex)
                PageIndex = LastPageIndex;
            ShowPage();
            IsRendering = false;
            RenderStatus = $"Preview gerado em {clock.Elapsed.TotalSeconds:0.0} s";
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception e)
        {
            if (ct.IsCancellationRequested)
                return;
            IsRendering = false;
            RenderStatus = null;
            MessageIsError = true;
            Message = $"Não deu para gerar o preview: {e.Message}";
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
        if (_rendered.Count == 0)
        {
            Preview = null;
            return;
        }
        using var stream = new MemoryStream(_rendered[Math.Clamp(PageIndex, 0, _rendered.Count - 1)]);
        Preview = new Bitmap(stream);
    }

    private bool CanGoBack() => PageIndex > 0;
    private bool CanGoForward() => PageIndex < PageCount - 1;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void PreviousPage() => PageIndex--;

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void NextPage() => PageIndex++;

    [RelayCommand]
    private async Task SaveAsync()
    {
        var layout = Layout;
        if (_comic.SentAt is not null && layout.Mode != _saved.Mode && Confirm is not null
            && !await Confirm("Trocar o formato de uma obra já enviada",
                "O número de telas muda e o marcador de leitura no Kindle vai parar em outro ponto do livro. " +
                "A obra fica marcada para reenviar com o formato novo."))
            return;
        if (HasPendingChanges)
            ScheduleRender();
        if (layout != _saved || RightToLeft != _savedRightToLeft)
            ClearCache();
        _services.Library.SetComicLayout(_comic.Id, layout);
        _saved = layout;
        _services.Library.SetRightToLeft(_comic.Id, RightToLeft);
        _savedRightToLeft = RightToLeft;
        MessageIsError = false;
        Message = "Ajustes salvos para esta obra. Converter e Enviar já usam o novo enquadramento.";
    }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ClearCacheCommand))]
    private long _cacheBytes;

    public string CacheSize => CacheBytes switch
    {
        0 => "Cache vazio. A próxima conversão processa todas as páginas.",
        < 1_000_000 => $"Cache: {CacheBytes / 1000.0:0} KB de páginas já convertidas.",
        _ => $"Cache: {CacheBytes / 1_000_000.0:0.0} MB de páginas já convertidas.",
    };

    partial void OnCacheBytesChanged(long value) => OnPropertyChanged(nameof(CacheSize));

    private void RefreshCacheSize() => CacheBytes = _services.ComicPageCache.SizeOf(_comic.Id);

    private bool HasCache() => CacheBytes > 0;

    [RelayCommand(CanExecute = nameof(HasCache))]
    private void ClearCache()
    {
        try
        {
            _services.ComicPageCache.Clear(_comic.Id);
        }
        catch (IOException)
        {
        }
        RefreshCacheSize();
    }

    private bool CanEditCuts() => ShowCutOptions && Chapter is not null && !IsRendering && CutEditor is null;

    [RelayCommand(CanExecute = nameof(CanEditCuts))]
    private async Task OpenCutEditorAsync()
    {
        var chapter = Chapter!;
        var pages = await SourcePagesAsync(chapter, CancellationToken.None);
        var options = PreviewOptions();
        int? start = PageIndex < _screenPages.Count ? _screenPages[PageIndex] : null;
        var editor = new CutEditorViewModel(_services.Library, _comic.Id, chapter, pages, options, CloseCutEditor);
        CutEditor = editor;
        await editor.LoadAsync(start);
    }

    public void OpenCutEditorFromPreview()
    {
        if (OpenCutEditorCommand.CanExecute(null))
            OpenCutEditorCommand.Execute(null);
    }

    private void CloseCutEditor()
    {
        var changed = CutEditor?.Changed == true;
        CutEditor = null;
        if (changed)
            ScheduleRender();
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
        _close();
    }
}
