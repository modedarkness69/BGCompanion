using System.Text.Json;

namespace BGCompanion.Core;

public sealed class Settings
{
    public string? HearthstoneDir { get; set; }
    public string CardLocale { get; set; } = "ruRU";
    public bool OverlayEnabled { get; set; } = true;
    public double OverlayOpacity { get; set; } = 0.92;
    public double OverlayScale { get; set; } = 1.0;
    public bool ShowHeroPickPanel { get; set; } = true;
    public bool ShowTavernTips { get; set; } = true;
    public bool CheckUpdatesOnStart { get; set; } = true;
    public bool StartMinimized { get; set; }
    public bool ImportedOldLogs { get; set; }

    private static string PathOf => System.IO.Path.Combine(AppInfo.DataDir, "settings.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    public static Settings Load()
    {
        try
        {
            if (File.Exists(PathOf))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(PathOf), Json) ?? new Settings();
        }
        catch (JsonException) { }
        return new Settings();
    }

    public void Save() => File.WriteAllText(PathOf, JsonSerializer.Serialize(this, Json));
}
