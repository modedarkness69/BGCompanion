using System.Text.Json;
using System.Text.Json.Serialization;

namespace BGCompanion.Core.Cards;

public sealed class PatchChange
{
    [JsonPropertyName("id")] public string Id { get; set; } = "";
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";     // buff / nerf / change / added / removed
    [JsonPropertyName("detail")] public string Detail { get; set; } = "";

    /// <summary>+1 бафф, −1 нерф, 0 — нейтрально.</summary>
    public int Sign => Kind switch { "buff" or "added" => 1, "nerf" or "removed" => -1, _ => 0 };

    public string KindText => Kind switch
    {
        "buff" => "усилено", "nerf" => "ослаблено", "added" => "новое в пуле", "removed" => "убрано из пула", _ => "изменено",
    };
}

public sealed class PatchInfo
{
    [JsonPropertyName("date")] public string Date { get; set; } = "";
    [JsonPropertyName("changes")] public List<PatchChange> Changes { get; set; } = new();
}

/// <summary>
/// Изменения существ Battlegrounds между двумя версиями базы карт HearthstoneJSON —
/// это и есть баффы/нерфы патча, только в виде данных, а не текста патчноутов.
/// </summary>
public static class PatchDiff
{
    private sealed record M(string Name, int Atk, int Hp, int Tier, string Text, bool Pool);

    private static Dictionary<string, M> Read(string path)
    {
        var d = new Dictionary<string, M>();
        using var fs = File.OpenRead(path);
        using var doc = JsonDocument.Parse(fs);
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            bool pool = e.TryGetProperty("isBattlegroundsPoolMinion", out var p) && p.ValueKind == JsonValueKind.True;
            int tier = e.TryGetProperty("techLevel", out var t) && t.ValueKind == JsonValueKind.Number ? t.GetInt32() : 0;
            if (!pool && tier == 0) continue;
            var id = e.GetProperty("id").GetString() ?? "";
            d[id] = new M(
                e.TryGetProperty("name", out var n) ? n.GetString() ?? id : id,
                e.TryGetProperty("attack", out var a) && a.ValueKind == JsonValueKind.Number ? a.GetInt32() : 0,
                e.TryGetProperty("health", out var h) && h.ValueKind == JsonValueKind.Number ? h.GetInt32() : 0,
                tier,
                CardText.Clean(e.TryGetProperty("text", out var x) ? x.GetString() : ""),
                pool);
        }
        return d;
    }

    public static PatchInfo Compute(string oldPath, string newPath)
    {
        var oldC = Read(oldPath);
        var newC = Read(newPath);
        var info = new PatchInfo { Date = DateTime.Now.ToString("yyyy-MM-dd") };
        foreach (var (id, n) in newC)
        {
            oldC.TryGetValue(id, out var o);
            if (n.Pool && (o == null || !o.Pool))
            {
                info.Changes.Add(new PatchChange { Id = id, Name = n.Name, Kind = "added", Detail = $"T{n.Tier} {n.Atk}/{n.Hp}" });
                continue;
            }
            if (!n.Pool || o == null) continue;
            var parts = new List<string>();
            int score = 0;
            if (n.Atk != o.Atk || n.Hp != o.Hp)
            {
                parts.Add($"{o.Atk}/{o.Hp} → {n.Atk}/{n.Hp}");
                score += (n.Atk + n.Hp) - (o.Atk + o.Hp);
            }
            if (n.Tier != o.Tier)
            {
                parts.Add($"тир {o.Tier} → {n.Tier}");
                score += (o.Tier - n.Tier) * 3; // ниже тир — раньше доступна, это бафф
            }
            bool textChanged = n.Text != o.Text;
            if (textChanged) parts.Add("новый текст: " + n.Text);
            if (parts.Count == 0) continue;
            info.Changes.Add(new PatchChange
            {
                Id = id, Name = n.Name,
                Kind = score > 0 ? "buff" : score < 0 ? "nerf" : "change",
                Detail = string.Join("; ", parts),
            });
        }
        foreach (var (id, o) in oldC)
            if (o.Pool && (!newC.TryGetValue(id, out var n) || !n.Pool))
                info.Changes.Add(new PatchChange { Id = id, Name = o.Name, Kind = "removed", Detail = $"был T{o.Tier}" });
        return info;
    }

    public static string StorePath => Path.Combine(AppInfo.DataDir, "patch_changes.json");

    public static void Save(PatchInfo info) =>
        File.WriteAllText(StorePath, JsonSerializer.Serialize(info, new JsonSerializerOptions { WriteIndented = true }));

    public static PatchInfo? Load()
    {
        try { return File.Exists(StorePath) ? JsonSerializer.Deserialize<PatchInfo>(File.ReadAllText(StorePath)) : null; }
        catch (JsonException) { return null; }
    }
}
