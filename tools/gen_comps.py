"""
Генератор стартового файла сборок data/comps.json из базы карт HearthstoneJSON.

Сборки строятся по синергиям в тексте карт текущего пула Battlegrounds
(какие существа усиливают тип или механику), а не по статистике побед.
Файл можно и нужно править руками: программа скачивает его при запуске.

Запуск:  python tools/gen_comps.py <cards.json ruRU> data/comps.json
"""
import json
import re
import sys
from datetime import date

CARDS, OUT = sys.argv[1], sys.argv[2]
cards = [c for c in json.load(open(CARDS, encoding="utf-8")) if c.get("isBattlegroundsPoolMinion")]

RACES = {
    "MURLOC": ("Мурлоки", ["мурлок"]),
    "DRAGON": ("Драконы", ["дракон"]),
    "MECHANICAL": ("Механизмы", ["механизм"]),
    "DEMON": ("Демоны", ["демон"]),
    "QUILBOAR": ("Свинобразы", ["свинобраз"]),
    "NAGA": ("Наги", ["наг"]),
    "UNDEAD": ("Нежить", ["нежит"]),
    "PIRATE": ("Пираты", ["пират"]),
    "ELEMENTAL": ("Элементали", ["элементал"]),
    "BEAST": ("Звери", ["звер"]),
    "ABERRATION": ("Аберрации", ["аберрац"]),
}

MECHANICS = [
    ("deathrattle", "Предсмертные хрипы", ["предсмертн"], "DEATHRATTLE"),
    ("battlecry", "Боевые кличи", ["боевой клич", "боевого клича", "боевые кличи"], "BATTLECRY"),
    ("divine", "Божественные щиты", ["божественн"], "DIVINE_SHIELD"),
    ("spells", "Заклинания таверны", ["заклинани"], None),
    ("rally", "Натиск", ["натиск"], "BACON_RALLY"),
]


def text(c):
    t = re.sub(r"<[^>]+>", "", c.get("text", "")).lower().replace("\n", " ")
    return t


def mentions(c, stems):
    t = text(c)
    return any(re.search(r"(?<![а-яё])" + re.escape(s), t) for s in stems)


def races(c):
    return c.get("races", [])


def pick(lst, n, reverse):
    lst = sorted(lst, key=lambda c: (c.get("techLevel", 0), c["name"]), reverse=reverse)
    return [c["id"] for c in lst[:n]]


comps = []
for race, (name, stems) in RACES.items():
    own = [c for c in cards if race in races(c) or "ALL" in races(c)]
    if len([c for c in own if race in races(c)]) < 6:
        continue  # тип не в пуле этого патча
    buffers = [c for c in cards if mentions(c, stems)]
    core = [c for c in buffers if c.get("techLevel", 0) >= 4]
    early = [c for c in buffers if c.get("techLevel", 0) <= 3]
    if len(early) < 2:
        # нет ранних карт-усилителей — заходим просто существами этого типа
        early += [c for c in cards if race in races(c) and c.get("techLevel", 0) <= 3 and c not in early]
    if len(core) < 2:
        continue
    comps.append({
        "id": race.lower(),
        "name": f"{name}",
        "races": [race],
        "core": pick(core, 6, True),
        "early": pick(early, 5, False),
        "tips": f"Набирайте {name.lower()} с первых ходов и держите 2–3 карты типа к T3. "
                f"Ключевые карты приходят на T4–T6 — до них тяните существ типа, а не случайных сильных.",
    })

for mid, name, stems, mech in MECHANICS:
    payoff = [c for c in cards if mentions(c, stems)]
    carriers = [c for c in cards if mech and mech in c.get("mechanics", [])]
    core = [c for c in payoff if c.get("techLevel", 0) >= 4]
    early = [c for c in carriers if c.get("techLevel", 0) <= 3] if carriers else [c for c in payoff if c.get("techLevel", 0) <= 3]
    if len(core) < 2:
        continue
    comps.append({
        "id": mid,
        "name": name,
        "races": [],
        "core": pick(core, 6, True),
        "early": pick(early, 5, False),
        "tips": f"Сборка на механике «{name}»: ключевые карты усиливают её на T4–T6, ранние существа — носители механики.",
    })

out = {
    "updated": date.today().isoformat(),
    "source": "Сгенерировано из базы карт HearthstoneJSON по синергиям в тексте карт (не по статистике побед). Правки приветствуются.",
    "comps": comps,
}
json.dump(out, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
byid = {c["id"]: c for c in cards}
for comp in comps:
    print(f"{comp['name']}: ключевые = {', '.join(byid[i]['name'] + ' T' + str(byid[i]['techLevel']) for i in comp['core'])}")
    print(f"    ранние = {', '.join(byid[i]['name'] + ' T' + str(byid[i]['techLevel']) for i in comp['early'])}")
print(f"сборок: {len(comps)}")
