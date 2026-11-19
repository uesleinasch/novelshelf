using System.Net.Http.Json;
using System.Text.Json.Nodes;
using NovelShelf.Core.Comics;

namespace NovelShelf.Suwayomi;

public enum ContentWarning
{
    Safe,
    Mixed,
    Nsfw,
}

public sealed record SuwayomiExtension(
    string PkgName,
    string Name,
    string Language,
    string VersionName,
    bool IsInstalled,
    bool HasUpdate,
    bool IsObsolete,
    ContentWarning ContentWarning,
    Uri? IconUrl);

public sealed class SuwayomiClient(HttpClient http, Func<Uri?> baseUrl) : IComicProvider
{
    private const string ExtensionFields = "pkgName name lang versionName isInstalled hasUpdate isObsolete contentWarning iconUrl";

    private Uri Base => baseUrl() ?? throw new InvalidOperationException("O servidor de mangás não está rodando.");

    public async Task EnsureNoxStoreAsync(CancellationToken ct = default)
    {
        var data = await GraphAsync("{ extensionStores { nodes { indexUrl } } }", null, ct);
        var stores = data["extensionStores"]!["nodes"]!.AsArray()
            .Select(n => (string?)n!["indexUrl"] ?? "");
        if (stores.Any(s => s.StartsWith(SuwayomiRelease.NoxRepositoryPrefix, StringComparison.Ordinal)))
            return;
        await GraphAsync("mutation($url: String!) { addExtensionStore(input: { indexUrl: $url }) { clientMutationId } }",
            new { url = SuwayomiRelease.NoxIndexUrl }, ct);
    }

    public async Task<IReadOnlyList<SuwayomiExtension>> GetExtensionsAsync(bool refresh, CancellationToken ct = default)
    {
        var nodes = refresh
            ? (await GraphAsync($"mutation {{ fetchExtensions(input: {{}}) {{ extensions {{ {ExtensionFields} }} }} }}", null, ct))
                ["fetchExtensions"]!["extensions"]!.AsArray()
            : (await GraphAsync($"{{ extensions {{ nodes {{ {ExtensionFields} }} }} }}", null, ct))
                ["extensions"]!["nodes"]!.AsArray();
        return nodes.Select(n => ParseExtension(n!)).OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public async Task<SuwayomiExtension> SetExtensionAsync(string pkgName, bool? install = null, bool update = false, CancellationToken ct = default)
    {
        var patch = new JsonObject();
        if (install == true)
            patch["install"] = true;
        if (install == false)
            patch["uninstall"] = true;
        if (update)
            patch["update"] = true;
        var data = await GraphAsync(
            $"mutation($id: String!, $patch: UpdateExtensionPatchInput!) {{ updateExtension(input: {{ id: $id, patch: $patch }}) {{ extension {{ {ExtensionFields} }} }} }}",
            new JsonObject { ["id"] = pkgName, ["patch"] = patch }, ct);
        return ParseExtension(data["updateExtension"]!["extension"]!);
    }

    public async Task<IReadOnlyList<ComicSourceInfo>> GetSourcesAsync(CancellationToken ct = default)
    {
        var data = await GraphAsync("{ sources { nodes { id name displayName lang contentWarning supportsLatest iconUrl } } }", null, ct);
        return data["sources"]!["nodes"]!.AsArray()
            .Select(n => n!)
            .Where(n => (string?)n["id"] != "0")
            .Select(n => new ComicSourceInfo(
                (string)n["id"]!,
                (string?)n["name"] ?? (string)n["displayName"]!,
                (string?)n["lang"] ?? "",
                ParseWarning((string?)n["contentWarning"]) == ContentWarning.Nsfw,
                (bool?)n["supportsLatest"] ?? false,
                Absolute((string?)n["iconUrl"])))
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<ComicBrowsePage> BrowseAsync(string sourceId, ComicListing listing, string? query, int page, CancellationToken ct = default)
    {
        var type = listing switch
        {
            ComicListing.Latest => "LATEST",
            ComicListing.Search => "SEARCH",
            _ => "POPULAR",
        };
        var data = await GraphAsync("""
            mutation($source: LongString!, $type: FetchSourceMangaType!, $page: Int!, $query: String) {
              fetchSourceManga(input: { source: $source, type: $type, page: $page, query: $query }) {
                hasNextPage
                mangas { id url title author thumbnailUrl }
              }
            }
            """, new { source = sourceId, type, page, query = listing == ComicListing.Search ? query : null }, ct);
        var result = data["fetchSourceManga"]!;
        var items = result["mangas"]!.AsArray()
            .Select(n => new ComicSummary(sourceId, (string)n!["url"]!, (string)n["title"]!, (string?)n["author"], Absolute((string?)n["thumbnailUrl"])))
            .ToList();
        return new ComicBrowsePage(items, (bool?)result["hasNextPage"] ?? false);
    }

    public async Task<ComicDetails> GetComicAsync(string sourceId, string key, CancellationToken ct = default)
    {
        var id = await MangaIdAsync(sourceId, key, ct);
        var data = await GraphAsync("""
            mutation($id: Int!) {
              fetchMangaAndChapters(input: { id: $id, fetchManga: true, fetchChapters: true }) {
                manga { title author artist description status genre thumbnailUrl realUrl source { name displayName } }
                chapters { id }
              }
            }
            """, new { id }, ct);
        var result = data["fetchMangaAndChapters"]!;
        var manga = result["manga"]!;
        var author = (string?)manga["author"];
        var artist = (string?)manga["artist"];
        if (!string.IsNullOrWhiteSpace(artist) && artist != author)
            author = string.IsNullOrWhiteSpace(author) ? artist : $"{author}, {artist}";
        return new ComicDetails(
            sourceId,
            (string?)manga["source"]?["name"] ?? (string?)manga["source"]?["displayName"] ?? sourceId,
            key,
            (string)manga["title"]!,
            string.IsNullOrWhiteSpace(author) ? null : author,
            (string?)manga["description"],
            StatusLabel((string?)manga["status"]),
            manga["genre"]?.AsArray().Select(g => (string)g!).Where(g => !string.IsNullOrWhiteSpace(g)).ToList() ?? [],
            Absolute((string?)manga["thumbnailUrl"]),
            Uri.TryCreate((string?)manga["realUrl"], UriKind.Absolute, out var web) ? web : null,
            result["chapters"]?.AsArray().Count);
    }

    public async Task<IReadOnlyList<ComicChapterInfo>> GetChaptersAsync(string sourceId, string key, CancellationToken ct = default)
    {
        var id = await MangaIdAsync(sourceId, key, ct);
        var data = await GraphAsync("""
            mutation($id: Int!) {
              fetchMangaAndChapters(input: { id: $id, fetchManga: false, fetchChapters: true }) {
                chapters { id url name chapterNumber sourceOrder }
              }
            }
            """, new { id }, ct);
        return data["fetchMangaAndChapters"]!["chapters"]!.AsArray()
            .Select(n => new ComicChapterInfo(
                (string)n!["url"]!,
                (string)n["name"]!,
                (double?)n["chapterNumber"] ?? -1,
                (int?)n["sourceOrder"] ?? 0,
                (long)n["id"]!))
            .OrderBy(c => c.Order)
            .ToList();
    }

    public async Task<IReadOnlyList<Uri>> GetPageUrlsAsync(ComicChapterInfo chapter, CancellationToken ct = default)
    {
        var data = await GraphAsync("mutation($id: Int!) { fetchChapterPages(input: { chapterId: $id }) { pages } }",
            new { id = chapter.RemoteId }, ct);
        return data["fetchChapterPages"]!["pages"]!.AsArray()
            .Select(p => Absolute((string?)p) ?? throw new InvalidOperationException("Página sem endereço."))
            .ToList();
    }

    public async Task<byte[]> GetBytesAsync(Uri url, CancellationToken ct = default)
    {
        using var response = await http.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"O site da fonte não entregou a imagem ({(int)response.StatusCode}).", null, response.StatusCode);
        return await response.Content.ReadAsByteArrayAsync(ct);
    }

    public Uri? Absolute(string? path) =>
        string.IsNullOrEmpty(path) ? null
        : Uri.TryCreate(path, UriKind.Absolute, out var absolute) && absolute.Scheme.StartsWith("http", StringComparison.Ordinal) ? absolute
        : new Uri(Base, path.TrimStart('/'));

    private async Task<int> MangaIdAsync(string sourceId, string key, CancellationToken ct)
    {
        var data = await GraphAsync("query($source: LongString!, $url: String!) { mangas(condition: { sourceId: $source, url: $url }) { nodes { id } } }",
            new { source = sourceId, url = key }, ct);
        var node = data["mangas"]!["nodes"]!.AsArray().FirstOrDefault()
            ?? throw new InvalidOperationException("A obra não está mais no servidor de mangás. Busque e adicione de novo.");
        return (int)node["id"]!;
    }

    internal async Task<JsonNode> GraphAsync(string query, object? variables, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync(new Uri(Base, "api/graphql"), new { query, variables }, ct);
        var body = await response.Content.ReadFromJsonAsync<JsonObject>(ct)
            ?? throw new InvalidOperationException($"Resposta vazia do servidor de mangás ({(int)response.StatusCode}).");
        if (body["errors"] is JsonArray { Count: > 0 } errors)
            throw new InvalidOperationException(Clean((string?)errors[0]?["message"] ?? "Erro desconhecido no servidor de mangás."));
        response.EnsureSuccessStatusCode();
        return body["data"] ?? throw new InvalidOperationException("O servidor de mangás respondeu sem dados.");
    }

    private SuwayomiExtension ParseExtension(JsonNode n) => new(
        (string)n["pkgName"]!,
        (string)n["name"]!,
        (string?)n["lang"] ?? "",
        (string?)n["versionName"] ?? "",
        (bool?)n["isInstalled"] ?? false,
        (bool?)n["hasUpdate"] ?? false,
        (bool?)n["isObsolete"] ?? false,
        ParseWarning((string?)n["contentWarning"]),
        Absolute((string?)n["iconUrl"]));

    private static ContentWarning ParseWarning(string? value) => value switch
    {
        "NSFW" => ContentWarning.Nsfw,
        "MIXED" => ContentWarning.Mixed,
        _ => ContentWarning.Safe,
    };

    private static string? StatusLabel(string? status) => status switch
    {
        "ONGOING" => "Em andamento",
        "COMPLETED" => "Completo",
        "PUBLISHING_FINISHED" => "Publicação encerrada",
        "CANCELLED" => "Cancelado",
        "ON_HIATUS" => "Em hiato",
        "LICENSED" => "Licenciado",
        _ => null,
    };

    private static string Clean(string message)
    {
        const string prefix = "Exception while fetching data";
        if (!message.StartsWith(prefix, StringComparison.Ordinal))
            return message;
        var colon = message.IndexOf(':', prefix.Length);
        return colon >= 0 ? message[(colon + 1)..].Trim() : message;
    }
}
