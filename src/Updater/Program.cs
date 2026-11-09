// BG Companion Updater — made by modedarkness
// Скачивает последнюю версию с GitHub и ставит её поверх текущей.
// Запуск: двойной клик по Updater.exe (или кнопка «Обновить» в программе).
// Если программы в папке ещё нет — работает как установщик.
using System.Diagnostics;
using System.IO.Compression;
using System.Text;
using BGCompanion.Core;
using BGCompanion.Core.Update;

Console.OutputEncoding = Encoding.UTF8;
Console.Title = "BG Companion — обновление";
Console.WriteLine($"BG Companion Updater · {AppInfo.Credit}");
Console.WriteLine();

string? dir = null;
int pid = 0;
bool fromTemp = false;
for (int i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--dir" when i + 1 < args.Length: dir = args[++i]; break;
        case "--pid" when i + 1 < args.Length: int.TryParse(args[++i], out pid); break;
        case "--from-temp": fromTemp = true; break;
    }
}
dir ??= AppContext.BaseDirectory.TrimEnd('\\');

try
{
    // Апдейтер не может перезаписать сам себя, поэтому сначала копируется во временную папку
    // и перезапускается оттуда.
    if (!fromTemp)
    {
        var self = Environment.ProcessPath!;
        var tempDir = Path.Combine(Path.GetTempPath(), "BGCompanionUpdater");
        Directory.CreateDirectory(tempDir);
        var copy = Path.Combine(tempDir, "Updater.exe");
        File.Copy(self, copy, overwrite: true);
        var psi = new ProcessStartInfo(copy) { UseShellExecute = false };
        psi.ArgumentList.Add("--dir"); psi.ArgumentList.Add(dir);
        psi.ArgumentList.Add("--from-temp");
        if (pid > 0) { psi.ArgumentList.Add("--pid"); psi.ArgumentList.Add(pid.ToString()); }
        Process.Start(psi);
        return 0;
    }

    var mainExe = Path.Combine(dir, AppInfo.MainExe);
    Version? installed = null;
    if (File.Exists(mainExe))
    {
        var fv = FileVersionInfo.GetVersionInfo(mainExe);
        installed = new Version(fv.FileMajorPart, fv.FileMinorPart, fv.FileBuildPart);
        Console.WriteLine($"Установлена версия: {installed.ToString(3)}");
    }
    else Console.WriteLine("Программа не установлена — будет выполнена установка в:\n  " + dir);

    Console.WriteLine("Проверяю последнюю версию на GitHub…");
    var rel = await UpdateService.GetLatestAsync();
    if (rel == null || rel.AssetUrl == null)
    {
        Console.WriteLine("Не удалось получить информацию о релизе. Проверьте интернет и попробуйте позже.");
        return Finish(1, mainExe, launch: File.Exists(mainExe));
    }
    Console.WriteLine($"Последняя версия: {rel.Version.ToString(3)}");

    if (installed != null && rel.Version <= installed)
    {
        Console.WriteLine("У вас уже последняя версия.");
        return Finish(0, mainExe, launch: true);
    }

    // ждём закрытия программы
    WaitForApp(pid, mainExe);

    var work = Path.Combine(Path.GetTempPath(), "BGCompanionUpdate_" + rel.Version);
    if (Directory.Exists(work)) Directory.Delete(work, true);
    Directory.CreateDirectory(work);
    var zip = Path.Combine(work, AppInfo.ReleaseAsset);

    Console.Write("Скачиваю: 0%");
    var progress = new Progress<double>(p => Console.Write($"\rСкачиваю: {p:P0}   "));
    await UpdateService.DownloadAsync(rel.AssetUrl, zip, progress);
    Console.WriteLine("\rСкачано.            ");

    var extract = Path.Combine(work, "files");
    ZipFile.ExtractToDirectory(zip, extract);
    // в архиве файлы могут лежать во вложенной папке
    var root = extract;
    var sub = Directory.GetDirectories(extract);
    if (!File.Exists(Path.Combine(root, AppInfo.MainExe)) && sub.Length == 1) root = sub[0];
    if (!File.Exists(Path.Combine(root, AppInfo.MainExe)))
        throw new InvalidDataException("В архиве обновления нет " + AppInfo.MainExe);

    Console.WriteLine("Устанавливаю…");
    Directory.CreateDirectory(dir);
    foreach (var file in Directory.GetFiles(root, "*", SearchOption.AllDirectories))
    {
        var rel2 = Path.GetRelativePath(root, file);
        var target = Path.Combine(dir, rel2);
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        CopyWithRetry(file, target);
    }
    try { Directory.Delete(work, true); } catch { }

    Console.WriteLine($"Готово! Установлена версия {rel.Version.ToString(3)}.");
    return Finish(0, mainExe, launch: true);
}
catch (Exception ex)
{
    Console.WriteLine();
    Console.WriteLine("Ошибка: " + ex.Message);
    Console.WriteLine("Если не помогает повторный запуск, скачайте версию вручную:");
    Console.WriteLine($"  https://github.com/{AppInfo.GitHubOwner}/{AppInfo.GitHubRepo}/releases/latest");
    Console.WriteLine("Нажмите любую клавишу…");
    try { Console.ReadKey(true); } catch { }
    return 1;
}

static void WaitForApp(int pid, string mainExe)
{
    var procs = new List<Process>();
    if (pid > 0)
    {
        try { procs.Add(Process.GetProcessById(pid)); } catch { }
    }
    foreach (var p in Process.GetProcessesByName(Path.GetFileNameWithoutExtension(mainExe)))
    {
        try
        {
            if (string.Equals(p.MainModule?.FileName, mainExe, StringComparison.OrdinalIgnoreCase)) procs.Add(p);
        }
        catch { }
    }
    foreach (var p in procs.DistinctBy(p => p.Id))
    {
        if (p.HasExited) continue;
        Console.WriteLine("Жду закрытия BG Companion…");
        if (!p.WaitForExit(15000))
        {
            Console.WriteLine("Программа не закрылась — закрываю принудительно.");
            try { p.Kill(); p.WaitForExit(5000); } catch { }
        }
    }
}

static void CopyWithRetry(string src, string dst)
{
    for (int i = 0; ; i++)
    {
        try { File.Copy(src, dst, overwrite: true); return; }
        catch (IOException) when (i < 10) { Thread.Sleep(500); }
        catch (UnauthorizedAccessException) when (i < 10) { Thread.Sleep(500); }
    }
}

static int Finish(int code, string mainExe, bool launch)
{
    if (launch && File.Exists(mainExe))
    {
        Console.WriteLine("Запускаю BG Companion…");
        Process.Start(new ProcessStartInfo(mainExe) { UseShellExecute = true, WorkingDirectory = Path.GetDirectoryName(mainExe)! });
    }
    Console.WriteLine("Окно закроется через 3 секунды.");
    Thread.Sleep(3000);
    return code;
}
