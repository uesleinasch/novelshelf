using System.Text.Json;
using System.Text.Json.Serialization;

namespace NovelShelf.App.Services;

public sealed class AppSettings
{
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    [JsonIgnore]
    public string Path { get; private set; } = "";

    public string KindleProfile { get; set; } = NovelShelf.Comics.KindleProfile.Default.Id;
    public bool ShowAdultSources { get; set; }

    public static AppSettings Load(string path)
    {
        AppSettings settings;
        try
        {
            settings = File.Exists(path)
                ? JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings()
                : new AppSettings();
        }
        catch (Exception e) when (e is JsonException or IOException)
        {
            settings = new AppSettings();
        }
        settings.Path = path;
        return settings;
    }

    public void Save()
    {
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, Path, overwrite: true);
    }
}
