using System.Windows;
using System.Windows.Threading;
using BGCompanion.Core;
using BGCompanion.Core.Cards;
using BGCompanion.Core.Games;
using BGCompanion.Core.Logs;
using BGCompanion.Core.Update;

namespace BGCompanion;

/// <summary>Все сервисы программы и текущее состояние матча. События приходят в UI-потоке.</summary>
public sealed class AppCore : IDisposable
{
    public Settings Settings { get; } = Settings.Load();
    public CardDb Cards { get; }
    public StatsStore Store { get; } = new();
    public CommunityStats Community { get; } = new();
    public CompsDb CompsDb { get; } = new();
    public List<CompProgress> Comps { get; private set; } = new();
    public List<MinedComp> MinedComps { get; private set; } = new();
    public MetaMiner Miner { get; private set; } = null!;
    public PatchInfo? Patch { get; private set; }
    public Advisor Advisor { get; }
    public PowerLogParser Parser { get; } = new();
    public LogWatcher? Watcher { get; private set; }
    public string? HearthstoneDir { get; private set; }
    public ReleaseInfo? AvailableUpdate { get; private set; }

    // текущий матч
    public GameRecord? Game { get; private set; }
    public List<HeroAdvice> HeroOffer { get; private set; } = new();
    public bool HeroPickPhase { get; private set; }
    public int Tier { get; private set; }
    public int Turn { get; private set; }

    // таверна (обновляется таймером)
    public ShopAdvisor ShopAdvisor { get; }
    public BgSnapshot? Bg { get; private set; }
    public List<ShopAdvice> Shop { get; private set; } = new();
    public List<string> LobbyRaces { get; private set; } = new();
    public bool LobbyRacesComplete { get; private set; }
    /// <summary>Типы, известные точно: распознаны с экрана или отмечены вручную.</summary>
    public List<string> KnownRaces { get; private set; } = new();
    public string RacesSource { get; private set; } = "";
    public string RacesWarning { get; private set; } = "";
    public event Action? AskRaces;
    private int _gameSerial;
    private PowerLogParser? _demoParser;
    private string? _focusId;
    private readonly DispatcherTimer _bgTimer = new() { Interval = TimeSpan.FromMilliseconds(300) };

    public event Action? StateChanged;
    public event Action? BgChanged;
    public event Action? StatsChanged;
    public event Action<string>? Status;
    public event Action? UpdateFound;

    public AppCore()
    {
        Cards = new CardDb(Settings.CardLocale);
        Advisor = new Advisor(Cards, Store, Community);
        ShopAdvisor = new ShopAdvisor(Cards);
        Wire(Parser, record: true);
        _bgTimer.Tick += (_, _) => RefreshBg();
        _bgTimer.Start();
    }

    private void RefreshBg()
    {
        var p = IsDemo ? _demoParser : Parser;
        if (p == null || Game == null || Game.HeroCardId == null)
        {
            if (Bg != null) { Bg = null; Shop = new(); LobbyRaces = new(); Comps = new(); BgChanged?.Invoke(); }
            return;
        }
        Bg = p.Tracker.Snapshot();
        var fromShop = ShopAdvisor.LobbyRaces(Bg.SeenShopCards, out var complete);
        if (KnownRaces.Count >= 5)
        {
            LobbyRaces = KnownRaces.ToList();
            LobbyRacesComplete = true;
            var extra = fromShop.Where(r => !KnownRaces.Contains(r)).ToList();
            RacesWarning = extra.Count > 0
                ? "В таверне есть " + string.Join(", ", extra.Select(ShopAdvisor.RaceName)) + " — проверьте типы игры"
                : "";
        }
        else
        {
            LobbyRaces = KnownRaces.Concat(fromShop).Distinct().Take(5).ToList();
            LobbyRacesComplete = complete;
            RacesSource = KnownRaces.Count > 0 ? RacesSource : "таверна";
        }
        Comps = CompsDb.Rank(LobbyRaces, Bg.Board.Concat(Bg.Hand), Cards);
        // ведущая сборка «липкая»: меняем её, только если другая заметно лучше
        if (_focusId != null && Comps.Count > 1 && Comps[0].Comp.Id != _focusId)
        {
            var prev = Comps.FirstOrDefault(c => c.Comp.Id == _focusId);
            if (prev != null && prev.Score >= Comps[0].Score - 3)
            {
                Comps[0].Recommended = false;
                prev.Recommended = true;
                Comps.Remove(prev);
                Comps.Insert(0, prev);
            }
        }
        var focus = Comps.FirstOrDefault(c => c.Recommended);
        if (focus != null) _focusId = focus.Comp.Id;
        Shop = Bg.ShopPhase ? ShopAdvisor.Rate(Bg, Tier, focus) : new();
        BgChanged?.Invoke();
    }

    /// <summary>Подключает парсер к состоянию программы. Демо-парсер (record=false) ничего не сохраняет.</summary>
    private void Wire(PowerLogParser p, bool record)
    {
        bool Active() => record ? !IsDemo : IsDemo; // во время демо живой лог не перебивает оверлей

        p.GameStarted += g => Ui(() =>
        {
            if (!Active()) return;
            Game = g; HeroOffer = new(); HeroPickPhase = false; Tier = 1; Turn = 0;
            KnownRaces = new(); RacesSource = ""; RacesWarning = ""; _focusId = null;
            var serial = ++_gameSerial;
            if (record) _ = DetectRacesAsync(serial);
            StateChanged?.Invoke();
        });
        p.HeroesOffered += (g, heroes) => Ui(() =>
        {
            if (!Active() || !g.GameType.StartsWith("GT_BATTLEGROUNDS")) return;
            HeroOffer = Advisor.RankHeroes(heroes);
            HeroPickPhase = true;
            StateChanged?.Invoke();
        });
        p.HeroPicked += (_, _) => Ui(() => { if (Active()) { HeroPickPhase = false; StateChanged?.Invoke(); } });
        p.TavernTierChanged += (_, t) => Ui(() => { if (Active()) { Tier = t; StateChanged?.Invoke(); } });
        p.TurnChanged += (_, t) => Ui(() => { if (Active()) { Turn = t; StateChanged?.Invoke(); } });
        p.GameEnded += g => Ui(() =>
        {
            if (record && KnownRaces.Count >= 5) g.LobbyRaces = KnownRaces.ToList();
            if (record && Store.Add(g))
            {
                RefreshMined();
                Status?.Invoke(g.Complete
                    ? $"Игра записана: {Cards.NameOf(g.HeroCardId)}, {g.Placement}-е место"
                    : "Игра записана (не до конца)");
                StatsChanged?.Invoke();
            }
            if (!Active()) return;
            if (!record) Status?.Invoke($"Демо: игра окончена, место {g.Placement}");
            Game = null; HeroPickPhase = false; HeroOffer = new(); Tier = 0; Turn = 0;
            StateChanged?.Invoke();
        });
    }

    /// <summary>Пересчитать сборки из ваших игр (после каждой записанной игры).</summary>
    public void RefreshMined()
    {
        Miner ??= new MetaMiner(Cards);
        MinedComps = Miner.Mine(Store.Games);
        CompsDb.SetMined(MinedComps);
        MetaChanged?.Invoke();
    }

    public event Action? MetaChanged;

    // ---------- типы существ этой игры ----------
    public void SetRaces(IEnumerable<string> races, string source)
    {
        KnownRaces = races.Distinct().ToList();
        RacesSource = source;
        RacesWarning = "";
        Status?.Invoke($"Типы этой игры ({source}): " + string.Join(", ", KnownRaces.Select(ShopAdvisor.RaceName)));
        RefreshBg();
        StateChanged?.Invoke();
    }

    /// <summary>
    /// В начале матча игра пишет типы текстом — распознаём их со снимков окна.
    /// Работает до тех пор, пока не найдено 5 типов или не прошёл 2-й ход.
    /// </summary>
    private async Task DetectRacesAsync(int serial)
    {
        if (!Settings.RaceOcr || !RaceOcr.Available)
        {
            if (Settings.RaceOcr) Status?.Invoke("Распознавание текста Windows недоступно — отметьте типы вручную");
            _ = AskLaterAsync(serial);
            return;
        }
        var best = new List<string>();
        int shots = 0;
        var pickedAt = (DateTime?)null;
        while (serial == _gameSerial && Game != null && KnownRaces.Count < 5 && Turn <= 2)
        {
            await Task.Delay(1500);
            if (serial != _gameSerial || KnownRaces.Count >= 5) return;
            try
            {
                bool debug = Settings.RaceOcrDebugShots && shots < 6;
                var (races, _) = await RaceOcr.ScanOnceAsync(debug, $"game_{shots:00}.jpg");
                shots++;
                if (races.Count > best.Count) best = races;
                if (races.Count == 5)
                {
                    if (Settings.RaceOcrDebugShots) using (var b = RaceOcr.CaptureHearthstone()) if (b != null) RaceOcr.SaveDebug(b, "found.jpg");
                    SetRaces(races, "с экрана");
                    return;
                }
            }
            catch (Exception ex)
            {
                Status?.Invoke("Ошибка распознавания: " + ex.Message);
                break;
            }
            if (Game?.HeroCardId != null) pickedAt ??= DateTime.Now;
            if (pickedAt != null && DateTime.Now - pickedAt > TimeSpan.FromSeconds(20)) break;
        }
        if (serial != _gameSerial || KnownRaces.Count >= 5) return;
        if (best.Count >= 3) Status?.Invoke($"Распознано типов: {best.Count} из 5 — уточните вручную");
        if (Settings.AskRacesIfUnknown && Game != null) AskRaces?.Invoke();
    }

    private async Task AskLaterAsync(int serial)
    {
        // ждём выбора героя, потом спрашиваем
        for (int i = 0; i < 120 && serial == _gameSerial && Game?.HeroCardId == null; i++) await Task.Delay(1000);
        if (serial == _gameSerial && Game != null && KnownRaces.Count < 5 && Settings.AskRacesIfUnknown) AskRaces?.Invoke();
    }

    // ---------- демо-режим ----------
    public bool IsDemo { get; private set; }
    private CancellationTokenSource? _demoCts;

    public async Task RunDemoAsync()
    {
        if (IsDemo) { StopDemo(); return; }
        if (HearthstoneDir == null) { Status?.Invoke("Hearthstone не найден — демо проигрывает ваши старые логи"); return; }
        var log = await Task.Run(() => LogReplayer.FindDemoLog(HearthstoneDir));
        if (log == null) { Status?.Invoke("В логах нет полной игры Battlegrounds для демо"); return; }

        IsDemo = true;
        _demoCts = new CancellationTokenSource();
        Game = null; HeroOffer = new(); HeroPickPhase = false;
        StateChanged?.Invoke();
        DemoChanged?.Invoke();
        Status?.Invoke("Демо: проигрываю игру от " + log.Value.Date.ToString("dd.MM HH:mm"));
        var parser = new PowerLogParser();
        _demoParser = parser;
        Wire(parser, record: false);
        try
        {
            await Task.Run(() => new LogReplayer().PlayAsync(log.Value.File, log.Value.Date, parser, _demoCts.Token));
        }
        catch (OperationCanceledException) { Status?.Invoke("Демо остановлено"); }
        finally
        {
            await Task.Delay(300); // дать дойти последним событиям
            IsDemo = false;
            Game = null; HeroOffer = new(); HeroPickPhase = false; Tier = 0; Turn = 0;
            StateChanged?.Invoke();
            DemoChanged?.Invoke();
        }
    }

    public void StopDemo() => _demoCts?.Cancel();
    public event Action? DemoChanged;

    private static void Ui(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);

    public async Task StartAsync()
    {
        Cards.Load();
        Community.LoadCache();
        CompsDb.LoadCache();

        // 1. база карт (HearthstoneJSON) — обновляется, если вышел патч
        try
        {
            Status?.Invoke("Проверяю базу карт…");
            if (await Cards.SyncAsync()) { Cards.Load(); Status?.Invoke($"База карт обновлена: {Cards.Count} карт"); }
            else if (Cards.Count == 0) Cards.Load();
        }
        catch (Exception ex)
        {
            Status?.Invoke(Cards.Count > 0 ? "Нет связи с HearthstoneJSON, использую сохранённую базу карт" : "Не удалось скачать базу карт: " + ex.Message);
        }

        // 2. общая статистика героев из репозитория проекта
        try { if (await Community.SyncAsync()) StatsChanged?.Invoke(); }
        catch { /* файла пока нет или нет сети — работаем на личной статистике */ }
        try { await CompsDb.SyncAsync(); }
        catch { /* нет сети — используем сохранённые сборки */ }
        CompsDb.Validate(Cards);
        Patch = PatchDiff.Load();
        CompsDb.SetPatch(Patch);
        RefreshMined();
        if (CompsDb.RemovedCards > 0 || CompsDb.HiddenComps > 0)
            Status?.Invoke($"Сборки сверены с патчем: убрано выбывших карт — {CompsDb.RemovedCards}, скрыто сборок — {CompsDb.HiddenComps}");

        // 3. игра и логи
        HearthstoneDir = HearthstoneLocator.FindInstallDir(Settings.HearthstoneDir);
        if (HearthstoneDir == null)
        {
            Status?.Invoke("Hearthstone не найден — укажите папку игры в настройках");
        }
        else
        {
            if (HearthstoneLocator.EnsureLogConfig())
                Status?.Invoke("Включил запись логов Hearthstone — перезапустите игру один раз");
            const int importVersion = 2; // 2: итоговые столы всех игроков
            if (!Settings.ImportedOldLogs || Settings.ImportVersion < importVersion)
            {
                var n = await Task.Run(() => Store.ImportFromLogs(HearthstoneDir));
                Settings.ImportedOldLogs = true;
                Settings.ImportVersion = importVersion;
                Settings.Save();
                if (n > 0) Status?.Invoke($"Импортировано игр из старых логов: {n}");
                RefreshMined(); // в импортированных играх есть итоговые столы
            }
            StartWatcher();
        }
        StatsChanged?.Invoke();
        StateChanged?.Invoke();

        if (Settings.CheckUpdatesOnStart) _ = CheckUpdatesAsync(false);
    }

    public void StartWatcher()
    {
        Watcher?.Dispose();
        if (HearthstoneDir == null) return;
        Watcher = new LogWatcher(HearthstoneDir, Parser);
        Watcher.Status += s => Ui(() => Status?.Invoke(s));
        Watcher.Start();
    }

    public void SetHearthstoneDir(string dir)
    {
        Settings.HearthstoneDir = dir;
        Settings.Save();
        HearthstoneDir = HearthstoneLocator.FindInstallDir(dir);
        StartWatcher();
        StateChanged?.Invoke();
    }

    public async Task<int> ImportOldLogsAsync()
    {
        if (HearthstoneDir == null) return 0;
        var n = await Task.Run(() => Store.ImportFromLogs(HearthstoneDir));
        RefreshMined();
        StatsChanged?.Invoke();
        return n;
    }

    public async Task CheckUpdatesAsync(bool manual)
    {
        try
        {
            var r = await UpdateService.GetLatestAsync();
            if (r != null && UpdateService.IsNewer(r) && r.AssetUrl != null)
            {
                AvailableUpdate = r;
                UpdateFound?.Invoke();
            }
            else if (manual) Status?.Invoke($"У вас последняя версия ({AppInfo.VersionText})");
        }
        catch (Exception ex)
        {
            if (manual) Status?.Invoke("Не удалось проверить обновления: " + ex.Message);
        }
    }

    public void Dispose() => Watcher?.Dispose();
}
