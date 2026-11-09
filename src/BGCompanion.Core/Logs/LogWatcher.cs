using System.Text;

namespace BGCompanion.Core.Logs;

/// <summary>
/// Следит за Power.log самой новой сессии Hearthstone и отдаёт новые строки парсеру.
/// Работает опросом файла: игра держит лог открытым, FileSystemWatcher на нём ненадёжен.
/// </summary>
public sealed class LogWatcher : IDisposable
{
    private readonly string _installDir;
    private readonly PowerLogParser _parser;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loop;

    public string? CurrentFile { get; private set; }
    public event Action<string>? Status;

    public LogWatcher(string installDir, PowerLogParser parser)
    {
        _installDir = installDir;
        _parser = parser;
    }

    public void Start() => _loop = Task.Run(() => LoopAsync(_cts.Token));

    private async Task LoopAsync(CancellationToken ct)
    {
        long pos = 0;
        string? file = null;
        var pending = new StringBuilder();
        var startedAt = DateTime.Now;

        while (!ct.IsCancellationRequested)
        {
            try
            {
                var newest = HearthstoneLocator.SessionDirs(_installDir).LastOrDefault();
                var candidate = newest.Dir != null ? Path.Combine(newest.Dir, "Power.log") : null;

                if (candidate != null && candidate != file && File.Exists(candidate))
                {
                    _parser.FinishIfAny(false);
                    file = candidate;
                    CurrentFile = file;
                    _parser.SetLogDate(newest.Started);
                    // Новая сессия, начатая после запуска программы, читается с начала.
                    // Если игра уже шла, тоже читаем с начала: так подхватится текущий матч.
                    pos = 0;
                    pending.Clear();
                    Status?.Invoke("Читаю лог: " + file);
                }

                if (file != null && File.Exists(file))
                {
                    using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    if (fs.Length < pos) pos = 0; // файл пересоздан
                    if (fs.Length > pos)
                    {
                        fs.Seek(pos, SeekOrigin.Begin);
                        var buf = new byte[Math.Min(fs.Length - pos, 4 * 1024 * 1024)];
                        var read = await fs.ReadAsync(buf, ct);
                        pos += read;
                        pending.Append(Encoding.UTF8.GetString(buf, 0, read));
                        var text = pending.ToString();
                        var lastNl = text.LastIndexOf('\n');
                        if (lastNl >= 0)
                        {
                            foreach (var line in text[..lastNl].Split('\n'))
                                _parser.Feed(line.TrimEnd('\r'));
                            pending.Clear();
                            pending.Append(text[(lastNl + 1)..]);
                        }
                        continue; // сразу дочитываем, если данных много
                    }
                }
            }
            catch (IOException) { /* файл занят — попробуем позже */ }
            catch (Exception ex) { Status?.Invoke("Ошибка чтения лога: " + ex.Message); }

            await Task.Delay(250, ct).ContinueWith(_ => { });
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1000); } catch { }
        _cts.Dispose();
    }
}
