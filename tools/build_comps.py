"""
Сборка data/comps.json из списка сборок сообщества (tools/community_comps.json, вручную)
и сгенерированных сборок по синергиям (tools/gen_comps.py).

Английские названия карт сопоставляются с id через HearthstoneJSON enUS.
Ранние карты для сборки на тип берутся из сгенерированной сборки этого типа.

Карты, которых больше нет в пуле Battlegrounds, выбрасываются; сборки, где осталось меньше
2 ключевых карт, удаляются. Файл перезаписывается только если сборки реально изменились.

Запуск:  python tools/build_comps.py <cards enUS.json> <generated comps.json> data/comps.json
"""
import os
import json
import sys
from datetime import date

EN, GEN, OUT = sys.argv[1], sys.argv[2], sys.argv[3]
cards = json.load(open(EN, encoding="utf-8"))
community = json.load(open("tools/community_comps.json", encoding="utf-8"))
generated = json.load(open(GEN, encoding="utf-8"))

# имя → id: сначала существа из пула BG, потом любые карты Battlegrounds
by_name = {}
for c in sorted(cards, key=lambda c: (not c.get("isBattlegroundsPoolMinion", False), c["id"])):
    n = c.get("name", "").lower()
    if not (c.get("isBattlegroundsPoolMinion") or c.get("isBattlegroundsPoolSpell")):
        continue  # только то, что сейчас есть в пуле
    if n and n not in by_name and (c.get("isBattlegroundsPoolMinion") or c.get("set") == "BATTLEGROUNDS"):
        by_name[n] = c

early_by_race = {g["races"][0]: g["early"] for g in generated["comps"] if g["races"]}
missing = []
comps = []
for i, cc in enumerate(community["comps"]):
    ids = []
    for name in cc["core"]:
        c = by_name.get(name.lower())
        if c is None:
            missing.append(f"{cc['name']}: {name}")
        else:
            ids.append(c["id"])
    race = cc["races"][0] if cc["races"] else None
    if len(ids) < 2:
        missing.append(f"{cc['name']}: сборка убрана — осталось ключевых карт: {len(ids)}")
        continue
    comps.append({
        "id": f"community-{i:02d}",
        "name": cc["name"],
        "races": cc["races"],
        "tier": cc["tier"],
        "core": ids,
        "early": [e for e in early_by_race.get(race, []) if e not in ids][:5],
        "tips": f"Тир {cc['tier']} по данным сообщества. Ключевые карты — ядро сборки; ранние — чем заходить в тип.",
        "source": "community",
    })

# сборки на механики из генератора — как запасной вариант
for g in generated["comps"]:
    if not g["races"]:
        g = dict(g, tier="", source="synergy", name=g["name"] + " (по синергиям)")
        comps.append(g)

old = json.load(open(OUT, encoding="utf-8")) if os.path.exists(OUT) else {}
if old.get("comps") == comps:
    print("сборки не изменились — файл не трогаю")
    sys.exit(0)
out = {
    "updated": date.today().isoformat(),
    "source": community["source"] + " Плюс сборки на механики, сгенерированные из текста карт.",
    "comps": comps,
}
json.dump(out, open(OUT, "w", encoding="utf-8"), ensure_ascii=False, indent=2)
print(f"сборок: {len(comps)}, сообщества: {len(community['comps'])}")
print("не в пуле / убрано:", missing if missing else "нет")
