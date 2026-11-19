using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NovelShelf.App.Services;
using NovelShelf.Core.Downloading;

namespace NovelShelf.App.ViewModels;

public sealed partial class DownloadJobViewModel(DownloadJob job, DownloadsViewModel owner) : ViewModelBase
{
    public DownloadsViewModel Owner { get; } = owner;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StateText), nameof(CanPause), nameof(CanResume), nameof(IsRunning), nameof(Maximum), nameof(Value))]
    private DownloadJob _job = job;

    public string Title => Job.Title;
    public long NovelId => Job.NovelId;
    public bool CanPause => Job.State is DownloadState.Queued or DownloadState.Running;
    public bool CanResume => Job.State is DownloadState.Paused or DownloadState.Failed;
    public bool IsRunning => Job.State == DownloadState.Running;
    public double Maximum => Math.Max(1, Job.Requested);
    public double Value => Job.Done;
    public string StateText => DownloadText.Describe(Job);
}

public static class DownloadText
{
    public static string Describe(DownloadJob job) => job.State switch
    {
        DownloadState.Queued when job.Done > 0 => $"Na fila — {job.Done} de {job.Requested} capítulos",
        DownloadState.Queued => $"Na fila — {job.Requested} capítulos",
        DownloadState.Running => $"Baixando {job.Done} de {job.Requested}" + (job.Detail is null ? "" : $" — {job.Detail}"),
        DownloadState.Paused => $"Pausado em {job.Done} de {job.Requested}",
        DownloadState.Done when job.ReachedEnd => $"Concluído: {job.Done} capítulos. Chegou ao último publicado.",
        DownloadState.Done => $"Concluído: {job.Done} capítulos.",
        _ => $"Erro: {job.Detail}",
    };
}

public sealed partial class DownloadsViewModel : ViewModelBase
{
    private readonly DownloadQueue _queue;

    public DownloadsViewModel(AppServices services)
    {
        _queue = services.Downloads.Queue;
        services.Downloads.Updated += Sync;
        Sync();
    }

    public ObservableCollection<DownloadJobViewModel> Items { get; } = [];

    public int ActiveCount => Items.Count(i => i.Job.State is DownloadState.Queued or DownloadState.Running);
    public bool HasItems => Items.Count > 0;
    public string NavTitle => ActiveCount > 0 ? $"Downloads ({ActiveCount})" : "Downloads";

    private void Sync()
    {
        var jobs = _queue.Jobs;
        for (var i = Items.Count - 1; i >= 0; i--)
            if (jobs.All(j => j.NovelId != Items[i].NovelId))
                Items.RemoveAt(i);
        for (var i = 0; i < jobs.Count; i++)
        {
            var current = Items.Select((item, index) => (item, index)).FirstOrDefault(x => x.item.NovelId == jobs[i].NovelId);
            if (current.item is null)
            {
                Items.Insert(i, new DownloadJobViewModel(jobs[i], this));
                continue;
            }
            current.item.Job = jobs[i];
            if (current.index != i)
                Items.Move(current.index, i);
        }
        OnPropertyChanged(nameof(ActiveCount));
        OnPropertyChanged(nameof(HasItems));
        OnPropertyChanged(nameof(NavTitle));
    }

    [RelayCommand]
    private void PauseAll() => _queue.PauseAll();

    [RelayCommand]
    private void ResumeAll() => _queue.ResumeAll();

    [RelayCommand]
    private void ClearFinished() => _queue.ClearFinished();

    [RelayCommand]
    private void Pause(DownloadJobViewModel item) => _queue.Pause(item.NovelId);

    [RelayCommand]
    private void Resume(DownloadJobViewModel item) => _queue.Resume(item.NovelId);

    [RelayCommand]
    private void Remove(DownloadJobViewModel item) => _queue.Remove(item.NovelId);

    [RelayCommand]
    private void MoveUp(DownloadJobViewModel item) => _queue.Move(item.NovelId, -1);

    [RelayCommand]
    private void MoveDown(DownloadJobViewModel item) => _queue.Move(item.NovelId, 1);
}
