using System.Diagnostics;
using System.IO;
using System.Windows;
using BGCompanion.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace BGCompanion;

public partial class App : Application
{
    public static AppCore Core { get; private set; } = null!;
    public static bool ExitRequested { get; private set; }
    public static bool OverlayTest { get; private set; }
    private OverlayWindow? _overlay;
    private static Mutex? _single;
    private static Forms.NotifyIcon? _tray;
    private static bool _trayHintShown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _single = new Mutex(true, "BGCompanion_single_instance", out var first);
        if (!first)
        {
            MessageBox.Show("BG Companion уже запущен — значок в трее рядом с часами.", AppInfo.Name);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            File.AppendAllText(Path.Combine(AppInfo.DataDir, "error.log"), $"{DateTime.Now}: {ex.Exception}\n");
            MessageBox.Show("Ошибка: " + ex.Exception.Message + "\nПодробности в error.log в папке данных.", AppInfo.Name);
            ex.Handled = true;
        };

        ShutdownMode = ShutdownMode.OnExplicitShutdown; // окно может жить в трее
        OverlayTest = e.Args.Contains("--overlay-test", StringComparer.OrdinalIgnoreCase);
        Core = new AppCore();
        CreateTray();

        var main = new MainWindow();
        MainWindow = main;
        bool toTray = e.Args.Contains("--tray", StringComparer.OrdinalIgnoreCase);
        if (!toTray)
        {
            if (Core.Settings.StartMinimized) main.WindowState = WindowState.Minimized;
            main.Show();
        }

        _overlay = new OverlayWindow();
        _overlay.Show();

        await Core.StartAsync();
        if (e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase)) await Core.RunDemoAsync();
    }

    // ---------- трей ----------
    private void CreateTray()
    {
        _tray = new Forms.NotifyIcon
        {
            Text = $"{AppInfo.Name} {AppInfo.VersionText}",
            Icon = System.Drawing.Icon.ExtractAssociatedIcon(Environment.ProcessPath!) ?? System.Drawing.SystemIcons.Application,
            Visible = true,
        };
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Открыть BG Companion", null, (_, _) => ShowMain());
        menu.Items.Add("Демо: проиграть прошлую игру", null, async (_, _) => await Core.RunDemoAsync());
        menu.Items.Add("Проверить обновления", null, async (_, _) => { ShowMain(); await Core.CheckUpdatesAsync(true); });
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", null, (_, _) => ExitApp());
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => ShowMain();
    }

    public static void ShowMain()
    {
        var w = Current.MainWindow;
        if (w == null) return;
        w.Show();
        if (w.WindowState == WindowState.Minimized) w.WindowState = WindowState.Normal;
        w.Activate();
    }

    public static void ShowTrayHint()
    {
        if (_trayHintShown || _tray == null) return;
        _trayHintShown = true;
        _tray.ShowBalloonTip(4000, AppInfo.Name, "Программа работает в трее и включит оверлей при запуске игры. Выход — правой кнопкой по значку.", Forms.ToolTipIcon.Info);
    }

    public static void ExitApp()
    {
        ExitRequested = true;
        Current.Shutdown();
    }

    // ---------- автозапуск ----------
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    public static void SetAutostart(bool on)
    {
        try
        {
            using var k = Registry.CurrentUser.OpenSubKey(RunKey, writable: true) ?? Registry.CurrentUser.CreateSubKey(RunKey);
            if (on) k.SetValue("BGCompanion", $"\"{Environment.ProcessPath}\" --tray");
            else k.DeleteValue("BGCompanion", throwOnMissingValue: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось изменить автозапуск: " + ex.Message, AppInfo.Name);
        }
    }

    // ---------- обновление ----------
    public static void LaunchUpdater()
    {
        var dir = AppContext.BaseDirectory;
        var updater = Path.Combine(dir, AppInfo.UpdaterExe);
        if (!File.Exists(updater))
        {
            MessageBox.Show("Не найден Updater.exe рядом с программой. Скачайте последнюю версию с GitHub.", AppInfo.Name);
            return;
        }
        Process.Start(new ProcessStartInfo(updater)
        {
            ArgumentList = { "--dir", dir.TrimEnd('\\'), "--pid", Environment.ProcessId.ToString() },
            UseShellExecute = false,
        });
        ExitApp();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray != null) { _tray.Visible = false; _tray.Dispose(); }
        Core?.Dispose();
        base.OnExit(e);
    }
}
