namespace BGCompanion.Core.Games;

/// <summary>
/// Поиск типов существ в распознанном тексте экрана («В этой игре: Демоны, Драконы, …»).
/// Корни слов выбраны так, чтобы переживать ошибки распознавания и падежи; есть русский и английский клиент.
/// </summary>
public static class RaceText
{
    private static readonly (string Race, string[] Stems)[] Words =
    {
        ("MURLOC", new[] { "мурлок", "murloc" }),
        ("DRAGON", new[] { "дракон", "dragon" }),
        ("MECHANICAL", new[] { "механизм", "mech" }),
        ("DEMON", new[] { "демон", "demon" }),
        ("QUILBOAR", new[] { "свинобраз", "свинобр", "quilboar" }),
        ("NAGA", new[] { "наги", "нага", "наг ", "naga" }),
        ("UNDEAD", new[] { "нежит", "undead" }),
        ("PIRATE", new[] { "пират", "pirate" }),
        ("ELEMENTAL", new[] { "элементал", "elemental" }),
        ("BEAST", new[] { "звер", "beast" }),
        ("ABERRATION", new[] { "аберрац", "aberration" }),
    };

    /// <summary>Типы, упомянутые в одной строке текста.</summary>
    public static List<string> InLine(string line)
    {
        // частые ошибки OCR: ноль вместо «о», латинские буквы-двойники вместо кириллицы
        var l = " " + line.ToLowerInvariant().Replace('ё', 'е').Replace('0', 'о') + " ";
        if (l.Any(ch => ch >= 'а' && ch <= 'я'))
            l = l.Replace('a', 'а').Replace('e', 'е').Replace('o', 'о').Replace('p', 'р').Replace('c', 'с').Replace('x', 'х');
        var found = new List<string>();
        foreach (var (race, stems) in Words)
            if (stems.Any(l.Contains)) found.Add(race);
        return found;
    }

    /// <summary>
    /// Типы из распознанного экрана: учитываются только строки, где перечислено хотя бы 2 типа
    /// (так это выглядит в объявлении типов), чтобы не цеплять случайные слова из текста карт.
    /// </summary>
    public static List<string> FromScreen(IEnumerable<string> lines)
    {
        var set = new List<string>();
        var all = lines.ToList();
        for (int i = 0; i < all.Count; i++)
        {
            var here = InLine(all[i]);
            // список может переноситься на следующую строку
            var next = i + 1 < all.Count ? InLine(all[i + 1]) : new List<string>();
            if (here.Count >= 2 || (here.Count == 1 && next.Count >= 1))
                foreach (var r in here) if (!set.Contains(r)) set.Add(r);
        }
        return set;
    }
}
