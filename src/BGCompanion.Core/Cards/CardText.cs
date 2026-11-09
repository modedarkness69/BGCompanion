using System.Text.RegularExpressions;

namespace BGCompanion.Core.Cards;

/// <summary>Текст карты из HearthstoneJSON без разметки: убирает &lt;b&gt;, [x], $, # и переносы.</summary>
public static partial class CardText
{
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex TagsRx();
    [GeneratedRegex(@"\s+")] private static partial Regex SpacesRx();
    [GeneratedRegex(@"[$#](\d)")] private static partial Regex NumRx();
    [GeneratedRegex(@"(\S)\s*\n\s*")] private static partial Regex NewlineRx();

    public static string Clean(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        // в текстах с [x] перенос — просто разбивка строки; в остальных он разделяет ключевые слова
        bool manualWrap = text.Contains("[x]");
        var t = TagsRx().Replace(text, "");
        t = t.Replace("[x]", "").Replace(' ', ' ').Replace("@", "");
        t = manualWrap
            ? t.Replace("\n", " ")
            : NewlineRx().Replace(t, m => ".!?:".Contains(m.Groups[1].Value) ? m.Groups[1].Value + " " : m.Groups[1].Value + ". ");
        t = NumRx().Replace(t, "$1");
        return SpacesRx().Replace(t, " ").Trim();
    }
}
