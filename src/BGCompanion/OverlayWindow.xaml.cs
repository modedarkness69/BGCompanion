using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace BGCompanion;

public partial class OverlayWindow : Window
{
    public static OverlayWindow? Instance { get; private set; }
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private AppCore Core => App.Core;

    public OverlayWindow()
    {
        InitializeComponent();
        Instance = this;
        SourceInitialized += (_, _) => MakeClickThrough();
        Core.StateChanged += Refresh;
        Core.BgChanged += RefreshBg;
        _timer.Tick += (_, _) => Track();
        _timer.Start();
        ApplySettings();
    }

    // ---------- Win32 ----------
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr h, int i);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr h, int i, int v);
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_LAYERED = 0x80000, WS_EX_NOACTIVATE = 0x08000000;
    private static readonly IntPtr HWND_TOPMOST = new(-1);
    private const uint SWP_NOACTIVATE = 0x10, SWP_SHOWWINDOW = 0x40;

    private IntPtr Handle => new WindowInteropHelper(this).Handle;

    private void MakeClickThrough()
    {
        var ex = GetWindowLong(Handle, GWL_EXSTYLE);
        SetWindowLong(Handle, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_LAYERED | WS_EX_NOACTIVATE);
    }

    private (int x, int y, int w, int h) _last;

    /// <summary>Повторяет положение и размер окна Hearthstone; прячется, если игра свёрнута или не в фокусе.</summary>
    private void Track()
    {
        if (App.OverlayTest)
        {
            // служебный режим для проверки без игры: оверлей на весь основной экран
            var b = System.Windows.Forms.Screen.PrimaryScreen!.Bounds;
            var r = (b.X, b.Y, b.Width, b.Height);
            if (!IsVisible) Show();
            if (r != _last)
            {
                _last = r;
                SetWindowPos(Handle, HWND_TOPMOST, b.X, b.Y, b.Width, b.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
                ApplyScale();
            }
            return;
        }
        var hs = FindWindow("UnityWndClass", "Hearthstone");
        bool show = Core.Settings.OverlayEnabled && hs != IntPtr.Zero && IsWindowVisible(hs) && !IsIconic(hs);
        if (show)
        {
            var fg = GetForegroundWindow();
            show = fg == hs || fg == Handle;
        }
        if (!show)
        {
            if (IsVisible) Hide();
            return;
        }
        GetClientRect(hs, out var rc);
        var p = new POINT();
        ClientToScreen(hs, ref p);
        var rect = (p.X, p.Y, rc.Right - rc.Left, rc.Bottom - rc.Top);
        if (!IsVisible) Show();
        if (rect != _last)
        {
            _last = rect;
            // координаты в пикселях — ставим напрямую через Win32, чтобы не путаться с DPI
            SetWindowPos(Handle, HWND_TOPMOST, rect.Item1, rect.Item2, rect.Item3, rect.Item4, SWP_NOACTIVATE | SWP_SHOWWINDOW);
            ApplyScale();
        }
    }

    public void ApplySettings()
    {
        Opacity = Core.Settings.OverlayOpacity;
        ApplyScale();
        Refresh();
    }

    /// <summary>Размер панелей подстраивается под высоту окна игры (база — 1080p) и настройку пользователя.</summary>
    private void ApplyScale()
    {
        double auto = 1.0;
        if (_last.h > 0)
        {
            var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(this).DpiScaleY;
            auto = Math.Clamp(_last.h / dpi / 1080.0, 0.75, 2.5);
        }
        var s = auto * Core.Settings.OverlayScale;
        HeroScale.ScaleX = HeroScale.ScaleY = s;
        GameScale.ScaleX = GameScale.ScaleY = s;
    }

    private void Refresh()
    {
        var s = Core.Settings;
        // выбор героя
        if (s.ShowHeroPickPanel && Core.HeroPickPhase && Core.HeroOffer.Count > 0)
        {
            HeroItems.ItemsSource = Core.HeroOffer.Select(MainWindow.Describe).ToList();
            bool anyData = Core.HeroOffer.Any(h => h.Community != null || h.Personal is { Games: > 0 });
            HeroHint.Text = anyData
                ? "★ — лучший по ожидаемому месту. Личная статистика растёт с каждой игрой."
                : "Пока нет данных по этим героям — статистика накопится после нескольких игр.";
            HeroPanel.Visibility = Visibility.Visible;
        }
        else HeroPanel.Visibility = Visibility.Collapsed;

        // матч
        var g = Core.Game;
        bool inGame = g != null && g.HeroCardId != null && !Core.HeroPickPhase;
        if (inGame)
        {
            GameTitle.Text = Core.Cards.NameOf(g!.HeroCardId);
            GameLine.Text = Core.Turn > 0 ? $"Ход {Core.Turn} · таверна T{Core.Tier}" : "Начало матча";
            GameTip.Text = s.ShowTavernTips ? Core.Advisor.TavernTip(Core.Turn, Core.Tier) : "";
            GameTip.Visibility = GameTip.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            GamePanel.Visibility = Visibility.Visible;
        }
        else GamePanel.Visibility = Visibility.Collapsed;
        Credit.Visibility = inGame ? Visibility.Visible : Visibility.Collapsed;
        RefreshBg();
    }

    private sealed record ShopRow(string Title, string Why, System.Windows.Media.Brush Color, Visibility WhyVisibility);

    private static readonly System.Windows.Media.Brush BestBrush = Brush("#5BD37A");
    private static readonly System.Windows.Media.Brush SkipBrush = Brush("#6A6A7E");
    private static readonly System.Windows.Media.Brush NormalBrush = Brush("#E8E8EE");
    private static System.Windows.Media.Brush Brush(string hex)
    {
        var b = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(hex)!;
        b.Freeze();
        return b;
    }

    /// <summary>Таверна и соперник — обновляются таймером AppCore.</summary>
    private void RefreshBg()
    {
        var s = Core.Settings;
        var bg = Core.Bg;
        bool inGame = bg != null && Core.Game?.HeroCardId != null && !Core.HeroPickPhase;

        // типы этой игры
        RacesLine.Text = inGame && Core.LobbyRaces.Count > 0
            ? (Core.KnownRaces.Count >= 5 ? "Типы: " : Core.LobbyRacesComplete ? "Типы (по таверне): " : "Типы (по таверне, пока не все): ")
              + string.Join(", ", Core.LobbyRaces.Select(global::BGCompanion.Core.Games.ShopAdvisor.RaceName))
              + (Core.RacesWarning.Length > 0 ? Environment.NewLine + "⚠ " + Core.RacesWarning : "")
            : inGame ? "Типы ещё не известны — отметьте их через значок в трее" : "";
        RacesLine.Visibility = RacesLine.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

        // сборки: ведущая и запасная
        if (inGame && s.ShowShopAdvice && Core.Comps.Count > 0)
        {
            CompItems.ItemsSource = Core.Comps.Take(2).Select(c => new ShopRow(
                (c.Recommended ? "★ " : "   ") + c.Comp.Name + (c.Comp.Tier.Length > 0 ? $" ({c.Comp.Tier})" : "")
                    + $" — ключевых {c.CoreOwned} из {c.Comp.Core.Count}",
                c.MissingCore.Count > 0
                    ? "нужны: " + string.Join(", ", c.MissingCore.Take(3).Select(id => $"{Core.Cards.NameOf(id)} T{Core.Cards.Get(id)?.TechLevel}"))
                    : "все ключевые карты собраны",
                c.Recommended ? BestBrush : NormalBrush,
                Visibility.Visible)).ToList();
            CompsPanel.Visibility = Visibility.Visible;
        }
        else CompsPanel.Visibility = Visibility.Collapsed;

        // советы по таверне
        if (inGame && s.ShowShopAdvice && bg!.ShopPhase && Core.Shop.Count > 0)
        {
            ShopItems.ItemsSource = Core.Shop.Select(a => new ShopRow(
                (a.Best ? "★ " : a.Skip ? "– " : "   ") + $"T{a.Tier} {a.Name}",
                string.Join("; ", a.Reasons),
                a.Best ? BestBrush : a.Skip ? SkipBrush : NormalBrush,
                a.Reasons.Count > 0 && !a.Skip ? Visibility.Visible : Visibility.Collapsed)).ToList();
            ShopHeader.Text = Core.Shop.Any(x => x.Best)
                ? "Таверна"
                : "Таверна — явных находок нет: можно обновить или повысить тир";
            ShopPanel.Visibility = Visibility.Visible;
        }
        else ShopPanel.Visibility = Visibility.Collapsed;

        // следующий соперник
        if (inGame && s.ShowOpponent && bg!.ShopPhase && bg.NextOpponent is { } o)
        {
            var hero = o.HeroCardId != null ? Core.Cards.NameOf(o.HeroCardId) : "соперник";
            OppTitle.Text = "Следующий бой: " + hero;
            OppBoard.Text = o.LastSeenTurn > 0 && o.LastBoard.Count > 0
                ? $"Стол с {o.LastSeenTurn}-го хода: " + string.Join(", ", o.LastBoard.Select(m => $"{Core.Cards.NameOf(m.CardId)} {m.Atk}/{m.Health}"))
                : "Ещё не встречались — стол неизвестен.";
            OppPanel.Visibility = Visibility.Visible;
        }
        else OppPanel.Visibility = Visibility.Collapsed;
    }
}
