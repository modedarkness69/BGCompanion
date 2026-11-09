using System.Globalization;
using Microsoft.Win32;

namespace BGCompanion.Core.Logs;

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public static class HearthstoneLocator
{
    private static readonly string[] Defaults =
    {
        @"C:\Program Files (x86)\Hearthstone",
        @"C:\Program Files\Hearthstone",
        @"D:\Hearthstone", @"D:\Games\Hearthstone", @"D:\Program Files (x86)\Hearthstone",
        @"E:\Hearthstone", @"E:\Games\Hearthstone",
    };

    public static string? FindInstallDir(string? configured = null)
    {
        if (IsInstall(configured)) return configured;
        foreach (var key in new[]
                 {
                     @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone",
                     @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\Hearthstone",
                 })
        {
            try
            {
                using var k = Registry.LocalMachine.OpenSubKey(key);
                var loc = k?.GetValue("InstallLocation") as string;
                if (IsInstall(loc)) return loc;
            }
            catch { /* нет доступа к реестру — пробуем дальше */ }
        }
        return Defaults.FirstOrDefault(IsInstall);
    }

    private static bool IsInstall(string? dir) =>
        !string.IsNullOrWhiteSpace(dir) && File.Exists(Path.Combine(dir, "Hearthstone.exe"));

    public static string LogConfigPath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Blizzard", "Hearthstone", "log.config");

    /// <summary>
    /// Включает запись Power-лога в log.config. Возвращает true, если файл пришлось изменить
    /// (тогда игру нужно перезапустить).
    /// </summary>
    public static bool EnsureLogConfig()
    {
        var path = LogConfigPath;
        var text = File.Exists(path) ? File.ReadAllText(path) : "";
        if (text.Contains("[Power]", StringComparison.OrdinalIgnoreCase)) return false;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var add = "[Power]\r\nLogLevel=1\r\nFilePrinting=True\r\nConsolePrinting=False\r\nScreenPrinting=False\r\nVerbose=True\r\n";
        File.WriteAllText(path, (text.Length > 0 && !text.EndsWith("\n") ? text + "\r\n" : text) + add);
        return true;
    }

    /// <summary>Папки сессий: Logs\Hearthstone_YYYY_MM_DD_HH_MM_SS, от старых к новым.</summary>
    public static IEnumerable<(string Dir, DateTime Started)> SessionDirs(string installDir)
    {
        var logs = Path.Combine(installDir, "Logs");
        if (!Directory.Exists(logs)) yield break;
        foreach (var d in Directory.GetDirectories(logs, "Hearthstone_*").Order(StringComparer.Ordinal))
        {
            var name = Path.GetFileName(d)["Hearthstone_".Length..];
            if (DateTime.TryParseExact(name, "yyyy_MM_dd_HH_mm_ss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
                yield return (d, dt);
        }
    }

    /// <summary>Все Power-логи сессии (текущий и сохранённый старый).</summary>
    public static IEnumerable<string> PowerLogs(string sessionDir)
    {
        foreach (var n in new[] { "Power_old.log", "Power.log" })
        {
            var p = Path.Combine(sessionDir, n);
            if (File.Exists(p)) yield return p;
        }
    }
}
