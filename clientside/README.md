# Ability Brawl — client addon / клиентский аддон

`pak01_dir.vpk` — built for **Deadlock build 6819** (23 October 2026). Optional: without it you pick cards by typing
`1`, `2`, `3` in chat and upgrade with ALT + ability key.

`pak01_dir.vpk` собран под **сборку Deadlock 6819** (23 октября 2026). Необязателен: без него карточки берут
цифрами `1`, `2`, `3` в чате, а прокачивают через ALT + кнопка способности.

## What it adds / Что даёт
- Draft cards can be clicked (the server finds the addon by itself; `/vpk off` switches the clicks off), show the ability's tooltip and play the pick
  animation: the taken card grows, the others fade, the next ones come in one by one.
  Карточки драфта кликаются (сервер сам находит аддон; `/vpk off` выключает клики), показывают подсказку способности и
  проигрывают анимацию выбора: взятая карточка растёт, остальные гаснут, новые появляются по одной.
- The TAB upgrade view works: a click trains the ability, the pips show its real state.
  Меню прокачки по TAB работает: клик прокачивает способность, полоски показывают её настоящее состояние.

## Install / Установка
1. Close Deadlock. Copy `pak01_dir.vpk` into `<Deadlock>/game/citadel/addons/` (create the folder). If you already
   have mods there, rename the file to the next free number: `pak02_dir.vpk`, `pak03_dir.vpk`, …
   Закройте Deadlock. Скопируйте `pak01_dir.vpk` в `<Deadlock>/game/citadel/addons/` (создайте папку). Если там уже
   лежат моды, переименуйте файл в следующий свободный номер: `pak02_dir.vpk`, `pak03_dir.vpk`, …
2. Open `<Deadlock>/game/citadel/gameinfo.gi` in a text editor and make the end of the `SearchPaths` block read
   exactly this (skip this step if a mod guide already had you do it):
   Откройте `<Deadlock>/game/citadel/gameinfo.gi` в текстовом редакторе и приведите конец блока `SearchPaths` ровно
   к такому виду (пропустите шаг, если уже делали это по инструкции к другим модам):
   ```
   Game    citadel/addons
   Mod     citadel
   Write   citadel
   Game    citadel
   Mod     core
   Write   core
   Game    core
   ```
   With only the first line added the game does not start.
   Если добавить только первую строку, игра не запустится.

   **With the Deadworks launcher installed** the block looks different: the launcher keeps its own lines between
   `// Deadworks Launcher - Start` and `// Deadworks Launcher - End` and writes that part anew, so a line added there
   is lost. Leave its lines as they are and add one line right after the `End` comment:
   **Если установлен лаунчер Deadworks**, блок выглядит иначе: лаунчер держит свои строки между
   `// Deadworks Launcher - Start` и `// Deadworks Launcher - End` и переписывает эту часть заново, так что добавленная
   туда строка пропадает. Его строки не трогайте, а сразу после комментария `End` добавьте одну строку:
   ```
   // Deadworks Launcher - End
   Game    citadel/addons
   Game    citadel
   Game    core
   ```
3. Start the game. A game update, and the Deadworks launcher when it sets itself up again, restore `gameinfo.gi`:
   repeat step 2 after them. If the cards cannot be clicked, check this line first.
   Запустите игру. Обновление игры, а также лаунчер Deadworks, когда он заново настраивает себя, возвращают
   `gameinfo.gi` к своему виду: после них повторите шаг 2. Если карточки не кликаются, первым
   делом проверьте эту строку.

To remove it, delete the VPK. / Чтобы убрать аддон, удалите VPK.

## Read before using / Прочтите перед использованием
- **It replaces the game's ability and item data with a copy from build 6819.** After a game patch that changes
  items or abilities this copy is out of date — also in normal matchmaking, where you would see old numbers. Remove
  the VPK after a patch until a rebuilt one is published here, or rebuild it yourself (see the main README).
  **Аддон подменяет данные способностей и предметов игры копией из сборки 6819.** После патча, который меняет
  предметы или способности, эта копия устаревает — в том числе в обычном матчмейкинге, где вы увидите старые цифры.
  После патча уберите VPK, пока здесь не появится пересобранный, или пересоберите сами (см. основной README).
- It also replaces the layout of the hideout screen, to load the Deadworks UI bridge. The bridge does nothing until
  a Deadworks server asks it to.
  Он также подменяет разметку экрана убежища, чтобы загрузить мост интерфейса Deadworks. Мост ничего не делает,
  пока его не попросит сервер Deadworks.
- Client mods are used at your own risk; this one is meant for private servers.
  Клиентские моды используются на свой риск; этот рассчитан на частные серверы.

## What is inside / Что внутри
| File | What it is |
|---|---|
| `scripts/abilities.vdata_c` | The game's ability and item data (© Valve) plus one item per draftable ability, built by `tools/build_cards.py` |
| `resource/localization/citadel_gc_mod_names/*` | The game's item names (© Valve) plus the names of those items |
| `panorama/layout/hud_hideout.vxml_c`, `panorama/scripts/dw_bootstrap.vjs_c`, `panorama/scripts/dw_addon.vjs_c` | The Deadworks UI bridge, unchanged, from Deadworks' client bootstrap v8. Scripts under GPL-3.0; source: [`client-bootstrap`](https://github.com/Deadworks-net/deadworks/tree/v0.6.2/client-bootstrap) |
| `panorama/layout/abilitydraft_*.vxml_c`, `panorama/scripts/abilitydraft_hud.vjs_c`, `panorama/styles/abilitydraft_click.vcss_c` | This project's own script, layouts and style; source in [`addon/`](../addon) |

SHA-256: `a2bf31102e655bf2b709521a4dada7e8aebc530fade514c31851edb86a934fb8`
