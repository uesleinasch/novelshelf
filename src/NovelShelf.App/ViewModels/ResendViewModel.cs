using CommunityToolkit.Mvvm.ComponentModel;
using NovelShelf.Core.Books;
using NovelShelf.Core.Library;

namespace NovelShelf.App.ViewModels;

public sealed partial class ResendVolume(VolumeSpec volume) : ObservableObject
{
    public VolumeSpec Volume { get; } = volume;
    public string Label => $"Vol. {Volume.Number:D2} (cap. {Volume.First + 1}–{Volume.Through})";

    [ObservableProperty]
    private bool _isChecked;
}

public sealed partial class ResendViewModel : ViewModelBase
{
    private readonly IReadOnlyList<VolumeSpec> _plan;

    public ResendViewModel(LibraryNovel work)
    {
        Work = work;
        _plan = Volumes.Split(work.DownloadedChapters, work.VolumeSize);
        VolumeOptions = _plan.Count > 1 ? _plan.Select(v => new ResendVolume(v)).ToList() : [];
        foreach (var option in VolumeOptions)
            option.PropertyChanged += (_, _) => OnPropertyChanged(nameof(CanApply));
        _toChapter = Math.Max(1, work.DownloadedChapters);
    }

    public LibraryNovel Work { get; }
    public double ChapterCount => Math.Max(1, Work.DownloadedChapters);
    public IReadOnlyList<ResendVolume> VolumeOptions { get; }
    public bool HasVolumes => VolumeOptions.Count > 0;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    private bool _everything = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply), nameof(ChaptersHint))]
    private bool _byChapters;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply))]
    private bool _byVolumes;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply), nameof(ChaptersHint))]
    private double _fromChapter = 1;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanApply), nameof(ChaptersHint))]
    private double _toChapter;

    private ChapterRange? Chapters =>
        double.IsNaN(FromChapter) || double.IsNaN(ToChapter) || FromChapter < 1 || ToChapter < FromChapter || ToChapter > Work.DownloadedChapters
            ? null
            : ChapterRange.FromChapters((int)FromChapter, (int)ToChapter);

    public string ChaptersHint
    {
        get
        {
            if (Chapters is not { } range)
                return $"Escolha capítulos entre 1 e {Work.DownloadedChapters}.";
            if (_plan[0].IsWholeWork || _plan.Count == 1)
                return "A obra não está dividida em volumes: o livro inteiro vai de novo.";
            var volumes = Volumes.Changed(_plan, int.MaxValue, everything: false, [range]);
            return volumes.Count == 1
                ? $"Vai de novo o volume {volumes[0].Number}."
                : $"Vão de novo os volumes {string.Join(", ", volumes.Select(v => v.Number))}.";
        }
    }

    public bool CanApply =>
        Everything
        || (ByChapters && Chapters is not null)
        || (ByVolumes && VolumeOptions.Any(v => v.IsChecked));

    public string Apply(LibraryStore store)
    {
        if (ByChapters && Chapters is { } range)
        {
            store.MarkResend(Work.Id, [range]);
            return $"Capítulos {range.First + 1}–{range.Through}";
        }
        if (ByVolumes && VolumeOptions.Where(v => v.IsChecked).ToList() is { Count: > 0 } chosen)
        {
            store.MarkResend(Work.Id, chosen.Select(v => v.Volume.Range));
            return chosen.Count == 1
                ? $"Volume {chosen[0].Volume.Number}"
                : $"Volumes {string.Join(", ", chosen.Select(v => v.Volume.Number))}";
        }
        store.MarkResendAll(Work.Id);
        return "A obra inteira";
    }
}
