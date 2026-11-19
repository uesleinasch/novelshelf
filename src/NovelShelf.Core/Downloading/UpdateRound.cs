namespace NovelShelf.Core.Downloading;

public sealed record UpdateResult(long NovelId, string Title, int NewChapters);

public sealed class UpdateRound(IReadOnlyCollection<long> novelIds)
{
    private readonly HashSet<long> _ids = [.. novelIds];

    public int Count => _ids.Count;

    public bool IsFinished(IReadOnlyList<DownloadJob> jobs) =>
        jobs.Where(j => _ids.Contains(j.NovelId)).All(j => j.State is not (DownloadState.Queued or DownloadState.Running));

    public IReadOnlyList<UpdateResult> Results(IReadOnlyList<DownloadJob> jobs) =>
        jobs.Where(j => _ids.Contains(j.NovelId) && j.State == DownloadState.Done && j.Done > 0)
            .Select(j => new UpdateResult(j.NovelId, j.Title, j.Done))
            .ToList();

    public IReadOnlyList<DownloadJob> Failures(IReadOnlyList<DownloadJob> jobs) =>
        jobs.Where(j => _ids.Contains(j.NovelId) && j.State == DownloadState.Failed).ToList();
}
