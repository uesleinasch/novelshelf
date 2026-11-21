using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelShelf.Comics;
using NovelShelf.Core.Library;

namespace NovelShelf.App.ViewModels;

public sealed partial class CutPageItem(int index, PageCut plan, bool isManual) : ObservableObject
{
    public int Index { get; } = index;
    public string Label => $"Página {Index + 1}";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(IsSuspect))]
    private PageCut _plan = plan;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Detail), nameof(IsSuspect))]
    private bool _isManual = isManual;

    public bool IsSuspect => !IsManual && Plan.IsSuspect;

    public string Detail => IsManual
        ? $"linhas manuais · {Screens(Plan.Screens.Count)}"
        : Plan.IsSuspect
            ? $"{Describe(Plan.Suspicion)} · confiança {Plan.Confidence:P0}"
            : Screens(Plan.Screens.Count);

    private static string Screens(int count) => count == 1 ? "1 tela" : $"{count} telas";

    private static string Describe(PageSuspicion suspicion)
    {
        var reasons = new List<string>();
        if (suspicion.HasFlag(PageSuspicion.NoClearGutter))
            reasons.Add("sem calha clara");
        if (suspicion.HasFlag(PageSuspicion.TinyPiece))
            reasons.Add("pedaço minúsculo");
        if (suspicion.HasFlag(PageSuspicion.HugePiece))
            reasons.Add("pedaço gigante");
        return string.Join(", ", reasons);
    }
}

public sealed partial class CutEditorViewModel : ViewModelBase
{
    private readonly LibraryStore _store;
    private readonly long _comicId;
    private readonly StoredComicChapter _chapter;
    private readonly IReadOnlyList<byte[]> _pages;
    private readonly ComicPageOptions _options;
    private readonly Action _close;
    private List<CutPageItem> _all = [];
    private bool _showingPage;

    public CutEditorViewModel(LibraryStore store, long comicId, StoredComicChapter chapter, IReadOnlyList<byte[]> pages,
        ComicPageOptions options, Action close)
    {
        _store = store;
        _comicId = comicId;
        _chapter = chapter;
        _pages = pages;
        _options = options;
        _close = close;
    }

    public string ChapterTitle => _chapter.Title;
    public ObservableCollection<CutPageItem> Visible { get; } = [];
    public bool Changed { get; private set; }

    [ObservableProperty]
    private bool _isLoading = true;

    [ObservableProperty]
    private bool _onlySuspect = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PageTitle))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand), nameof(NextCommand), nameof(ResetPageCommand))]
    private CutPageItem? _selected;

    [ObservableProperty]
    private Bitmap? _source;

    [ObservableProperty]
    private PageCuts? _cuts;

    [ObservableProperty]
    private string? _summary;

    public string PageTitle => Selected is { } item ? $"{item.Label} de {_pages.Count} — {item.Detail}" : "";

    public async Task LoadAsync(int? startPage)
    {
        var manual = _store.GetPageCuts(_comicId, _chapter.Key);
        var plans = await Task.Run(() => _pages
            .AsParallel().AsOrdered()
            .Select((page, i) => ComicPageProcessor.PlanLandscape(page, _options, manual.GetValueOrDefault(i)))
            .ToList());
        _all = plans.Select((plan, i) => plan is null ? null : new CutPageItem(i, plan, manual.ContainsKey(i)))
            .OfType<CutPageItem>()
            .ToList();
        IsLoading = false;
        if (startPage is { } page && _all.FirstOrDefault(i => i.Index == page) is { IsSuspect: false })
            OnlySuspect = false;
        else
            Refilter();
        Selected = (startPage is { } start ? Visible.FirstOrDefault(i => i.Index == start) : null) ?? Visible.FirstOrDefault();
    }

    partial void OnOnlySuspectChanged(bool value) => Refilter();

    private void Refilter()
    {
        var keep = Selected;
        var suspects = _all.Where(i => i.IsSuspect || i.IsManual).ToList();
        Visible.Clear();
        foreach (var item in OnlySuspect ? suspects : _all)
            Visible.Add(item);
        Summary = suspects.Count(i => i.IsSuspect) switch
        {
            0 => "Nenhuma página suspeita neste capítulo.",
            1 => "1 página suspeita neste capítulo.",
            var n => $"{n} páginas suspeitas neste capítulo.",
        };
        Selected = keep is not null && Visible.Contains(keep) ? keep : Visible.FirstOrDefault();
        PreviousCommand.NotifyCanExecuteChanged();
        NextCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedChanged(CutPageItem? value)
    {
        _showingPage = true;
        try
        {
            if (value is null)
            {
                Source = null;
                Cuts = null;
                return;
            }
            using (var stream = new MemoryStream(_pages[value.Index]))
                Source = new Bitmap(stream);
            Cuts = value.IsManual
                ? _store.GetPageCuts(_comicId, _chapter.Key).GetValueOrDefault(value.Index)
                : LinesFrom(value.Plan);
        }
        finally
        {
            _showingPage = false;
        }
    }

    partial void OnCutsChanged(PageCuts? value)
    {
        if (_showingPage || value is null || Selected is not { } item)
            return;
        _store.SetPageCuts(_comicId, _chapter.Key, item.Index, value);
        Changed = true;
        item.IsManual = true;
        _ = ReplanAsync(item, value);
    }

    private async Task ReplanAsync(CutPageItem item, PageCuts? cuts)
    {
        var page = _pages[item.Index];
        if (await Task.Run(() => ComicPageProcessor.PlanLandscape(page, _options, cuts)) is { } plan)
            item.Plan = plan;
        OnPropertyChanged(nameof(PageTitle));
    }

    private static PageCuts LinesFrom(PageCut plan) => new()
    {
        Rows = plan.Screens.Zip(plan.Screens.Skip(1))
            .Select(pair => plan.ToSourceFraction((pair.First.Bottom + pair.Second.Top) / 2))
            .ToList(),
    };

    private bool HasSelection() => Selected is not null;
    private bool CanGoBack() => Selected is not null && Visible.IndexOf(Selected) > 0;
    private bool CanGoForward() => Selected is not null && Visible.IndexOf(Selected) < Visible.Count - 1;

    [RelayCommand(CanExecute = nameof(CanGoBack))]
    private void Previous() => Selected = Visible[Visible.IndexOf(Selected!) - 1];

    [RelayCommand(CanExecute = nameof(CanGoForward))]
    private void Next() => Selected = Visible[Visible.IndexOf(Selected!) + 1];

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task ResetPageAsync()
    {
        var item = Selected!;
        _store.SetPageCuts(_comicId, _chapter.Key, item.Index, null);
        Changed = true;
        item.IsManual = false;
        await ReplanAsync(item, null);
        if (ReferenceEquals(Selected, item))
            OnSelectedChanged(item);
    }

    [RelayCommand]
    private void Close() => _close();
}
