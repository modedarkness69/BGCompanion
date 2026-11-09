// Проверка парсера на настоящих логах: dotnet run --project tools/LogCheck -- "<папка Hearthstone>"
using System.Text;
using BGCompanion.Core.Cards;
using BGCompanion.Core.Games;
using BGCompanion.Core.Logs;

Console.OutputEncoding = Encoding.UTF8;
var dir = args.Length > 0 ? args[0] : HearthstoneLocator.FindInstallDir();
if (dir == null) { Console.WriteLine("Hearthstone не найден"); return 1; }

var cards = new CardDb();
if (!cards.HasCache) await cards.SyncAsync();
cards.Load();
Console.WriteLine($"Карт: {cards.Count}, существ в пуле: {cards.PoolMinions.Count()}");

var tmp = Path.Combine(Path.GetTempPath(), "bgc_logcheck_games.jsonl");
File.Delete(tmp);
var store = new StatsStore(tmp);
var added = store.ImportFromLogs(dir);
Console.WriteLine($"Импортировано игр: {added}");
foreach (var g in store.Games)
{
    Console.WriteLine($"- {g.StartedAt:dd.MM HH:mm} {g.GameType} build {g.BuildNumber} complete={g.Complete}");
    Console.WriteLine($"  предложены: {string.Join(", ", g.OfferedHeroes.Select(cards.NameOf))}");
    Console.WriteLine($"  выбран: {cards.NameOf(g.HeroCardId)} → место {g.Placement}, таверна {g.FinalTavernTier}, ходов {g.Turns}");
    Console.WriteLine($"  повышения: {string.Join(", ", g.TierUpTurns.OrderBy(k => k.Key).Select(k => $"T{k.Key}@{k.Value}"))}");
    foreach (var c in g.Choices)
        Console.WriteLine($"  выбор {c.Kind} (ход {c.Turn}): {string.Join(" / ", c.Offered.Select(cards.NameOf))} → {cards.NameOf(c.Chosen)}");
}
foreach (var s in store.HeroTable(cards).Values.OrderByDescending(s => s.Offered))
    Console.WriteLine($"  {cards.NameOf(s.HeroId),-28} предлагали {s.Offered}, брали {s.Picked}, игр {s.Games}, ср.место {s.AvgPlace:0.0}");

// Проверка живого чтения: события парсера на последнем логе
var last = HearthstoneLocator.SessionDirs(dir).Last();
var p = new PowerLogParser();
p.SetLogDate(last.Started);
int offers = 0, tiers = 0, turns = 0;
p.HeroesOffered += (_, _) => offers++;
p.TavernTierChanged += (_, _) => tiers++;
p.TurnChanged += (_, _) => turns++;
foreach (var f in HearthstoneLocator.PowerLogs(last.Dir))
    foreach (var line in File.ReadLines(f)) p.Feed(line);
Console.WriteLine($"События последнего лога: предложения героев {offers}, смены таверны {tiers}, ходов {turns}");

// Таверна: снимок перед каждым боем (конец хода в таверне)
var full = LogReplayer.FindDemoLog(dir);
if (full != null)
{
    var p2 = new PowerLogParser();
    p2.SetLogDate(full.Value.Date);
    var adv = new ShopAdvisor(cards);
    int tier = 1;
    p2.TavernTierChanged += (_, t) => tier = t;
    string Name(string id) => cards.NameOf(id);
    foreach (var line in File.ReadLines(full.Value.File))
    {
        if (line.Contains("GameState.DebugPrintPower") && line.Contains("Entity=GameEntity tag=TURN value="))
        {
            var v = int.Parse(line[(line.LastIndexOf('=') + 1)..].Trim());
            if (v % 2 == 0 && v > 0)
            {
                var s = p2.Tracker.Snapshot();
                var races = adv.LobbyRaces(s.SeenShopCards, out var complete);
                Console.WriteLine($"\nХод {s.Turn}, таверна T{tier}. Типы{(complete ? "" : " (пока)")}: {string.Join(", ", races.Select(ShopAdvisor.RaceName))}");
                Console.WriteLine("  стол: " + string.Join(", ", s.Board.Select(m => $"{Name(m.CardId)} {m.Atk}/{m.Health}")));
                foreach (var a in adv.Rate(s, tier))
                    Console.WriteLine($"  {(a.Best ? "★" : a.Skip ? "–" : " ")} [{a.Score,3}] T{a.Tier} {a.Name}: {string.Join("; ", a.Reasons)}");
                if (s.NextOpponent is { } o)
                    Console.WriteLine($"  след. соперник: {Name(o.HeroCardId ?? "?")} (стол с хода {o.LastSeenTurn}): " +
                                      string.Join(", ", o.LastBoard.Select(m => $"{Name(m.CardId)} {m.Atk}/{m.Health}")));
            }
        }
        p2.Feed(line);
    }
}
File.Delete(tmp);
return 0;
