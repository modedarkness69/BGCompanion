using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using BGCompanion.Core.Games;

namespace BGCompanion;

/// <summary>Ручной выбор типов существ матча (если распознать текст на экране не вышло).</summary>
public partial class RacePickerWindow : Window
{
    private static RacePickerWindow? _open;
    private static readonly string[] AllRaces =
        { "BEAST", "DEMON", "DRAGON", "ELEMENTAL", "MECHANICAL", "MURLOC", "NAGA", "PIRATE", "QUILBOAR", "UNDEAD", "ABERRATION" };

    private readonly List<ToggleButton> _toggles = new();

    public static void ShowPicker()
    {
        if (_open != null) { _open.Activate(); return; }
        _open = new RacePickerWindow();
        _open.Closed += (_, _) => _open = null;
        _open.PlaceOverGame();
        _open.Show();
        _open.Activate();
    }

    public RacePickerWindow()
    {
        InitializeComponent();
        var core = App.Core;
        var preset = core.KnownRaces.Count > 0 ? core.KnownRaces : core.LobbyRaces;
        foreach (var r in AllRaces)
        {
            var t = new ToggleButton
            {
                Content = ShopAdvisor.RaceName(r),
                Tag = r,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(12, 6, 12, 6),
                MinWidth = 110,
                IsChecked = preset.Contains(r),
                Style = (Style)FindResource("RaceToggle"),
            };
            t.Click += (_, _) => UpdateCounter();
            _toggles.Add(t);
            Buttons.Children.Add(t);
        }
        if (core.LobbyRaces.Count > 0 && core.KnownRaces.Count == 0)
            Hint.Text = "Отметьте 5 типов, которые игра написала в начале матча. Уже отмечены те, что видны в таверне.";
        UpdateCounter();
    }

    private List<string> Selected => _toggles.Where(t => t.IsChecked == true).Select(t => (string)t.Tag).ToList();

    private void UpdateCounter()
    {
        var n = Selected.Count;
        Counter.Text = $"Отмечено: {n} из 5";
        OkButton.IsEnabled = n is >= 3 and <= 6;
    }

    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr h);

    /// <summary>Ставим окно в правый верхний угол игры (или экрана).</summary>
    private void PlaceOverGame()
    {
        var hs = FindWindow("UnityWndClass", "Hearthstone");
        var dpi = System.Windows.Media.VisualTreeHelper.GetDpi(Application.Current.MainWindow ?? this);
        if (hs != IntPtr.Zero && GetWindowRect(hs, out var r))
        {
            Left = r.Right / dpi.DpiScaleX - Width - 40;
            Top = r.Top / dpi.DpiScaleY + 80;
        }
        else
        {
            Left = SystemParameters.WorkArea.Right - Width - 40;
            Top = SystemParameters.WorkArea.Top + 80;
        }
    }

    private void ReturnToGame()
    {
        var hs = FindWindow("UnityWndClass", "Hearthstone");
        if (hs != IntPtr.Zero) SetForegroundWindow(hs);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        App.Core.SetRaces(Selected, "вручную");
        Close();
        ReturnToGame();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
        ReturnToGame();
    }

    private void Drag(object sender, MouseButtonEventArgs e) => DragMove();
}
