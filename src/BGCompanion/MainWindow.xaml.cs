using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using BGCompanion.Core.Cards;
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
        Core.BgChanged += OnBgChanged;
        Core.MetaChanged += RefreshMeta;
        SourceInitialized += (_, _) => UseDarkTitleBar();
        Closing += (_, e) =>
        {
            if (Core.Settings.CloseToTray && !App.ExitRequested)
            {
                e.Cancel = true;
                Hide();
                App.ShowTrayHint();
            }
        };
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
        var external = Core.Store.Games.Count(g => g.External);
        StGames.Text = $"Записано игр: {Core.Store.MyGames.Count}" + (external > 0 ? $" (и {external} из чужих логов — только для меты)" : "");

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

    // ---------- тёмный заголовок окна (Windows 10 20H1+ / 11) ----------
    [DllImport("dwmapi.dll")] private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private void UseDarkTitleBar()
    {
        var h = new WindowInteropHelper(this).Handle;
        int on = 1;
        if (DwmSetWindowAttribute(h, 20, ref on, sizeof(int)) != 0) DwmSetWindowAttribute(h, 19, ref on, sizeof(int));
    }

    // ---------- таверна ----------
    private string _tavernRacesKey = "";

    private void OnBgChanged()
    {
        GameRaces.Text = Core.LobbyRaces.Count > 0
            ? $"Типы ({(Core.KnownRaces.Count >= 5 ? Core.RacesSource : Core.LobbyRacesComplete ? "по таверне" : "по таверне, пока не все")}): "
              + string.Join(", ", Core.LobbyRaces.Select(global::BGCompanion.Core.Games.ShopAdvisor.RaceName))
            : "";
        GameRacesWarn.Text = Core.RacesWarning;
        var key = string.Join(",", Core.LobbyRaces);
        if (key == _tavernRacesKey) return;
        _tavernRacesKey = key;
        RefreshTavern();
    }

    private void TavernFilter_Changed(object sender, RoutedEventArgs e) => RefreshTavern();
    private void TavernSearch_Changed(object sender, System.Windows.Controls.TextChangedEventArgs e) => RefreshTavern();

    private void RefreshTavern()
    {
        if (Core.Cards.Count == 0) return;
        var races = Core.LobbyRaces;
        bool filter = TavernOnlyLobby.IsChecked == true && races.Count > 0;
        var q = TavernSearch.Text.Trim();
        var list = Core.Cards.PoolMinions
            .Where(c => !filter || c.Races.Length == 0 || c.Races.Contains("ALL") || c.Races.Any(races.Contains))
            .Select(c => new
            {
                c.TechLevel,
                Tier = c.TechLevel,
                c.Name,
                Race = c.Races.Contains("ALL") ? "Все типы"
                    : c.Races.Length == 0 ? "—" : string.Join(", ", c.Races.Select(global::BGCompanion.Core.Games.ShopAdvisor.RaceName)),
                Stats = $"{c.Attack}/{c.Health}",
                Text = CardText.Clean(c.Text),
            })
            .Where(x => q.Length == 0 || x.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Text.Contains(q, StringComparison.OrdinalIgnoreCase) || x.Race.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderBy(x => x.TechLevel).ThenBy(x => x.Name)
            .ToList();
        TavernGrid.ItemsSource = list;
        TavernInfo.Text = races.Count == 0
            ? $"Все существа Battlegrounds: {list.Count}. Во время игры список сузится до типов вашего лобби."
            : (Core.LobbyRacesComplete ? "Типы этой игры: " : "Типы этой игры (определены не все): ")
              + string.Join(", ", races.Select(global::BGCompanion.Core.Games.ShopAdvisor.RaceName)) + $" · существ: {list.Count}";
    }

    // ---------- логи других игроков ----------
    private async void ImportZip_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Архив с логами Hearthstone", Filter = "Архив ZIP|*.zip" };
        if (dlg.ShowDialog(this) == true) await ImportExternal(dlg.FileName);
    }

    private async void ImportFolder_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Папка Logs из Hearthstone другого игрока" };
        if (dlg.ShowDialog(this) == true) await ImportExternal(dlg.FolderName);
    }

    private async Task ImportExternal(string path)
    {
        StatusText.Text = "Импортирую логи…";
        try
        {
            var (games, files) = await Core.ImportExternalAsync(path);
            StatusText.Text = files == 0
                ? "В выбранном месте нет файлов Power.log — нужна папка Logs из папки Hearthstone"
                : $"Из {files} файлов логов добавлено игр Battlegrounds: {games}";
        }
        catch (Exception ex) { StatusText.Text = "Не удалось импортировать: " + ex.Message; }
    }

    // ---------- своя мета и патч ----------
    private void RefreshMeta()
    {
        var m = Core.Miner;
        MetaInfo.Text = m == null || m.TotalBoards == 0
            ? "Пока нет данных. Каждая сыгранная игра даёт до 8 итоговых столов (ваш и соперников) с их местами — из них программа сама находит сборки и считает, как они занимают места."
            : $"Собрано: {m.TotalGames} игр (ваших и из логов друзей), {m.TotalBoards} итоговых столов. " +
              $"Тир появляется, когда у сборки набирается {MetaMiner.MinBoardsForTier}+ столов.";
        MetaGrid.ItemsSource = Core.MinedComps.Select(x => new
        {
            x.Comp.Name,
            Tier = x.EnoughData ? x.Comp.Tier : "мало данных",
            x.Boards,
            Avg = x.AvgPlace.ToString("0.0"),
            Top4 = x.Top4Rate.ToString("P0"),
            Wins = x.WinRate.ToString("P0"),
            Core = string.Join(", ", x.Comp.Core.Select(Core.Cards.NameOf)),
        }).ToList();

        var p = Core.Patch;
        PatchInfoText.Text = p == null || p.Changes.Count == 0
            ? "Изменения патча появятся, когда выйдет следующее обновление игры: программа сравнит старую и новую базу карт."
            : $"Изменения существ в последнем патче ({p.Date}): усиленные карты поднимают сборки, ослабленные — опускают.";
        PatchGrid.ItemsSource = p?.Changes.OrderBy(c => c.Sign).Select(c => new { c.Name, Kind = c.KindText, c.Detail }).ToList();
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

        HistoryGrid.ItemsSource = Core.Store.MyGames.OrderByDescending(g => g.StartedAt).Select(g => new
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
        RefreshTavern();
    }

    // ---------- кнопки ----------
    private async void Demo_Click(object sender, RoutedEventArgs e) => await Core.RunDemoAsync();
    private void Races_Click(object sender, RoutedEventArgs e) => RacePickerWindow.ShowPicker();

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
        OptShop.IsChecked = s.ShowShopAdvice;
        OptOcr.IsChecked = s.RaceOcr;
        OptAsk.IsChecked = s.AskRacesIfUnknown;
        OptOcrShots.IsChecked = s.RaceOcrDebugShots;
        OptOpp.IsChecked = s.ShowOpponent;
        OptTray.IsChecked = s.CloseToTray;
        OptAutostart.IsChecked = s.StartWithWindows;
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
        s.ShowShopAdvice = OptShop.IsChecked == true;
        s.RaceOcr = OptOcr.IsChecked == true;
        s.AskRacesIfUnknown = OptAsk.IsChecked == true;
        s.RaceOcrDebugShots = OptOcrShots.IsChecked == true;
        s.ShowOpponent = OptOpp.IsChecked == true;
        s.CloseToTray = OptTray.IsChecked == true;
        if (s.StartWithWindows != (OptAutostart.IsChecked == true))
        {
            s.StartWithWindows = OptAutostart.IsChecked == true;
            App.SetAutostart(s.StartWithWindows);
        }
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
