using BGCompanion.Core.Cards;

namespace BGCompanion.Core.Games;

/// <summary>Сборка, найденная в ваших играх, со статистикой по реальным столам.</summary>
public sealed class MinedComp
{
    public Comp Comp { get; init; } = null!;
    public int Boards { get; init; }          // сколько итоговых столов попало в сборку
    public double AvgPlace { get; init; }
    public double Top4Rate { get; init; }
    public double WinRate { get; init; }
    public bool EnoughData => Boards >= MetaMiner.MinBoardsForTier;
}

/// <summary>
/// Своя мета из логов: итоговые столы всех игроков ваших матчей (ваш и 7 соперников) с их местами.
/// Столы группируются по основному типу, внутри типа ищутся «якорные» карты, которые часто стоят вместе;
/// вокруг якоря собирается ядро сборки и считается, как такие столы занимают места.
/// </summary>
public sealed class MetaMiner
{
    public const int MinBoardsForTier = 8;     // меньше — показываем, но без тира
    private const int MinTurn = 8;              // ранние столы не показательны
    private const int MinMinions = 4;

    private readonly CardDb _cards;
    public MetaMiner(CardDb cards) => _cards = cards;

    private sealed record Board(HashSet<string> Cards, int Place, string Group);

    private string BaseId(string id)
    {
        var c = _cards.Get(id);
        return c != null && c.NormalDbfId > 0 && _cards.GetByDbf(c.NormalDbfId) is { } n ? n.Id : id;
    }

    /// <summary>Основной тип стола: 3+ существа одного типа (существа «все типы» считаются в любой).</summary>
    private string GroupOf(IEnumerable<string> cardIds)
    {
        var count = new Dictionary<string, int>();
        int all = 0;
        foreach (var id in cardIds)
        {
            var c = _cards.Get(id);
            if (c == null) continue;
            if (c.Races.Contains("ALL")) { all++; continue; }
            foreach (var r in c.Races) count[r] = count.GetValueOrDefault(r) + 1;
        }
        var best = count.OrderByDescending(kv => kv.Value).FirstOrDefault();
        return best.Value + all >= 3 ? best.Key : "MIXED";
    }

    public int TotalBoards { get; private set; }
    public int TotalGames { get; private set; }

    public List<MinedComp> Mine(IEnumerable<GameRecord> games)
    {
        var boards = new List<Board>();
        int gamesUsed = 0;
        foreach (var g in games.Where(g => g.Complete && g.Boards.Count > 0))
        {
            gamesUsed++;
            foreach (var b in g.Boards)
            {
                if (b.Place <= 0 || b.Turn < MinTurn) continue;
                var ids = b.Cards.Select(BaseId).Where(id => _cards.Get(id)?.IsPoolMinion == true).ToHashSet();
                if (ids.Count < MinMinions) continue;
                boards.Add(new Board(ids, b.Place, GroupOf(ids)));
            }
        }
        TotalBoards = boards.Count;
        TotalGames = gamesUsed;

        var result = new List<MinedComp>();
        foreach (var group in boards.GroupBy(b => b.Group))
        {
            var list = group.ToList();
            if (list.Count < 2) continue;
            // частота карт в группе
            var freq = list.SelectMany(b => b.Cards).GroupBy(c => c).ToDictionary(x => x.Key, x => x.Count());
            var anchors = freq.Where(kv => kv.Value >= Math.Max(2, list.Count * 0.15))
                              .OrderByDescending(kv => kv.Value).Select(kv => kv.Key).ToList();
            var cores = new List<HashSet<string>>();
            foreach (var anchor in anchors)
            {
                var cluster = list.Where(b => b.Cards.Contains(anchor)).ToList();
                if (cluster.Count < 2) continue;
                var cf = cluster.SelectMany(b => b.Cards).GroupBy(c => c).ToDictionary(x => x.Key, x => x.Count());
                var core = cf.Where(kv => kv.Value >= Math.Max(2, cluster.Count * 0.4))
                             .OrderByDescending(kv => kv.Value).ThenByDescending(kv => _cards.Get(kv.Key)?.TechLevel ?? 0)
                             .Take(6).Select(kv => kv.Key).ToHashSet();
                core.Add(anchor);
                if (core.Count < 2) continue;
                // не плодим почти одинаковые сборки
                if (cores.Any(c => Jaccard(c, core) >= 0.6)) continue;
                cores.Add(core);

                // в сборку попадают столы, где есть якорь и хотя бы половина ядра
                var members = list.Where(b => b.Cards.Contains(anchor) && core.Count(b.Cards.Contains) * 2 >= core.Count).ToList();
                if (members.Count < 2) continue;
                // название: тип + самая «своя» карта (частая здесь, редкая в остальной группе)
                var signature = core.OrderByDescending(id => Lift(id, members, list)).ThenByDescending(id => _cards.Get(id)?.TechLevel ?? 0).First();
                var groupName = group.Key == "MIXED" ? "Смешанная" : ShopAdvisor.RaceName(group.Key);
                var places = members.Select(m => m.Place).ToList();
                var avg = places.Average();
                var mined = new MinedComp
                {
                    Boards = members.Count,
                    AvgPlace = avg,
                    Top4Rate = places.Count(p => p <= 4) / (double)places.Count,
                    WinRate = places.Count(p => p == 1) / (double)places.Count,
                    Comp = new Comp
                    {
                        Id = $"mined-{group.Key.ToLowerInvariant()}-{signature}",
                        Name = $"{groupName}: {_cards.NameOf(signature)}",
                        Races = group.Key == "MIXED" ? new List<string>() : new List<string> { group.Key },
                        Core = core.OrderByDescending(id => _cards.Get(id)?.TechLevel ?? 0).ToList(),
                        Early = new List<string>(),
                        Tier = members.Count >= MinBoardsForTier ? TierOf(avg) : "",
                        Source = "mined",
                        Tips = $"По {members.Count} столам из ваших игр: среднее место {avg:0.0}, " +
                               $"топ-4 {places.Count(p => p <= 4) * 100 / places.Count}%.",
                    },
                };
                result.Add(mined);
            }
        }
        return result.OrderBy(m => m.EnoughData ? 0 : 1).ThenBy(m => m.AvgPlace).ToList();
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b) =>
        a.Count + b.Count == 0 ? 0 : a.Intersect(b).Count() / (double)a.Union(b).Count();

    private static double Lift(string id, List<Board> members, List<Board> all)
    {
        double inMembers = members.Count(b => b.Cards.Contains(id)) / (double)members.Count;
        double inAll = all.Count(b => b.Cards.Contains(id)) / (double)all.Count;
        return inAll == 0 ? 0 : inMembers / inAll;
    }

    /// <summary>Тир по среднему месту (в BG среднее по лобби — 4,5).</summary>
    public static string TierOf(double avgPlace) => avgPlace switch
    {
        <= 3.0 => "S",
        <= 3.8 => "A",
        <= 4.6 => "B",
        _ => "C",
    };
}
