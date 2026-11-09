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
File.Delete(tmp);
return 0;
