using System.Diagnostics;
using System.IO;
using System.Windows;
using BGCompanion.Core;

namespace BGCompanion;

public partial class App : Application
{
    public static AppCore Core { get; private set; } = null!;
    private OverlayWindow? _overlay;
    private static Mutex? _single;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        _single = new Mutex(true, "BGCompanion_single_instance", out var first);
        if (!first)
        {
            MessageBox.Show("BG Companion уже запущен.", AppInfo.Name);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            File.AppendAllText(Path.Combine(AppInfo.DataDir, "error.log"), $"{DateTime.Now}: {ex.Exception}\n");
            MessageBox.Show("Ошибка: " + ex.Exception.Message + "\nПодробности в error.log в папке данных.", AppInfo.Name);
            ex.Handled = true;
        };

        Core = new AppCore();
        var main = new MainWindow();
        MainWindow = main;
        if (Core.Settings.StartMinimized) main.WindowState = WindowState.Minimized;
        main.Show();

        _overlay = new OverlayWindow();
        _overlay.Show();

        await Core.StartAsync();
        if (e.Args.Contains("--demo", StringComparer.OrdinalIgnoreCase)) await Core.RunDemoAsync();
    }

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
        Current.Shutdown();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Core?.Dispose();
        base.OnExit(e);
    }
}
