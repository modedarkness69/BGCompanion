using BGCompanion.Core.Cards;

namespace BGCompanion.Core.Games;

public sealed class HeroAdvice
{
    public string HeroId { get; init; } = "";
    public string Name { get; init; } = "";
    public HeroStats? Personal { get; init; }
    public CommunityHero? Community { get; init; }
    public double Score { get; init; }        // меньше — лучше (ожидаемое место)
    public bool Recommended { get; set; }
}

/// <summary>Подсказки по выбору героя и темпу таверны.</summary>
public sealed class Advisor
{
    private readonly CardDb _cards;
    private readonly StatsStore _store;
    private readonly CommunityStats _community;

    // ориентир по умолчанию: на каком ходу таверны обычно берут тир
    private static readonly Dictionary<int, int> DefaultCurve = new() { [2] = 2, [3] = 4, [4] = 6, [5] = 8, [6] = 10 };

    public Advisor(CardDb cards, StatsStore store, CommunityStats community)
    {
        _cards = cards;
        _store = store;
        _community = community;
    }

    public List<HeroAdvice> RankHeroes(IEnumerable<string> offered)
    {
        var table = _store.HeroTable(_cards);
        var list = new List<HeroAdvice>();
        foreach (var raw in offered.Distinct())
        {
            var id = _cards.BaseHeroId(raw);
            table.TryGetValue(id, out var personal);
            var comm = _community.Get(id);
            // оценка: сообщество (если есть) + личная статистика с весом по числу игр
            double? cScore = comm?.AvgPlace;
            double? pScore = personal is { Games: > 0 } ? personal.AvgPlace : null;
            double score;
            if (cScore != null && pScore != null)
            {
                var w = Math.Min(personal!.Games, 20) / 40.0; // максимум половина веса — личной
                score = cScore.Value * (1 - w) + pScore.Value * w;
            }
            else score = cScore ?? pScore ?? 4.5;
            list.Add(new HeroAdvice { HeroId = id, Name = _cards.NameOf(raw), Personal = personal, Community = comm, Score = score });
        }
        var known = list.Where(h => h.Community != null || h.Personal is { Games: > 0 }).ToList();
        if (known.Count > 0) known.OrderBy(h => h.Score).First().Recommended = true;
        return list.OrderBy(h => h.Score).ToList();
    }

    /// <summary>Средний ход повышения до тира в ваших играх с местом в топ-4.</summary>
    public Dictionary<int, double> PersonalCurve()
    {
        var res = new Dictionary<int, double>();
        var good = _store.MyGames.Where(g => g.Complete && g.Placement is > 0 and <= 4).ToList();
        if (good.Count < 3) return res;
        for (int t = 2; t <= 6; t++)
        {
            var turns = good.Where(g => g.TierUpTurns.ContainsKey(t)).Select(g => g.TierUpTurns[t]).ToList();
            if (turns.Count >= 2) res[t] = turns.Average();
        }
        return res;
    }

    public string TavernTip(int turn, int tier)
    {
        if (turn <= 0) return "";
        var curve = PersonalCurve();
        int next = tier + 1;
        if (next > 6) return "Таверна максимальная — собирайте сильнейший состав.";
        bool personal = curve.TryGetValue(next, out var pTurn);
        double target = personal ? pTurn : DefaultCurve[next];
        string src = personal ? "в ваших топ-4 играх" : "обычно";
        if (turn >= Math.Ceiling(target))
            return $"Пора на T{next}: {src} его берут к {target:0.#}-му ходу.";
        return $"Следующий тир T{next} {src} берут к {target:0.#}-му ходу.";
    }
}
