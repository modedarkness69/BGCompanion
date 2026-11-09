using BGCompanion.Core.Cards;
using BGCompanion.Core.Logs;

namespace BGCompanion.Core.Games;

public sealed class ShopAdvice
{
    public MinionView Minion { get; init; } = null!;
    public string Name { get; init; } = "";
    public int Tier { get; init; }
    public int Score { get; set; }
    public List<string> Reasons { get; } = new();
    public bool Best { get; set; }
    public bool Skip { get; set; }
}

/// <summary>
/// Подсказки по существам в таверне. Пока только правила: тройки, тип вашего стола,
/// синергии по тексту карты, тир. Позже веса можно уточнять по статистике игр.
/// </summary>
public sealed class ShopAdvisor
{
    private readonly CardDb _cards;
    public ShopAdvisor(CardDb cards) => _cards = cards;

    public static readonly Dictionary<string, string> RaceNames = new()
    {
        ["MURLOC"] = "Мурлоки", ["DRAGON"] = "Драконы", ["MECHANICAL"] = "Механизмы", ["DEMON"] = "Демоны",
        ["QUILBOAR"] = "Свинобразы", ["NAGA"] = "Наги", ["UNDEAD"] = "Нежить", ["PIRATE"] = "Пираты",
        ["ELEMENTAL"] = "Элементали", ["BEAST"] = "Звери", ["ABERRATION"] = "Аберрации",
    };

    // винительный падеж: «усиливает драконов»
    private static readonly Dictionary<string, string> RaceAcc = new()
    {
        ["MURLOC"] = "мурлоков", ["DRAGON"] = "драконов", ["MECHANICAL"] = "механизмы", ["DEMON"] = "демонов",
        ["QUILBOAR"] = "свинобразов", ["NAGA"] = "наг", ["UNDEAD"] = "нежить", ["PIRATE"] = "пиратов",
        ["ELEMENTAL"] = "элементалей", ["BEAST"] = "зверей", ["ABERRATION"] = "аберраций",
    };

    // корни слов в тексте карт (ruRU) для поиска синергий
    private static readonly Dictionary<string, string[]> RaceStems = new()
    {
        ["MURLOC"] = new[] { "мурлок" }, ["DRAGON"] = new[] { "дракон" }, ["MECHANICAL"] = new[] { "механизм" },
        ["DEMON"] = new[] { "демон" }, ["QUILBOAR"] = new[] { "свинобраз" }, ["NAGA"] = new[] { "наг" },
        ["UNDEAD"] = new[] { "нежит" }, ["PIRATE"] = new[] { "пират" }, ["ELEMENTAL"] = new[] { "элементал" },
        ["BEAST"] = new[] { "звер" }, ["ABERRATION"] = new[] { "аберрац" },
    };

    private static readonly (string Mechanic, string Stem, string Label)[] MechanicLinks =
    {
        ("BATTLECRY", "боевой клич", "боевыми кличами"),
        ("DEATHRATTLE", "предсмертн", "предсмертными хрипами"),
        ("BACON_RALLY", "натиск", "натиском"),
        ("DIVINE_SHIELD", "божественн", "божественными щитами"),
        ("MAGNETIC", "магнетизм", "магнетизмом"),
        ("TAUNT", "провокаци", "провокацией"),
    };

    public static string RaceName(string race) => RaceNames.TryGetValue(race, out var n) ? n : race;

    /// <summary>Типы существ в этой игре — по тому, что встречалось в таверне.</summary>
    public List<string> LobbyRaces(IEnumerable<string> seenShopCards, out bool complete)
    {
        var byRace = new Dictionary<string, HashSet<string>>();
        foreach (var id in seenShopCards)
        {
            var c = _cards.Get(id);
            if (c == null) continue;
            foreach (var r in c.Races)
            {
                if (r is "ALL" or "INVALID" or "") continue;
                if (!byRace.TryGetValue(r, out var set)) byRace[r] = set = new HashSet<string>();
                set.Add(c.Id);
            }
        }
        // в лобби 5 типов; случайные одиночки (из открытий и даров) отсекаются порогом
        var races = byRace.Where(kv => kv.Value.Count >= 2)
            .OrderByDescending(kv => kv.Value.Count).Take(5).Select(kv => kv.Key).ToList();
        complete = races.Count >= 5;
        return races;
    }

    private string BaseId(string cardId)
    {
        var c = _cards.Get(cardId);
        return c != null && c.NormalDbfId > 0 && _cards.GetByDbf(c.NormalDbfId) is { } n ? n.Id : cardId;
    }

    public List<ShopAdvice> Rate(BgSnapshot s, int tavernTier, CompProgress? focus = null)
    {
        var mine = s.Board.Concat(s.Hand).ToList();
        // сколько копий каждой карты уже есть (золотые не считаются — их тройкой не собрать)
        var copies = mine.Where(m => !m.Golden).GroupBy(m => BaseId(m.CardId)).ToDictionary(g => g.Key, g => g.Count());

        // главный тип стола
        var raceCount = new Dictionary<string, int>();
        var mechCount = new Dictionary<string, int>();
        foreach (var m in s.Board)
        {
            var c = _cards.Get(m.CardId);
            if (c == null) continue;
            foreach (var r in c.Races.Where(r => r != "ALL")) raceCount[r] = raceCount.GetValueOrDefault(r) + 1;
            foreach (var mech in c.Mechanics) mechCount[mech] = mechCount.GetValueOrDefault(mech) + 1;
        }
        var main = raceCount.OrderByDescending(kv => kv.Value).FirstOrDefault();

        var list = new List<ShopAdvice>();
        foreach (var m in s.Shop)
        {
            var c = _cards.Get(m.CardId);
            var a = new ShopAdvice { Minion = m, Name = c?.Name ?? m.CardId, Tier = c?.TechLevel ?? 0 };
            if (c == null) { list.Add(a); continue; }
            var text = (c.Text ?? "").ToLowerInvariant();

            // тройки
            var have = copies.GetValueOrDefault(BaseId(m.CardId));
            if (have >= 2) { a.Score += 100; a.Reasons.Add("тройка!"); }
            else if (have == 1) { a.Score += 20; a.Reasons.Add("пара — шаг к тройке"); }

            // тип стола
            bool isAll = c.Races.Contains("ALL");
            if (main.Value >= 2 && (c.Races.Contains(main.Key) || isAll))
            {
                a.Score += 15 + 4 * main.Value;
                a.Reasons.Add($"в ваш тип ({RaceName(main.Key)} ×{main.Value})");
            }

            // синергия по тексту: карта усиливает тип, который у вас на столе
            foreach (var (race, n) in raceCount.Where(kv => kv.Value >= 2))
            {
                if (RaceStems.TryGetValue(race, out var stems) && stems.Any(text.Contains))
                {
                    a.Score += 12 + 3 * n;
                    a.Reasons.Add("усиливает " + (RaceAcc.TryGetValue(race, out var acc) ? acc : RaceName(race).ToLowerInvariant()));
                    break;
                }
            }
            foreach (var (mech, stem, label) in MechanicLinks)
            {
                if (mechCount.GetValueOrDefault(mech) >= 2 && text.Contains(stem) && !c.Mechanics.Contains(mech))
                {
                    a.Score += 10;
                    a.Reasons.Add($"синергия с {label}");
                    break;
                }
            }

            // ведущая сборка
            if (focus != null)
            {
                var baseId = BaseId(m.CardId);
                if (focus.Comp.Core.Contains(baseId)) { a.Score += 25; a.Reasons.Add($"ключевая для сборки «{focus.Comp.Name}»"); }
                else if (focus.Comp.Early.Contains(baseId) && tavernTier <= 4) { a.Score += 10; a.Reasons.Add($"заход в сборку «{focus.Comp.Name}»"); }
            }

            // тир: свежие карты текущего тира обычно сильнее
            a.Score += a.Tier * 3;
            if (tavernTier > 0 && a.Tier == tavernTier) a.Reasons.Add($"тир {a.Tier} — максимум сейчас");

            // чужой тип при уже собранном своём
            if (main.Value >= 3 && c.Races.Length > 0 && !isAll && !c.Races.Contains(main.Key) && have == 0 && a.Reasons.Count == 0)
            {
                a.Score -= 10;
                a.Reasons.Add("не ваш тип");
            }
            list.Add(a);
        }

        if (list.Count > 0)
        {
            // ★ — только если есть настоящая причина брать (тройка, тип, синергия), а не «лучший из плохих»
            static bool Strong(ShopAdvice x) => x.Reasons.Any(r => !r.StartsWith("тир ") && r != "не ваш тип");
            var best = list.OrderByDescending(x => x.Score).First();
            if (best.Score > 0 && Strong(best)) best.Best = true;
            foreach (var x in list)
                x.Skip = !x.Best && (x.Reasons.Contains("не ваш тип") || (best.Best && x.Score < best.Score - 20 && !Strong(x)));
        }
        return list.OrderByDescending(x => x.Score).ToList();
    }
}
