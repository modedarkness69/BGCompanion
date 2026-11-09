using System.Diagnostics;
using System.Windows;
using BGCompanion.Core;
using BGCompanion.Core.Games;

namespace BGCompanion;

public partial class MainWindow : Window
{
    private AppCore Core => App.Core;
    private bool _loading = true; // до конца LoadOptions события ползунков игнорируются

    public MainWindow()
    {
        InitializeComponent();
        VersionText.Text = "v" + AppInfo.VersionText;
        AboutVersion.Text = "Версия " + AppInfo.VersionText;

        Core.Status += s => StatusText.Text = s;
        Core.StateChanged += RefreshState;
        Core.StatsChanged += RefreshStats;
        Core.DemoChanged += OnDemoChanged;
        Core.UpdateFound += () =>
        {
            UpdateText.Text = $"Доступна версия {Core.AvailableUpdate!.Version.ToString(3)}";
            UpdateBanner.Visibility = Visibility.Visible;
        };
        LoadOptions();
    }

    // ---------- обзор ----------
    private void RefreshState()
    {
        StHs.Text = Core.HearthstoneDir != null ? "Hearthstone: " + Core.HearthstoneDir : "Hearthstone не найден — укажите папку в настройках";
        StLog.Text = Core.Watcher?.CurrentFile != null ? "Лог: " + Core.Watcher.CurrentFile : "Лог: ждём запуска игры";
        StCards.Text = Core.Cards.Count > 0
            ? $"База карт: {Core.Cards.Count} карт ({Core.Cards.PoolMinions.Count()} существ BG), от {Core.Cards.SourceDate:dd.MM.yyyy}"
            : "База карт не загружена";
        StCommunity.Text = Core.Community.HeroCount > 0
            ? $"Общая статистика: {Core.Community.HeroCount} героев, обновлено {Core.Community.Updated}"
            : "Общая статистика: пока нет данных (используется личная)";
        StGames.Text = $"Записано игр: {Core.Store.Games.Count}";

        if (Core.Game == null)
        {
            GameHero.Text = "Нет активного матча";
            GameInfo.Text = "Запустите Battlegrounds — данные появятся автоматически.";
            GameTip.Text = "";
            OfferList.ItemsSource = null;
            return;
        }
        var g = Core.Game;
        GameHero.Text = g.HeroCardId != null ? Core.Cards.NameOf(g.HeroCardId) : (Core.HeroPickPhase ? "Выбор героя…" : "Матч начался");
        GameInfo.Text = Core.Turn > 0 ? $"Ход {Core.Turn} · таверна T{Core.Tier}" : "";
        GameTip.Text = Core.Settings.ShowTavernTips && g.HeroCardId != null ? Core.Advisor.TavernTip(Core.Turn, Core.Tier) : "";
        OfferList.ItemsSource = Core.HeroOffer.Select(Describe).ToList();
    }

    public static string Describe(HeroAdvice h)
    {
        var parts = new List<string>();
        if (h.Personal is { Games: > 0 } p)
            parts.Add($"вы: {p.Games} игр, ср. место {p.AvgPlace:0.0}, топ-4 {p.Top4Rate:P0}");
        if (h.Community != null)
            parts.Add($"все: ср. место {h.Community.AvgPlace:0.00}, топ-4 {h.Community.Top4Rate:P0}");
        var stats = parts.Count > 0 ? string.Join(" · ", parts) : "нет данных";
        return (h.Recommended ? "★ " : "   ") + h.Name + " — " + stats;
    }

    // ---------- статистика ----------
    private void RefreshStats()
    {
        var table = Core.Store.HeroTable(Core.Cards);
        HeroGrid.ItemsSource = table.Values
            .OrderBy(s => s.Games > 0 ? s.AvgPlace : 99).ThenByDescending(s => s.Offered)
            .Select(s => new
            {
                Name = Core.Cards.NameOf(s.HeroId),
                s.Offered,
                s.Picked,
                PickRate = s.PickRate.ToString("P0"),
                s.Games,
                AvgPlace = s.Games > 0 ? s.AvgPlace.ToString("0.00") : "—",
                Top4 = s.Games > 0 ? s.Top4Rate.ToString("P0") : "—",
                Wins = s.Games > 0 ? s.WinRate.ToString("P0") : "—",
                Community = Core.Community.Get(s.HeroId) is { } c ? c.AvgPlace.ToString("0.00") : "—",
            }).ToList();

        HistoryGrid.ItemsSource = Core.Store.Games.OrderByDescending(g => g.StartedAt).Select(g => new
        {
            Date = g.StartedAt.ToString("dd.MM HH:mm"),
            Hero = Core.Cards.NameOf(g.HeroCardId),
            Place = g.Complete && g.Placement > 0 ? g.Placement.ToString() : "—",
            Tier = g.FinalTavernTier > 0 ? "T" + g.FinalTavernTier : "—",
            g.Turns,
            Curve = string.Join(" ", g.TierUpTurns.OrderBy(k => k.Key).Select(k => $"T{k.Key}:{k.Value}")),
            Offered = string.Join(", ", g.OfferedHeroes.Select(Core.Cards.NameOf)),
        }).ToList();
        RefreshState();
    }

    // ---------- кнопки ----------
    private async void Demo_Click(object sender, RoutedEventArgs e) => await Core.RunDemoAsync();

    private void OnDemoChanged() =>
        DemoButton.Content = Core.IsDemo ? "Остановить демо" : "Демо: проиграть прошлую игру";

    private async void Import_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Импортирую старые логи…";
        var n = await Core.ImportOldLogsAsync();
        StatusText.Text = n > 0 ? $"Добавлено игр: {n}" : "Новых игр в логах не найдено";
    }

    private async void Sync_Click(object sender, RoutedEventArgs e)
    {
        StatusText.Text = "Обновляю базу…";
        try
        {
            var cardsUpdated = await Core.Cards.SyncAsync();
            if (cardsUpdated) Core.Cards.Load();
            try { await Core.Community.SyncAsync(); } catch { }
            StatusText.Text = cardsUpdated ? "База карт обновлена" : "База карт актуальна";
            RefreshStats();
        }
        catch (Exception ex) { StatusText.Text = "Ошибка: " + ex.Message; }
    }

    private void DataDir_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo("explorer.exe", AppInfo.DataDir) { UseShellExecute = true });

    private void Update_Click(object sender, RoutedEventArgs e) => App.LaunchUpdater();

    private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await Core.CheckUpdatesAsync(true);

    private void GitHub_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo($"https://github.com/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}") { UseShellExecute = true });

    // ---------- настройки ----------
    private void LoadOptions()
    {
        _loading = true;
        var s = Core.Settings;
        OptOverlay.IsChecked = s.OverlayEnabled;
        OptHeroPanel.IsChecked = s.ShowHeroPickPanel;
        OptTips.IsChecked = s.ShowTavernTips;
        OptUpdates.IsChecked = s.CheckUpdatesOnStart;
        OptMinimized.IsChecked = s.StartMinimized;
        OptOpacity.Value = s.OverlayOpacity;
        OptScale.Value = s.OverlayScale;
        OptDir.Text = s.HearthstoneDir ?? "(автопоиск)";
        _loading = false;
    }

    private void SaveOptions()
    {
        if (_loading) return;
        var s = Core.Settings;
        s.OverlayEnabled = OptOverlay.IsChecked == true;
        s.ShowHeroPickPanel = OptHeroPanel.IsChecked == true;
        s.ShowTavernTips = OptTips.IsChecked == true;
        s.CheckUpdatesOnStart = OptUpdates.IsChecked == true;
        s.StartMinimized = OptMinimized.IsChecked == true;
        s.OverlayOpacity = OptOpacity.Value;
        s.OverlayScale = OptScale.Value;
        s.Save();
        RefreshState();
        OverlayWindow.Instance?.ApplySettings();
    }

    private void Option_Changed(object sender, RoutedEventArgs e) => SaveOptions();
    private void Slider_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) => SaveOptions();

    private void PickDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Папка Hearthstone (где лежит Hearthstone.exe)" };
        if (dlg.ShowDialog(this) != true) return;
        if (!System.IO.File.Exists(System.IO.Path.Combine(dlg.FolderName, "Hearthstone.exe")))
        {
            MessageBox.Show("В этой папке нет Hearthstone.exe.", AppInfo.Name);
            return;
        }
        Core.SetHearthstoneDir(dlg.FolderName);
        OptDir.Text = dlg.FolderName;
    }
}
