using System.Globalization;

namespace BGCompanion.Core.Logs;

/// <summary>
/// Демо-режим: проигрывает записанный Power-лог через парсер с паузами, похожими на настоящие.
/// Длинные паузы сжимаются, чтобы игра на 30 минут проигрывалась примерно за 2 минуты,
/// а на выборе героя делается остановка, чтобы успеть рассмотреть подсказку.
/// </summary>
public sealed class LogReplayer
{
    public double Speed { get; init; } = 15;                 // во сколько раз быстрее реального времени
    public TimeSpan MaxGap { get; init; } = TimeSpan.FromSeconds(1.2);
    public TimeSpan HeroPickHold { get; init; } = TimeSpan.FromSeconds(12);

    /// <summary>Найти последний лог с полной игрой Battlegrounds (с выбором героя).</summary>
    public static (string File, DateTime Date)? FindDemoLog(string installDir)
    {
        foreach (var (dir, started) in HearthstoneLocator.SessionDirs(installDir).Reverse())
        {
            foreach (var f in HearthstoneLocator.PowerLogs(dir))
            {
                bool bg = false, pick = false;
                using var fs = new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var sr = new StreamReader(fs);
                string? line;
                int n = 0;
                while ((line = sr.ReadLine()) != null && n++ < 200_000)
                {
                    if (!bg && line.Contains("GameType=GT_BATTLEGROUNDS")) bg = true;
                    if (!pick && line.Contains("ChoiceType=MULLIGAN")) pick = true;
                    if (bg && pick) return (f, started);
                }
            }
        }
        return null;
    }

    public async Task PlayAsync(string file, DateTime logDate, PowerLogParser parser, CancellationToken ct)
    {
        parser.SetLogDate(logDate);
        TimeSpan? prev = null;
        using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var sr = new StreamReader(fs);
        string? line;
        while ((line = await sr.ReadLineAsync(ct)) != null)
        {
            ct.ThrowIfCancellationRequested();
            var t = TimeOf(line);
            if (t != null)
            {
                if (prev != null && t > prev)
                {
                    var gap = (t.Value - prev.Value) / Speed;
                    if (gap > MaxGap) gap = MaxGap;
                    if (gap > TimeSpan.FromMilliseconds(15)) await Task.Delay(gap, ct);
                }
                prev = t;
            }
            // остановка перед тем, как «игрок» выберет героя
            if (line.Contains("GameState.SendChoices() - id=") && line.Contains("ChoiceType=MULLIGAN"))
                await Task.Delay(HeroPickHold, ct);
            parser.Feed(line);
        }
        parser.FinishIfAny(false);
    }

    private static TimeSpan? TimeOf(string line)
    {
        // "D 20:39:36.0526737 GameState..."
        if (line.Length < 18 || line[0] != 'D' || line[1] != ' ') return null;
        return TimeSpan.TryParseExact(line.Substring(2, 16), @"hh\:mm\:ss\.fffffff", CultureInfo.InvariantCulture, out var t) ? t : null;
    }
}
