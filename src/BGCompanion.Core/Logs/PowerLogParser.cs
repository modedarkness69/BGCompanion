using System.Globalization;
using System.Text.RegularExpressions;
using BGCompanion.Core.Games;

namespace BGCompanion.Core.Logs;

/// <summary>
/// Разбор Power.log построчно. Используются только строки GameState.* — сервер присылает их
/// сразу, без задержки анимаций (строки PowerTaskList.* дублируют их и пропускаются).
/// </summary>
public sealed partial class PowerLogParser
{
    [GeneratedRegex(@"^D (?<time>\d\d:\d\d:\d\d\.\d+) GameState\.(?<method>\w+)\(\) - (?<body>.*)$")]
    private static partial Regex LineRx();

    [GeneratedRegex(@"\[entityName=(?<name>.*?) id=(?<id>\d+) zone=(?<zone>\w+) zonePos=\d+ cardId=(?<card>\S*) player=(?<player>\d+)\]")]
    private static partial Regex EntityRx();

    [GeneratedRegex(@"^\s*TAG_CHANGE Entity=(?<ent>.+?) tag=(?<tag>\S+) value=(?<val>\S+)")]
    private static partial Regex TagChangeRx();

    [GeneratedRegex(@"^id=(?<id>\d+) Player=(?<player>.+?) TaskList=\S* ChoiceType=(?<type>\w+)")]
    private static partial Regex ChoicesHeaderRx();

    [GeneratedRegex(@"^id=(?<id>\d+) ChoiceType=(?<type>\w+)")]
    private static partial Regex SendHeaderRx();

    [GeneratedRegex(@"^PlayerID=(?<id>\d+), PlayerName=(?<name>.+)$")]
    private static partial Regex PlayerRx();

    // ---- события для интерфейса ----
    public event Action<GameRecord>? GameStarted;
    public event Action<GameRecord, IReadOnlyList<string>>? HeroesOffered;
    public event Action<GameRecord, string>? HeroPicked;
    public event Action<GameRecord, int>? TavernTierChanged;
    public event Action<GameRecord, int>? TurnChanged;
    public event Action<GameRecord, ChoiceRecord>? ChoiceOffered;
    public event Action<GameRecord>? GameEnded;

    public GameRecord? Current { get; private set; }

    /// <summary>Таверна, стол, рука и соперники текущего матча.</summary>
    public EntityTracker Tracker { get; } = new();

    private readonly Dictionary<string, int> _playerIds = new();
    private string? _localName;
    private int _localId;
    private ChoiceRecord? _pendingChoice;      // последнее предложение выбора
    private string? _sendType;                 // тип выбора в SendChoices
    private DateTime _day = DateTime.Today;
    private TimeSpan _lastTime;

    /// <summary>Дата начала лога (из имени папки сессии), чтобы время игр было правильным.</summary>
    public void SetLogDate(DateTime date)
    {
        _day = date.Date;
        _lastTime = TimeSpan.Zero;
    }

    public void Feed(string line)
    {
        var m = LineRx().Match(line);
        if (!m.Success) return;
        var method = m.Groups["method"].Value;
        var body = m.Groups["body"].Value;
        var time = ParseTime(m.Groups["time"].Value);

        switch (method)
        {
            case "DebugPrintPower":
                OnPower(body, time);
                break;
            case "DebugPrintGame":
                OnGame(body);
                break;
            case "DebugPrintEntityChoices":
                OnEntityChoices(body);
                break;
            case "SendChoices":
                OnSendChoices(body);
                break;
        }
    }

    private DateTime ParseTime(string s)
    {
        if (!TimeSpan.TryParseExact(s.Length > 16 ? s[..16] : s, @"hh\:mm\:ss\.fffffff", CultureInfo.InvariantCulture, out var t)
            && !TimeSpan.TryParse(s.Split('.')[0], CultureInfo.InvariantCulture, out t))
            return _day;
        if (t < _lastTime - TimeSpan.FromHours(1)) _day = _day.AddDays(1); // переход через полночь
        _lastTime = t;
        return _day + t;
    }

    private void OnPower(string body, DateTime time)
    {
        var trimmed = body.TrimStart();
        if (trimmed == "CREATE_GAME")
        {
            FinishIfAny(false);
            Tracker.Reset();
            Current = new GameRecord { StartedAt = time };
            _playerIds.Clear();
            _localName = null;
            _localId = 0;
            _pendingChoice = null;
            GameStarted?.Invoke(Current);
            return;
        }
        if (Current == null) return;
        Tracker.Process(body);

        var tc = TagChangeRx().Match(trimmed);
        if (!tc.Success) return;
        var ent = tc.Groups["ent"].Value;
        var tag = tc.Groups["tag"].Value;
        var val = tc.Groups["val"].Value;

        if (ent == "GameEntity")
        {
            if (tag == "TURN" && int.TryParse(val, out var turn))
            {
                var shopTurn = (turn + 1) / 2;
                if (shopTurn > Current.Turns)
                {
                    Current.Turns = shopTurn;
                    TurnChanged?.Invoke(Current, shopTurn);
                }
            }
            else if (tag == "STATE" && val == "COMPLETE")
            {
                Current.EndedAt = time;
                FinishIfAny(true);
            }
            return;
        }

        var em = EntityRx().Match(ent);
        int player = em.Success ? int.Parse(em.Groups["player"].Value) : 0;
        bool isLocal = _localId != 0 && (player == _localId || ent == _localName);
        if (!isLocal) return;

        switch (tag)
        {
            case "PLAYER_TECH_LEVEL":
                if (int.TryParse(val, out var tier) && tier > Current.FinalTavernTier)
                {
                    Current.FinalTavernTier = tier;
                    if (tier > 1) Current.TierUpTurns[tier] = Current.Turns;
                    TavernTierChanged?.Invoke(Current, tier);
                }
                break;
            case "PLAYER_LEADERBOARD_PLACE":
                if (int.TryParse(val, out var place) && place > 0) Current.Placement = place;
                break;
        }
    }

    private void OnGame(string body)
    {
        if (Current == null) return;
        if (body.StartsWith("GameType=")) Current.GameType = body["GameType=".Length..].Trim();
        else if (body.StartsWith("BuildNumber=") && int.TryParse(body["BuildNumber=".Length..], out var b)) Current.BuildNumber = b;
        else
        {
            var pm = PlayerRx().Match(body);
            if (pm.Success)
            {
                _playerIds[pm.Groups["name"].Value.Trim()] = int.Parse(pm.Groups["id"].Value);
                ResolveLocal();
            }
        }
    }

    private void ResolveLocal()
    {
        if (_localName != null && _playerIds.TryGetValue(_localName, out var id)) _localId = id;
        Tracker.LocalName = _localName;
        if (_localId != 0) Tracker.LocalPlayerId = _localId;
    }

    private void OnEntityChoices(string body)
    {
        if (Current == null) return;
        var t = body.TrimStart();
        var h = ChoicesHeaderRx().Match(t);
        if (h.Success)
        {
            _localName ??= h.Groups["player"].Value.Trim();
            ResolveLocal();
            _pendingChoice = new ChoiceRecord
            {
                Kind = h.Groups["type"].Value == "MULLIGAN" ? "hero" : "other",
                Turn = Current.Turns,
            };
            return;
        }
        if (_pendingChoice == null) return;

        if (t.StartsWith("Source="))
        {
            var sm = EntityRx().Match(t);
            if (sm.Success)
            {
                var src = sm.Groups["card"].Value;
                _pendingChoice.SourceCardId = src;
                if (_pendingChoice.Kind != "hero")
                {
                    if (src.Contains("Trinket", StringComparison.OrdinalIgnoreCase)) _pendingChoice.Kind = "trinket";
                    else if (src.Contains("MidGameEffect", StringComparison.OrdinalIgnoreCase)) _pendingChoice.Kind = "darkgift";
                }
            }
        }
        else if (t.StartsWith("Entities["))
        {
            var em = EntityRx().Match(t);
            if (em.Success)
            {
                _pendingChoice.Offered.Add(em.Groups["card"].Value);
                if (_pendingChoice.Kind == "hero" && _localId == 0)
                {
                    _localId = int.Parse(em.Groups["player"].Value);
                    Tracker.LocalPlayerId = _localId;
                }
            }
            // все варианты героев пришли — сообщаем интерфейсу (после 4-го или при следующей строке)
            if (_pendingChoice.Kind == "hero" && _pendingChoice.Offered.Count >= 2)
            {
                Current.OfferedHeroes = new List<string>(_pendingChoice.Offered);
                HeroesOffered?.Invoke(Current, Current.OfferedHeroes);
            }
        }
    }

    private void OnSendChoices(string body)
    {
        if (Current == null) return;
        var t = body.TrimStart();
        var h = SendHeaderRx().Match(t);
        if (h.Success)
        {
            _sendType = h.Groups["type"].Value;
            return;
        }
        if (!t.StartsWith("m_chosenEntities[")) return;
        var em = EntityRx().Match(t);
        if (!em.Success) return;
        var card = em.Groups["card"].Value;

        if (_sendType == "MULLIGAN")
        {
            Current.HeroCardId = card;
            if (Current.OfferedHeroes.Count == 0 && _pendingChoice != null)
                Current.OfferedHeroes = new List<string>(_pendingChoice.Offered);
            HeroPicked?.Invoke(Current, card);
        }
        if (_pendingChoice != null && _pendingChoice.Chosen == null)
        {
            _pendingChoice.Chosen = card;
            if (_pendingChoice.Kind != "hero")
            {
                Current.Choices.Add(_pendingChoice);
                ChoiceOffered?.Invoke(Current, _pendingChoice);
            }
        }
    }

    /// <summary>Завершить текущую игру (например, при конце лога).</summary>
    public void FinishIfAny(bool complete)
    {
        if (Current == null) return;
        Current.Complete = complete;
        var g = Current;
        Current = null;
        if (g.GameType.StartsWith("GT_BATTLEGROUNDS", StringComparison.Ordinal))
            GameEnded?.Invoke(g);
    }
}
