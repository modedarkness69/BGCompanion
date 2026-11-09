namespace BGCompanion.Core.Games;

/// <summary>Выбор из нескольких вариантов (герой, аксессуар, Тёмный дар, сила героя и т.п.).</summary>
public sealed class ChoiceRecord
{
    public string Kind { get; set; } = "";          // hero / trinket / darkgift / other
    public string SourceCardId { get; set; } = "";
    public List<string> Offered { get; set; } = new();
    public string? Chosen { get; set; }
    public int Turn { get; set; }
}

/// <summary>Итоговый стол игрока (для собственной статистики сборок).</summary>
public sealed class BoardRecord
{
    public bool Mine { get; set; }
    public string? Hero { get; set; }
    public int Place { get; set; }
    public int Turn { get; set; }                       // ход, на котором стол видели последний раз
    public List<string> Cards { get; set; } = new();    // id существ (золотые — как есть)
}

/// <summary>Одна игра Battlegrounds. Хранится локально, без имён игроков.</summary>
public sealed class GameRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string GameType { get; set; } = "";
    public int BuildNumber { get; set; }
    public List<string> OfferedHeroes { get; set; } = new();
    public string? HeroCardId { get; set; }
    public int Placement { get; set; }               // 0 — неизвестно
    public int FinalTavernTier { get; set; }
    public int Turns { get; set; }                   // ходы таверны
    public Dictionary<int, int> TierUpTurns { get; set; } = new(); // тир -> ход, на котором взят
    public List<ChoiceRecord> Choices { get; set; } = new();
    public string? AnomalyCardId { get; set; }
    public List<string> LobbyRaces { get; set; } = new();     // типы игры (если известны точно)
    public List<string> SeenShop { get; set; } = new();       // существа, встреченные в таверне
    public List<BoardRecord> Boards { get; set; } = new();    // итоговые столы всех игроков
    public bool Complete { get; set; }
    /// <summary>Игра из чужих логов: идёт только в мету, не в вашу личную статистику.</summary>
    public bool External { get; set; }
    public string? Source { get; set; }
}
