using System.Text.Json;
using System.Text.Json.Serialization;
using BGCompanion.Core.Cards;
using BGCompanion.Core.Logs;

namespace BGCompanion.Core.Games;

public sealed class Comp
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("races")] public List<string> Races { get; set; } = new();
    [JsonPropertyName("core")] public List<string> Core { get; set; } = new();
    [JsonPropertyName("early")] public List<string> Early { get; set; } = new();
    [JsonPropertyName("tips")] public string Tips { get; set; } = "";
    [JsonPropertyName("tier")] public string Tier { get; set; } = "";      // S/A/B/C по данным сообщества
    [JsonPropertyName("source")] public string Source { get; set; } = "";  // community / synergy

    public double TierBonus => Tier switch { "S" => 3, "A" => 2, "B" => 1, "C" => 0.3, _ => 0 };
}

public sealed class CompsFile
{
    [JsonPropertyName("updated")] public string Updated { get; set; } = "";
    [JsonPropertyName("source")] public string Source { get; set; } = "";
    [JsonPropertyName("comps")] public List<Comp> Comps { get; set; } = new();
}

public sealed class CompProgress
{
    public Comp Comp { get; init; } = null!;
    public int CoreOwned { get; init; }
    public int EarlyOwned { get; init; }
    public double Score { get; init; }
    public List<string> MissingCore { get; init; } = new();
    public bool Recommended { get; set; }
}

/// <summary>
/// Сборки из файла data/comps.json в репозитории проекта (скачивается при запуске).
/// Подбирает сборки под типы лобби и считает прогресс по вашему столу и руке.
/// </summary>
public sealed class CompsDb
{
    public const string Url =
        "https://raw.githubusercontent.com/" + AppInfo.GitHubOwner + "/" + AppInfo.GitHubRepo + "/main/data/comps.json";

    private CompsFile _data = new();
    private string CachePath => Path.Combine(AppInfo.DataDir, "comps.json");

    public int Count => _data.Comps.Count;
    public string Updated => _data.Updated;
    public IReadOnlyList<Comp> All => _data.Comps;

    public void LoadCache()
    {
        // сначала скачанная копия, иначе файл, поставляемый с программой
        foreach (var path in new[] { CachePath, Path.Combine(AppContext.BaseDirectory, "comps.json") })
        {
            try
            {
                if (!File.Exists(path)) continue;
                var data = JsonSerializer.Deserialize<CompsFile>(File.ReadAllText(path));
                if (data is { Comps.Count: > 0 }) { _data = data; return; }
            }
            catch (JsonException) { }
        }
    }

    public async Task<bool> SyncAsync(CancellationToken ct = default)
    {
        var json = await Net.RetryAsync(async c =>
        {
            using var http = Net.CreateClient(TimeSpan.FromSeconds(30));
            return await http.GetStringAsync(Url, c);
        }, ct, attempts: 3);
        var data = JsonSerializer.Deserialize<CompsFile>(json);
        if (data == null || data.Comps.Count == 0) return false;
        _data = data;
        File.WriteAllText(CachePath, json);
        return true;
    }

    /// <summary>Сборки, доступные в этом лобби, с прогрессом. Лучшие — первыми.</summary>
    public List<CompProgress> Rank(IReadOnlyCollection<string> lobbyRaces, IEnumerable<MinionView> mine, CardDb cards)
    {
        var owned = new HashSet<string>(mine.Select(m => NormalId(m.CardId, cards)));
        var list = new List<CompProgress>();
        foreach (var comp in _data.Comps)
        {
            // сборка на тип возможна, только если тип есть в игре (пока типы неизвестны — показываем все)
            if (comp.Races.Count > 0 && lobbyRaces.Count > 0 && !comp.Races.All(lobbyRaces.Contains)) continue;
            int core = comp.Core.Count(owned.Contains);
            int early = comp.Early.Count(owned.Contains);
            // небольшой бонус сборкам на тип: в них проще попадать картами из таверны
            double score = core * 3 + early * 1.5 + comp.TierBonus + (comp.Races.Count > 0 ? 1 : 0)
                           + mine.Count(m => comp.Races.Any(r => cards.Get(m.CardId)?.Races.Contains(r) == true)) * 0.8;
            list.Add(new CompProgress
            {
                Comp = comp,
                CoreOwned = core,
                EarlyOwned = early,
                Score = score,
                MissingCore = comp.Core.Where(id => !owned.Contains(id)).ToList(),
            });
        }
        var ranked = list.OrderByDescending(p => p.Score).ThenBy(p => p.Comp.Races.Count == 0).ToList();
        // ★ только когда есть на что опереться: свои карты или явный лидер по тиру
        if (ranked.Count > 0 && (ranked[0].CoreOwned + ranked[0].EarlyOwned > 0 || ranked[0].Comp.TierBonus >= 3)) ranked[0].Recommended = true;
        return ranked;
    }

    private static string NormalId(string cardId, CardDb cards)
    {
        var c = cards.Get(cardId);
        return c != null && c.NormalDbfId > 0 && cards.GetByDbf(c.NormalDbfId) is { } n ? n.Id : cardId;
    }
}
