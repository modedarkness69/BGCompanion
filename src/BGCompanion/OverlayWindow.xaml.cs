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
        if (g != null && g.HeroCardId != null && !Core.HeroPickPhase)
        {
            GameTitle.Text = Core.Cards.NameOf(g.HeroCardId);
            GameLine.Text = Core.Turn > 0 ? $"Ход {Core.Turn} · таверна T{Core.Tier}" : "Начало матча";
            GameTip.Text = s.ShowTavernTips ? Core.Advisor.TavernTip(Core.Turn, Core.Tier) : "";
            GameTip.Visibility = GameTip.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
            GamePanel.Visibility = Visibility.Visible;
        }
        else GamePanel.Visibility = Visibility.Collapsed;
    }
}
