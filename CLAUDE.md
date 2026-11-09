# BG Companion

## Назначение
Помощник для Hearthstone Battlegrounds (C# .NET 8, WPF): читает Power.log, оверлей поверх игры, советы по героям, таверне и сборкам, своя мета из логов. Подпись «made by modedarkness», репо `modedarkness69/BGCompanion`, MIT.

## Структура
- `src/BGCompanion.Core` — Logs (парсер, трекер сущностей, демо), Cards (база карт, PatchDiff), Games (статистика, советы, сборки, MetaMiner), Update
- `src/BGCompanion` — WPF: окна, оверлей, OCR типов (`RaceOcr.cs`), трей
- `src/Updater` — апдейтер и установщик из GitHub Releases
- `tools/LogCheck` — проверка парсера на реальных логах; `tools/*.py` + `community_comps.json` — сборка `data/comps.json`
- `Directory.Build.props` — единая версия; `.github/workflows` — `release.yml` (по тегу), `update-comps.yml` (ежедневно)

## Запуск
- dotnet: `C:\Program Files\dotnet` (добавить в PATH); `dotnet build -c Release`
- `dotnet run --project tools/LogCheck -c Release -- "C:\Program Files (x86)\Hearthstone"`
- `.\build.ps1` → `dist\`; релиз: версия в `Directory.Build.props` → коммит → тег `vX.Y.Z` → push тега
- Установлено у владельца: `%LOCALAPPDATA%\Programs\BGCompanion`
- Ключи: `--demo`, `--overlay-test`, `--tray`, `--pick-races`, `--ocr-selftest <png>`

## Порты и внешние сервисы
- Портов нет. HearthstoneJSON (база карт), GitHub API/Releases, raw.githubusercontent (`comps.json`, `hero-stats.json`)
- Сеть через VPN нестабильна: загрузки только через `Net.CreateClient` + `Net.RetryAsync`

## Не трогать
- Только чтение логов: не читать память игры, не автоматизировать ввод, не заходить в матчи
- Не парсить HSReplay, Firestone, bgbuddy, Vicious Syndicate (условия запрещают); ники игроков не сохранять
- Русские строки и `\n`/`\\` не вставлять через bash-heredoc (ломаются) — правки через Edit

## Не читать без запроса
- `bin/`, `obj/`, `dist/`; `%LOCALAPPDATA%\BGCompanion` (games.jsonl, cards_*.json ~13 МБ)
- Логи Hearthstone (`Logs\Hearthstone_*\Power*.log` до 100 МБ) — только grep

## Отладка
- `%LOCALAPPDATA%\BGCompanion\error.log` — последние 50 строк; снимки OCR в `ocr\`

## Статус (2026-11-10)
- Версия 0.5.0, установлена у владельца; живой матч ещё не проверялся — ждём скриншоты и снимки `ocr\`
- Запрос на данные Firestone: github.com/Zero-to-Heroes/firestone/issues/1969 — ждём ответа; HSGuru и VS не спрашивать
- Дальше: правки по живой игре; позже — сервер общей статистики, режим трекера колод
