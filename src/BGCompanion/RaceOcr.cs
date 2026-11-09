using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using BGCompanion.Core;
using BGCompanion.Core.Games;
using Windows.Globalization;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace BGCompanion;

/// <summary>
/// Распознавание типов существ, которые игра пишет текстом в начале матча.
/// Только снимок окна игры и встроенный OCR Windows — в игру ничего не передаётся.
/// </summary>
public static class RaceOcr
{
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string? cls, string? title);
    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr h, ref POINT p);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);

    private static OcrEngine? _engine;
    private static bool _engineChecked;

    /// <summary>Есть ли в Windows распознавание русского (или английского) текста.</summary>
    public static bool Available
    {
        get
        {
            if (!_engineChecked)
            {
                _engineChecked = true;
                try
                {
                    _engine = OcrEngine.TryCreateFromLanguage(new Language("ru"))
                              ?? OcrEngine.TryCreateFromLanguage(new Language("en-US"))
                              ?? OcrEngine.TryCreateFromUserProfileLanguages();
                }
                catch { _engine = null; }
            }
            return _engine != null;
        }
    }

    public static string EngineLanguage => _engine?.RecognizerLanguage?.LanguageTag ?? "";

    /// <summary>Снимок клиентской области окна Hearthstone (null — окна нет или оно свёрнуто).</summary>
    public static Bitmap? CaptureHearthstone()
    {
        var hs = FindWindow("UnityWndClass", "Hearthstone");
        if (hs == IntPtr.Zero || IsIconic(hs)) return null;
        GetClientRect(hs, out var rc);
        var p = new POINT();
        ClientToScreen(hs, ref p);
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
        if (w < 200 || h < 200) return null;
        var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
        using var g = Graphics.FromImage(bmp);
        g.CopyFromScreen(p.X, p.Y, 0, 0, new System.Drawing.Size(w, h));
        return bmp;
    }

    /// <summary>Распознать текст на снимке. Возвращает строки текста.</summary>
    public static async Task<List<string>> ReadLinesAsync(Bitmap bmp)
    {
        if (!Available) return new List<string>();
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        ms.Position = 0;
        using var ras = ms.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(ras);
        using var sb = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
        var result = await _engine!.RecognizeAsync(sb);
        return result.Lines.Select(l => l.Text).ToList();
    }

    /// <summary>Сохранить снимок для отладки распознавания (локально, в папке данных).</summary>
    public static void SaveDebug(Bitmap bmp, string name)
    {
        try
        {
            var dir = Path.Combine(AppInfo.DataDir, "ocr");
            Directory.CreateDirectory(dir);
            // уменьшенная копия, чтобы не забивать диск
            double k = Math.Min(1.0, 1600.0 / bmp.Width);
            using var small = new Bitmap(bmp, new System.Drawing.Size((int)(bmp.Width * k), (int)(bmp.Height * k)));
            small.Save(Path.Combine(dir, name), ImageFormat.Jpeg);
        }
        catch { }
    }

    /// <summary>Один проход: снимок → текст → типы. Пустой список, если типов на экране нет.</summary>
    public static async Task<(List<string> Races, List<string> Lines)> ScanOnceAsync(bool saveDebug, string debugName)
    {
        using var bmp = CaptureHearthstone();
        if (bmp == null) return (new List<string>(), new List<string>());
        var lines = await ReadLinesAsync(bmp);
        var races = RaceText.FromScreen(lines);
        if (saveDebug) SaveDebug(bmp, debugName);
        return (races, lines);
    }
}
