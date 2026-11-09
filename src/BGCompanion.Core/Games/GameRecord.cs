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
    public bool Complete { get; set; }
}
