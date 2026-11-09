using System.Windows;
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

    public event Action? StateChanged;
    public event Action? StatsChanged;
    public event Action<string>? Status;
    public event Action? UpdateFound;

    public AppCore()
    {
        Cards = new CardDb(Settings.CardLocale);
        Advisor = new Advisor(Cards, Store, Community);

        Parser.GameStarted += g => Ui(() =>
        {
            Game = g; HeroOffer = new(); HeroPickPhase = false; Tier = 1; Turn = 0;
            StateChanged?.Invoke();
        });
        Parser.HeroesOffered += (g, heroes) => Ui(() =>
        {
            if (!g.GameType.StartsWith("GT_BATTLEGROUNDS")) return;
            HeroOffer = Advisor.RankHeroes(heroes);
            HeroPickPhase = true;
            StateChanged?.Invoke();
        });
        Parser.HeroPicked += (_, _) => Ui(() => { HeroPickPhase = false; StateChanged?.Invoke(); });
        Parser.TavernTierChanged += (_, t) => Ui(() => { Tier = t; StateChanged?.Invoke(); });
        Parser.TurnChanged += (_, t) => Ui(() => { Turn = t; StateChanged?.Invoke(); });
        Parser.GameEnded += g => Ui(() =>
        {
            if (Store.Add(g))
            {
                Status?.Invoke(g.Complete
                    ? $"Игра записана: {Cards.NameOf(g.HeroCardId)}, {g.Placement}-е место"
                    : "Игра записана (не до конца)");
                StatsChanged?.Invoke();
            }
            Game = null; HeroPickPhase = false; HeroOffer = new(); Tier = 0; Turn = 0;
            StateChanged?.Invoke();
        });
    }

    private static void Ui(Action a) => Application.Current?.Dispatcher.BeginInvoke(a);

    public async Task StartAsync()
    {
        Cards.Load();
        Community.LoadCache();

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
            if (!Settings.ImportedOldLogs)
            {
                var n = await Task.Run(() => Store.ImportFromLogs(HearthstoneDir));
                Settings.ImportedOldLogs = true;
                Settings.Save();
                if (n > 0) Status?.Invoke($"Импортировано игр из старых логов: {n}");
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
