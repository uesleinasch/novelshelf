using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text.Json;
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

public enum SourcePreferenceKind
{
    Text,
    Switch,
    CheckBox,
    List,
    MultiList,
}

public sealed record SourceChoice(string Label, string Value);

public sealed record SourcePreference(
    int Position,
    SourcePreferenceKind Kind,
    string? Key,
    string Title,
    string? Summary,
    bool Enabled,
    string? Text,
    bool IsOn,
    IReadOnlyList<SourceChoice> Choices,
    IReadOnlyList<string> Selected);

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
        var data = await GraphAsync("{ sources { nodes { id name displayName lang contentWarning supportsLatest iconUrl homeUrl isConfigurable } } }", null, ct);
        return data["sources"]!["nodes"]!.AsArray()
            .Select(n => n!)
            .Where(n => (string?)n["id"] != "0")
            .Select(n => new ComicSourceInfo(
                (string)n["id"]!,
                (string?)n["name"] ?? (string)n["displayName"]!,
                (string?)n["lang"] ?? "",
                ParseWarning((string?)n["contentWarning"]) == ContentWarning.Nsfw,
                (bool?)n["supportsLatest"] ?? false,
                Absolute((string?)n["iconUrl"]),
                Uri.TryCreate((string?)n["homeUrl"], UriKind.Absolute, out var home) ? home : null,
                (bool?)n["isConfigurable"] ?? false))
            .OrderBy(s => s.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }

    public async Task<IReadOnlyList<SourcePreference>> GetSourcePreferencesAsync(string sourceId, CancellationToken ct = default)
    {
        var data = await GraphAsync("""
            query($id: LongString!) {
              source(id: $id) {
                preferences {
                  __typename
                  ... on EditTextPreference { key title summary visible enabled text: currentValue }
                  ... on SwitchPreference { key title summary visible enabled on: currentValue }
                  ... on CheckBoxPreference { key title summary visible enabled on: currentValue }
                  ... on ListPreference { key title summary visible enabled choice: currentValue entries entryValues }
                  ... on MultiSelectListPreference { key title summary visible enabled choices: currentValue entries entryValues }
                }
              }
            }
            """, new { id = sourceId }, ct);
        return data["source"]!["preferences"]!.AsArray()
            .Select((n, position) => ParsePreference(n!, position))
            .OfType<SourcePreference>()
            .ToList();
    }

    public async Task SetSourcePreferenceAsync(string sourceId, SourcePreference preference, CancellationToken ct = default)
    {
        var change = new JsonObject { ["position"] = preference.Position };
        switch (preference.Kind)
        {
            case SourcePreferenceKind.Text:
                change["editTextState"] = preference.Text ?? "";
                break;
            case SourcePreferenceKind.Switch:
                change["switchState"] = preference.IsOn;
                break;
            case SourcePreferenceKind.CheckBox:
                change["checkBoxState"] = preference.IsOn;
                break;
            case SourcePreferenceKind.List:
                change["listState"] = preference.Selected.FirstOrDefault() ?? "";
                break;
            case SourcePreferenceKind.MultiList:
                change["multiSelectState"] = new JsonArray(preference.Selected.Select(v => (JsonNode?)JsonValue.Create(v)).ToArray());
                break;
        }
        await GraphAsync(
            "mutation($source: LongString!, $change: SourcePreferenceChangeInput!) { updateSourcePreference(input: { source: $source, change: $change }) { clientMutationId } }",
            new JsonObject { ["source"] = sourceId, ["change"] = change }, ct);
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

    public async Task<Uri> PrepareWebViewAsync(Uri target, CancellationToken ct = default)
    {
        await GraphAsync("mutation { setSettings(input: { settings: { kcefEnabled: true } }) { settings { kcefEnabled } } }", null, ct);
        while (!await WebViewAnswersAsync(target, ct))
            await Task.Delay(WebViewRetryDelay, ct);
        return new Uri(Base, "api/v1/webview#" + Uri.EscapeDataString(target.AbsoluteUri));
    }

    internal TimeSpan WebViewRetryDelay { get; init; } = TimeSpan.FromSeconds(3);
    internal TimeSpan WebViewProbeTimeout { get; init; } = TimeSpan.FromSeconds(20);

    private async Task<bool> WebViewAnswersAsync(Uri target, CancellationToken ct)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(WebViewProbeTimeout);
        using var socket = new ClientWebSocket();
        try
        {
            var endpoint = new UriBuilder(new Uri(Base, "api/v1/webview")) { Scheme = "ws" }.Uri;
            await socket.ConnectAsync(endpoint, timeout.Token);
            var load = JsonSerializer.SerializeToUtf8Bytes(new { type = "loadUrl", url = target.AbsoluteUri, width = 1024, height = 768 });
            await socket.SendAsync(load, WebSocketMessageType.Text, true, timeout.Token);
            var buffer = new byte[4096];
            var answer = await socket.ReceiveAsync(buffer, timeout.Token);
            return answer.MessageType != WebSocketMessageType.Close;
        }
        catch (Exception e) when (e is WebSocketException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return false;
        }
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

    private static SourcePreference? ParsePreference(JsonNode n, int position)
    {
        if ((bool?)n["visible"] == false)
            return null;
        SourcePreferenceKind? kind = (string?)n["__typename"] switch
        {
            "EditTextPreference" => SourcePreferenceKind.Text,
            "SwitchPreference" => SourcePreferenceKind.Switch,
            "CheckBoxPreference" => SourcePreferenceKind.CheckBox,
            "ListPreference" => SourcePreferenceKind.List,
            "MultiSelectListPreference" => SourcePreferenceKind.MultiList,
            _ => null,
        };
        if (kind is not { } known)
            return null;
        var labels = n["entries"]?.AsArray().Select(e => (string?)e ?? "") ?? [];
        var values = n["entryValues"]?.AsArray().Select(e => (string?)e ?? "") ?? [];
        IReadOnlyList<string> selected = known switch
        {
            SourcePreferenceKind.List when (string?)n["choice"] is { } choice => [choice],
            SourcePreferenceKind.MultiList => n["choices"]?.AsArray().Select(c => (string)c!).ToList() ?? [],
            _ => [],
        };
        var key = (string?)n["key"];
        var summary = (string?)n["summary"];
        return new SourcePreference(
            position,
            known,
            key,
            (string?)n["title"] ?? key ?? "",
            string.IsNullOrWhiteSpace(summary) ? null : summary,
            (bool?)n["enabled"] ?? true,
            (string?)n["text"],
            (bool?)n["on"] ?? false,
            labels.Zip(values, (label, value) => new SourceChoice(label, value)).ToList(),
            selected);
    }

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
