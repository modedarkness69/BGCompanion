using System.Net;
using System.Text.Json;

namespace BGCompanion.Core.Cards;

public sealed class Card
{
    public string Id { get; init; } = "";
    public int DbfId { get; init; }
    public string Name { get; init; } = "";
    public string Type { get; init; } = "";
    public int TechLevel { get; init; }
    public string[] Races { get; init; } = Array.Empty<string>();
    public bool IsPoolMinion { get; init; }
    public bool IsBgHero { get; init; }
    public bool IsTrinket { get; init; }
    public bool IsDarkGift { get; init; }
    public bool IsPoolSpell { get; init; }
    public int SkinParentDbfId { get; init; }
    public int NormalDbfId { get; init; }        // у золотой версии — обычная
    public string[] Mechanics { get; init; } = Array.Empty<string>();
    public int Attack { get; init; }
    public int Health { get; init; }
    public int Armor { get; init; }
    public string Text { get; init; } = "";
}

/// <summary>
/// База карт из HearthstoneJSON (бесплатный открытый источник).
/// Синхронизируется при запуске: скачивается только если на сервере новая версия (ETag).
/// </summary>
public sealed class CardDb
{
    private Dictionary<string, Card> _byId = new(StringComparer.OrdinalIgnoreCase);
    private Dictionary<int, Card> _byDbf = new();

    public string Locale { get; }
    public int Count => _byId.Count;
    public DateTime? SourceDate { get; private set; }

    public CardDb(string locale = "ruRU") => Locale = locale;

    private string CachePath => Path.Combine(AppInfo.DataDir, $"cards_{Locale}.json");
    private string MetaPath => Path.Combine(AppInfo.DataDir, $"cards_{Locale}.meta");

    public Card? Get(string? cardId) =>
        cardId != null && _byId.TryGetValue(cardId, out var c) ? c : null;

    public Card? GetByDbf(int dbf) => _byDbf.TryGetValue(dbf, out var c) ? c : null;

    public string NameOf(string? cardId) => Get(cardId)?.Name ?? cardId ?? "?";

    public IEnumerable<Card> PoolMinions => _byId.Values.Where(c => c.IsPoolMinion);

    /// <summary>Скин героя → базовый герой (статистика считается по базовому).</summary>
    public string BaseHeroId(string cardId)
    {
        var c = Get(cardId);
        if (c != null && c.SkinParentDbfId > 0 && GetByDbf(c.SkinParentDbfId) is { } parent)
            return parent.Id;
        return cardId;
    }

    /// <summary>Скачивает свежую базу, если она изменилась. Возвращает true, если файл обновлён.</summary>
    public Task<bool> SyncAsync(CancellationToken ct = default) => Net.RetryAsync(SyncOnceAsync, ct);

    private async Task<bool> SyncOnceAsync(CancellationToken ct)
    {
        var url = $"https://api.hearthstonejson.com/v1/latest/{Locale}/cards.json";
        using var http = Net.CreateClient(TimeSpan.FromSeconds(120));
        var req = new HttpRequestMessage(HttpMethod.Get, url);
        if (File.Exists(CachePath) && File.Exists(MetaPath))
        {
            var etag = File.ReadAllText(MetaPath).Trim();
            if (etag.Length > 0) req.Headers.TryAddWithoutValidation("If-None-Match", etag);
        }
        using var resp = await http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, ct);
        if (resp.StatusCode == HttpStatusCode.NotModified) return false;
        resp.EnsureSuccessStatusCode();

        var tmp = CachePath + ".tmp";
        await using (var fs = File.Create(tmp))
            await resp.Content.CopyToAsync(fs, ct);
        File.Move(tmp, CachePath, overwrite: true);
        File.WriteAllText(MetaPath, resp.Headers.ETag?.Tag ?? "");
        return true;
    }

    public bool HasCache => File.Exists(CachePath);

    public void Load()
    {
        if (!File.Exists(CachePath)) return;
        using var fs = File.OpenRead(CachePath);
        using var doc = JsonDocument.Parse(fs);
        var byId = new Dictionary<string, Card>(StringComparer.OrdinalIgnoreCase);
        var byDbf = new Dictionary<int, Card>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var card = new Card
            {
                Id = Str(e, "id"),
                DbfId = Int(e, "dbfId"),
                Name = Str(e, "name"),
                Type = Str(e, "type"),
                TechLevel = Int(e, "techLevel"),
                Races = e.TryGetProperty("races", out var r) && r.ValueKind == JsonValueKind.Array
                    ? r.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                    : (e.TryGetProperty("race", out var r1) && r1.ValueKind == JsonValueKind.String ? new[] { r1.GetString()! } : Array.Empty<string>()),
                IsPoolMinion = Bool(e, "isBattlegroundsPoolMinion"),
                IsBgHero = Bool(e, "battlegroundsHero"),
                IsTrinket = Str(e, "type") == "BATTLEGROUND_TRINKET",
                IsDarkGift = Bool(e, "isBattlegroundsDarkGift"),
                IsPoolSpell = Bool(e, "isBattlegroundsPoolSpell"),
                SkinParentDbfId = Int(e, "battlegroundsSkinParentId"),
                NormalDbfId = Int(e, "battlegroundsNormalDbfId"),
                Mechanics = e.TryGetProperty("mechanics", out var mech) && mech.ValueKind == JsonValueKind.Array
                    ? mech.EnumerateArray().Select(x => x.GetString() ?? "").ToArray()
                    : Array.Empty<string>(),
                Attack = Int(e, "attack"),
                Health = Int(e, "health"),
                Armor = Int(e, "armor"),
                Text = Str(e, "text"),
            };
            if (card.Id.Length == 0) continue;
            byId[card.Id] = card;
            if (card.DbfId > 0) byDbf[card.DbfId] = card;
        }
        _byId = byId;
        _byDbf = byDbf;
        SourceDate = File.GetLastWriteTime(CachePath);
    }

    private static string Str(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static int Int(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;

    private static bool Bool(JsonElement e, string p) =>
        e.TryGetProperty(p, out var v) && v.ValueKind == JsonValueKind.True;
}
