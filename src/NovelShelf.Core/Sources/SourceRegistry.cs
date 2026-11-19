namespace NovelShelf.Core.Sources;

public sealed class SourceRegistry(IEnumerable<INovelSource> sources)
{
    private readonly IReadOnlyList<INovelSource> _sources = sources.ToList();

    public IReadOnlyList<INovelSource> All => _sources;

    public INovelSource Get(string id) =>
        _sources.FirstOrDefault(s => s.Id == id)
        ?? throw new KeyNotFoundException($"Fonte '{id}' não registrada.");

    public INovelSource? FindFor(Uri url) => _sources.FirstOrDefault(s => s.CanHandle(url));
}
