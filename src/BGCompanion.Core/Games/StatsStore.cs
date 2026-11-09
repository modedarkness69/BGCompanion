using System.Text.Json;
using BGCompanion.Core.Cards;
using BGCompanion.Core.Logs;

namespace BGCompanion.Core.Games;

public sealed class HeroStats
{
    public string HeroId { get; init; } = "";
    public int Offered { get; set; }
    public int Picked { get; set; }
    public int Games { get; set; }
    public double AvgPlace { get; set; }
    public double Top4Rate { get; set; }
    public double WinRate { get; set; }
    public double PickRate => Offered > 0 ? (double)Picked / Offered : 0;
}

/// <summary>Локальная история игр: один JSON-объект на строку в games.jsonl.</summary>
public sealed class StatsStore
{
    private readonly List<GameRecord> _games = new();
    private readonly object _lock = new();
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    public string FilePath { get; }

    public StatsStore(string? path = null)
    {
        FilePath = path ?? Path.Combine(AppInfo.DataDir, "games.jsonl");
        Load();
    }

    public IReadOnlyList<GameRecord> Games
    {
        get { lock (_lock) return _games.ToList(); }
    }

    private void Load()
    {
        if (!File.Exists(FilePath)) return;
        foreach (var line in File.ReadLines(FilePath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var g = JsonSerializer.Deserialize<GameRecord>(line, Json);
                if (g != null) _games.Add(g);
            }
            catch (JsonException) { /* битая строка — пропускаем */ }
        }
    }

    private static string Key(GameRecord g) => $"{g.StartedAt:yyyyMMddHHmm}|{g.HeroCardId}";

    /// <summary>Добавляет игру, если её ещё нет. Возвращает true, если добавлена.</summary>
    public bool Add(GameRecord g)
    {
        if (g.HeroCardId == null) return false; // не видели выбор героя — статистике не поможет
        lock (_lock)
        {
            var key = Key(g);
            var existing = _games.FindIndex(x => Key(x) == key);
            if (existing >= 0)
            {
                // запись из повторного чтения лога — оставляем более полную
                var old = _games[existing];
                if ((g.Complete && !old.Complete) || (g.Boards.Count > 0 && old.Boards.Count == 0)) _games[existing] = g;
                else return false;
                Save();
                return true;
            }
            _games.Add(g);
            File.AppendAllText(FilePath, JsonSerializer.Serialize(g, Json) + "\n");
            return true;
        }
    }

    private void Save()
    {
        var tmp = FilePath + ".tmp";
        File.WriteAllLines(tmp, _games.Select(x => JsonSerializer.Serialize(x, Json)));
        File.Move(tmp, FilePath, overwrite: true);
    }

    public Dictionary<string, HeroStats> HeroTable(CardDb cards)
    {
        var table = new Dictionary<string, HeroStats>();
        HeroStats Get(string id)
        {
            var key = cards.BaseHeroId(id);
            if (!table.TryGetValue(key, out var s)) table[key] = s = new HeroStats { HeroId = key };
            return s;
        }

        var places = new Dictionary<string, List<int>>();
        foreach (var g in Games)
        {
            foreach (var h in g.OfferedHeroes.Distinct()) Get(h).Offered++;
            if (g.HeroCardId == null) continue;
            var s = Get(g.HeroCardId);
            s.Picked++;
            if (g.Complete && g.Placement > 0)
            {
                if (!places.TryGetValue(s.HeroId, out var list)) places[s.HeroId] = list = new List<int>();
                list.Add(g.Placement);
            }
        }
        foreach (var (id, list) in places)
        {
            var s = table[id];
            s.Games = list.Count;
            s.AvgPlace = list.Average();
            s.Top4Rate = list.Count(p => p <= 4) / (double)list.Count;
            s.WinRate = list.Count(p => p == 1) / (double)list.Count;
        }
        return table;
    }

    /// <summary>Импорт всех старых логов из папки Logs игры.</summary>
    public int ImportFromLogs(string installDir)
    {
        int added = 0;
        foreach (var (dir, started) in HearthstoneLocator.SessionDirs(installDir))
        {
            foreach (var file in HearthstoneLocator.PowerLogs(dir))
            {
                var parser = new PowerLogParser();
                parser.SetLogDate(started);
                parser.GameEnded += g => { if (Add(g)) added++; };
                using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                string? line;
                while ((line = sr.ReadLine()) != null) parser.Feed(line);
                parser.FinishIfAny(false);
            }
        }
        return added;
    }
}
