using System.Text.Json;
using System.Text.Json.Serialization;

namespace BGCompanion.Core.Games;

public sealed class CommunityHero
{
    [JsonPropertyName("games")] public int Games { get; set; }
    [JsonPropertyName("avgPlace")] public double AvgPlace { get; set; }
    [JsonPropertyName("top4")] public double Top4Rate { get; set; }
    [JsonPropertyName("pickRate")] public double PickRate { get; set; }
}

public sealed class CommunityFile
{
    [JsonPropertyName("updated")] public string Updated { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("build")] public int Build { get; set; }
    [JsonPropertyName("heroes")] public Dictionary<string, CommunityHero> Heroes { get; set; } = new();
}

/// <summary>
/// Общая статистика, которая публикуется в репозитории проекта (data/hero-stats.json)
/// и скачивается при запуске. Источник данных — только разрешённые/собственные.
/// </summary>
public sealed class CommunityStats
{
    public const string Url =
        "https://raw.githubusercontent.com/" + AppInfo.GitHubOwner + "/" + AppInfo.GitHubRepo + "/main/data/hero-stats.json";

    private CommunityFile _data = new();
    private string CachePath => Path.Combine(AppInfo.DataDir, "hero-stats.json");

    public int HeroCount => _data.Heroes.Count;
    public string Updated => _data.Updated;
    public string Source => _data.Source;

    public CommunityHero? Get(string heroId) =>
        _data.Heroes.TryGetValue(heroId, out var h) && h.Games > 0 ? h : null;

    public void LoadCache()
    {
        try
        {
            if (File.Exists(CachePath))
                _data = JsonSerializer.Deserialize<CommunityFile>(File.ReadAllText(CachePath)) ?? new CommunityFile();
        }
        catch (JsonException) { }
    }

    public async Task<bool> SyncAsync(CancellationToken ct = default)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd($"BGCompanion/{AppInfo.VersionText}");
        var json = await http.GetStringAsync(Url, ct);
        var data = JsonSerializer.Deserialize<CommunityFile>(json);
        if (data == null) return false;
        _data = data;
        File.WriteAllText(CachePath, json);
        return true;
    }
}
