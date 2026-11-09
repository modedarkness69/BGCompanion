using System.Text.RegularExpressions;

namespace BGCompanion.Core.Logs;

public sealed record MinionView(int EntityId, string CardId, int Atk, int Health, int Position, bool Golden);

public sealed class OpponentInfo
{
    public int PlayerId { get; init; }
    public string? HeroCardId { get; set; }
    public List<MinionView> LastBoard { get; set; } = new();
    public int LastSeenTurn { get; set; }     // ход таверны, когда видели стол
}

/// <summary>Снимок состояния матча Battlegrounds для интерфейса (неизменяемый).</summary>
public sealed class BgSnapshot
{
    public int Turn { get; init; }
    public bool ShopPhase { get; init; }
    public IReadOnlyList<MinionView> Shop { get; init; } = Array.Empty<MinionView>();
    public IReadOnlyList<MinionView> Board { get; init; } = Array.Empty<MinionView>();
    public IReadOnlyList<MinionView> Hand { get; init; } = Array.Empty<MinionView>();
    public IReadOnlyList<string> SeenShopCards { get; init; } = Array.Empty<string>();
    public OpponentInfo? NextOpponent { get; init; }
    public IReadOnlyDictionary<int, OpponentInfo> Opponents { get; init; } = new Dictionary<int, OpponentInfo>();
}

/// <summary>
/// Модель сущностей из Power.log (только строки GameState.DebugPrintPower).
/// Знает, что лежит в таверне, на столе, в руке, и запоминает столы соперников в боях.
/// Имена других игроков не сохраняются.
/// </summary>
public sealed partial class EntityTracker
{
    [GeneratedRegex(@"ID=(\d+) CardID=(\S*)")] private static partial Regex CreateRx();
    [GeneratedRegex(@"Entity=(.+?) CardID=(\S*)")] private static partial Regex UpdateRx();
    [GeneratedRegex(@"^tag=(\S+) value=(\S*)")] private static partial Regex TagRx();
    [GeneratedRegex(@"^TAG_CHANGE Entity=(.+?) tag=(\S+) value=(\S*)")] private static partial Regex TagChangeRx();
    [GeneratedRegex(@"^Player EntityID=(\d+) PlayerID=(\d+)")] private static partial Regex PlayerRx();
    [GeneratedRegex(@"^\[entityName=.*? id=(\d+) zone=\w+ zonePos=\d+ cardId=(\S*) player=(\d+)\]$")] private static partial Regex EntityRefRx();

    private sealed class Ent
    {
        public string CardId = "";
        public readonly Dictionary<string, string> Tags = new();
        public string Tag(string k) => Tags.TryGetValue(k, out var v) ? v : "";
        public int Int(string k) => int.TryParse(Tag(k), out var v) ? v : 0;
    }

    private readonly object _lock = new();
    private readonly Dictionary<int, Ent> _ents = new();
    private readonly Dictionary<int, int> _playerEntityByPid = new();   // PlayerID -> EntityID
    private readonly Dictionary<int, OpponentInfo> _opponents = new();
    private readonly HashSet<string> _seenShop = new();
    private int? _cur;
    private int _turnTag;
    private int _combatOpponent;
    private bool _combatCaptured;
    private int _nextOpponent;

    public int LocalPlayerId { get; set; }
    public string? LocalName { get; set; }

    /// <summary>Вызывается при любом изменении, важном для интерфейса.</summary>
    public event Action? Changed;

    public void Reset()
    {
        lock (_lock)
        {
            _ents.Clear(); _playerEntityByPid.Clear(); _opponents.Clear(); _seenShop.Clear();
            _cur = null; _turnTag = 0; _combatOpponent = 0; _combatCaptured = false; _nextOpponent = 0;
            LocalPlayerId = 0; LocalName = null;
        }
    }

    private int BobPlayerId => _playerEntityByPid.Keys.FirstOrDefault(p => p != LocalPlayerId);

    private Ent Get(int id)
    {
        if (!_ents.TryGetValue(id, out var e)) _ents[id] = e = new Ent();
        return e;
    }

    private int? Resolve(string s)
    {
        if (s == "GameEntity") return 1;
        if (int.TryParse(s, out var id)) return id;
        var m = EntityRefRx().Match(s);
        if (m.Success)
        {
            var i = int.Parse(m.Groups[1].Value);
            var e = Get(i);
            if (e.CardId.Length == 0) e.CardId = m.Groups[2].Value;
            return i;
        }
        // имя игрока: своё — наш Player, любое другое — сторона противника (Боб / соперник в бою)
        if (LocalName != null && s == LocalName && _playerEntityByPid.TryGetValue(LocalPlayerId, out var mine)) return mine;
        if (_playerEntityByPid.TryGetValue(BobPlayerId, out var bob)) return bob;
        return null;
    }

    /// <summary>Тело строки после "GameState.DebugPrintPower() - " (с отступами).</summary>
    public void Process(string body)
    {
        bool changed;
        lock (_lock) changed = ProcessLocked(body);
        if (changed) Changed?.Invoke();
    }

    /// <summary>Закончился блок тегов сущности — если это существо в таверне, запомнить его тип.</summary>
    private void FlushCurrent()
    {
        if (_cur is int c && _ents.TryGetValue(c, out var e)) NoteShop(e);
    }

    private void NoteShop(Ent e)
    {
        if (_turnTag % 2 == 1 && e.Tag("ZONE") == "PLAY" && e.Tag("CARDTYPE") == "MINION"
            && e.Int("IS_BACON_POOL_MINION") == 1 && e.Int("CONTROLLER") == BobPlayerId && e.CardId.Length > 0)
            _seenShop.Add(e.CardId);
    }

    private bool ProcessLocked(string body)
    {
        bool changed = false;
        {
            var t = body.TrimStart();
            if (!t.StartsWith("tag=", StringComparison.Ordinal)) FlushCurrent();
            if (t.StartsWith("FULL_ENTITY - Creating", StringComparison.Ordinal))
            {
                var m = CreateRx().Match(t);
                if (m.Success)
                {
                    var id = int.Parse(m.Groups[1].Value);
                    var e = new Ent { CardId = m.Groups[2].Value };
                    _ents[id] = e;
                    _cur = id;
                }
                return changed;
            }
            if (t.StartsWith("SHOW_ENTITY - Updating", StringComparison.Ordinal) || t.StartsWith("CHANGE_ENTITY - Updating", StringComparison.Ordinal))
            {
                var m = UpdateRx().Match(t);
                _cur = m.Success ? Resolve(m.Groups[1].Value) : null;
                if (_cur is int c && m.Groups[2].Value.Length > 0) Get(c).CardId = m.Groups[2].Value;
                changed = true;
                return changed;
            }
            if (t.StartsWith("Player EntityID=", StringComparison.Ordinal))
            {
                var m = PlayerRx().Match(t);
                if (m.Success)
                {
                    var eid = int.Parse(m.Groups[1].Value);
                    var pid = int.Parse(m.Groups[2].Value);
                    _playerEntityByPid[pid] = eid;
                    _cur = eid;
                    Get(eid).Tags["PLAYER_ID"] = pid.ToString();
                }
                return changed;
            }
            if (t.StartsWith("GameEntity EntityID=", StringComparison.Ordinal)) { _cur = 1; return changed; }
            if (t.StartsWith("tag=", StringComparison.Ordinal))
            {
                if (_cur is int c)
                {
                    var m = TagRx().Match(t);
                    if (m.Success) Get(c).Tags[m.Groups[1].Value] = m.Groups[2].Value;
                    changed = true;
                }
                return changed;
            }
            if (t.StartsWith("BLOCK_START BlockType=ATTACK", StringComparison.Ordinal))
            {
                _cur = null;
                if (!_combatCaptured && _turnTag > 0 && _turnTag % 2 == 0) CaptureCombat();
                return changed;
            }
            if (t.StartsWith("TAG_CHANGE", StringComparison.Ordinal))
            {
                _cur = null;
                var m = TagChangeRx().Match(t);
                if (!m.Success) return changed;
                var target = Resolve(m.Groups[1].Value);
                var tag = m.Groups[2].Value;
                var val = m.Groups[3].Value;
                if (target is not int id) return changed;
                Get(id).Tags[tag] = val;

                switch (tag)
                {
                    case "TURN" when id == 1:
                        int.TryParse(val, out _turnTag);
                        _combatCaptured = false;
                        changed = true;
                        break;
                    case "BACON_CURRENT_COMBAT_PLAYER_ID":
                        if (int.TryParse(val, out var opp) && opp != 0 && opp != LocalPlayerId) _combatOpponent = opp;
                        break;
                    case "NEXT_OPPONENT_PLAYER_ID":
                        if (Get(id).Int("CONTROLLER") == LocalPlayerId && int.TryParse(val, out var next))
                        {
                            _nextOpponent = next;
                            changed = true;
                        }
                        break;
                    case "ZONE":
                    case "CONTROLLER":
                        NoteShop(Get(id));
                        changed = true;
                        break;
                    case "ZONE_POSITION":
                    case "ATK":
                    case "HEALTH":
                    case "PREMIUM":
                        changed = true;
                        break;
                }
                return changed;
            }
            _cur = null;
        }
        return changed;
    }

    private void CaptureCombat()
    {
        _combatCaptured = true;
        if (_combatOpponent == 0) return;
        var info = Opponent(_combatOpponent);
        info.LastBoard = Minions(BobPlayerId, "PLAY");
        info.LastSeenTurn = (_turnTag + 1) / 2;
    }

    private OpponentInfo Opponent(int pid)
    {
        if (!_opponents.TryGetValue(pid, out var o)) _opponents[pid] = o = new OpponentInfo { PlayerId = pid };
        // герой соперника: сущность-герой с тегом PLAYER_ID = pid
        foreach (var e in _ents.Values)
        {
            if (e.Tag("CARDTYPE") == "HERO" && e.Int("PLAYER_ID") == pid && e.CardId.Length > 0 && !e.CardId.EndsWith("_PH", StringComparison.Ordinal))
                o.HeroCardId = e.CardId;
        }
        return o;
    }

    private List<MinionView> Minions(int controller, string zone)
    {
        var list = new List<MinionView>();
        foreach (var (id, e) in _ents)
        {
            if (e.Tag("CARDTYPE") != "MINION" || e.Tag("ZONE") != zone || e.Int("CONTROLLER") != controller || e.CardId.Length == 0) continue;
            list.Add(new MinionView(id, e.CardId, e.Int("ATK"), e.Int("HEALTH") - e.Int("DAMAGE"), e.Int("ZONE_POSITION"), e.Int("PREMIUM") == 1));
        }
        list.Sort((a, b) => a.Position.CompareTo(b.Position));
        return list;
    }

    public BgSnapshot Snapshot()
    {
        lock (_lock)
        {
            bool shop = _turnTag % 2 == 1;
            var shopList = shop && BobPlayerId != 0 ? Minions(BobPlayerId, "PLAY") : new List<MinionView>();
            OpponentInfo? next = null;
            if (_nextOpponent != 0)
            {
                next = Opponent(_nextOpponent);
            }
            return new BgSnapshot
            {
                Turn = (_turnTag + 1) / 2,
                ShopPhase = shop,
                Shop = shopList,
                Board = LocalPlayerId != 0 ? Minions(LocalPlayerId, "PLAY") : new List<MinionView>(),
                Hand = LocalPlayerId != 0 ? Minions(LocalPlayerId, "HAND") : new List<MinionView>(),
                SeenShopCards = _seenShop.ToList(),
                NextOpponent = next,
                Opponents = _opponents.ToDictionary(k => k.Key, v => v.Value),
            };
        }
    }
}
