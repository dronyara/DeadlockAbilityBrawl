# Ability Brawl for Deadlock

**English** · [Русский](README.ru.md)

A server-side [Deadworks](https://github.com/Deadworks-net/deadworks) plugin. Every player drafts four abilities
from other heroes on the Street Brawl draft screen, then the lobby plays Standard or Street Brawl with those kits.

Everything runs on a server you host; players join with `connect` and need to install nothing. An optional client
addon ([`clientside/`](clientside)) adds mouse picks on the draft screen and a working TAB upgrade view.

## How a game goes
1. Somebody starts the server. Players open the Deadlock console and type `connect <ip>:27067`, then pick heroes
   (the usual way, or `/hero haze` and `/team amber|sapphire` in chat). A hero somebody already has cannot be picked.
2. The **lobby leader** (the first player who joined) types **`/draft`** in chat.
3. Everyone votes by typing a digit in chat, in two steps of 15 seconds each: first the rules, then the format.
   A step ends early once everybody has voted; a tie goes to the leader's vote.
   - **Rules**: **1** Standard, **2** Street Brawl.
   - **Format**: **3** Draft (everyone picks four abilities), **4** Chaos (a random hero and four random
     abilities, nobody picks anything), **5** Shuffle (your own hero and four random abilities; once an
     ability you used has run its course it is replaced by another random one, which takes over the rest of its
     cooldown. An item attached to the ability stays with the slot).

   Players with the client addon get the same vote as a panel with tiles: the portraits of those who voted
   appear under each tile, and the format step slides in from below once the rules are settled. `/chaos` instead
   of `/draft` skips the format vote and goes straight to Chaos.

   With the addon the lobby leader also has a panel at the right edge of the screen while the lobby is open: the
   presets, who is here and with which hero, and a Start button. Hold TAB to click it. Before the fight everyone
   with the addon is shown both teams' kits for a few seconds.
4. The match starts and the **stock Street Brawl draft screen opens with ability cards**. Four picks, the last one
   is the ultimate, with three rerolls per pick on the stock Reroll button.
   **Take a card by typing its number in chat: Enter → `1`, `2` or `3` → Enter (left, top, right).** The message is
   not shown to anyone. With the client addon a card can simply be clicked: the server finds the addon by itself. `/reroll` in chat does
   what the Reroll button does.
5. Street Brawl: after the fourth ability the same screen goes on to the usual items, and the match continues as
   Street Brawl.
   Standard: once everyone has four abilities the map reloads into a normal match, and the plugin puts every player
   back on their team and hero with their drafted kit. Everyone waits behind the spawn walls until all are back,
   then for a 10-second countdown.
   The draft has no clock: the timer on the screen stands still until the last player has taken their fourth
   ability. Under Street Brawl rules it then runs for the usual minute, which is the time for the items.
6. When the match ends, the lobby reopens after 20 seconds: everyone but the lobby leader is kicked and the map
   reloads. `/newdraft` (leader) does the reload at any time, without kicking.

No two players get the same ability or the same hero. A card another player takes while it is on your screen is
replaced for free.

The drafted kit is written into the server's own table of the hero's abilities, so the game treats it as the hero's
kit: unlocking and upgrading (ALT + ability key) and items that attach to one ability work for every player, with
nothing installed. That table is per hero, which is one more reason heroes are unique.

If the stock screen is unavailable — after a game patch the plugin may fail to find the game functions it needs, see
[PATCHING.md](PATCHING.md) — the draft falls back to a text menu in front of the crosshair before the match starts:
`1`-`3` picks a card, `R` + `1`-`3` replaces it.

## Install (server owner only)
Windows only: Deadworks has no Linux build because Valve ships no Linux server for Deadlock.

1. Deadlock from Steam (or a separate copy through SteamCMD, app `1422450`).
2. [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0).
3. The latest [Deadworks release](https://github.com/Deadworks-net/deadworks/releases), unpacked into the Deadlock folder.
4. The zip from this repository's [Releases](../../releases), unpacked into the same Deadlock folder.
5. Run `game\bin\win64\start-abilitydraft.bat`.

Join your own server with console (`` ` ``) → `connect localhost:27067`.

**Friends outside your network do not need port forwarding.** On start the plugin switches on joining through Steam
and prints the command for it in the server log (`STEAM CONNECT: connect [A:1:…]`); the lobby leader also gets it in
chat on joining, and `/id` shows it again. Friends paste that whole line, brackets included, into their console. The
id changes every time the server starts.

Running the server and the game on one machine takes a lot of memory. It works on 8 GB, but a map load takes about a
minute, and so does the reload into Standard.

### Hosting with the Deadworks launcher
The launcher's hosting runs a server from its own copy of the game, with its own plugin folder.
1. Set up hosting in the launcher (it fetches the game files and Deadworks itself) and create a server.
2. Plugins → **Add plugins** → choose the release zip (or `AbilityDraft.dll`), and turn the plugin on for the server.
   The plugin carries the signatures of the build it was made for, so nothing else has to be copied.
3. Settings: switch **Hibernate when empty** off. With the game on the same PC, add the ConVars
   `net_limit_sv_message_process_time_ms_drop_burst` and `net_limit_sv_message_process_time_ms_drop_rate`, both
   `60000` (Advanced).
4. With the **Steam relay** network mode the launcher already lets friends join through Steam; the plugin sees it
   and leaves it alone.

`AbilityDraft.config.json` and the `AbilityDraft.lang` folder appear in the server's `game\bin\win64\managed\plugins`
(Advanced → **Server folder**) after the first start.

### Building from source
```
dotnet build AbilityDraft -c Release
pip install capstone
python tools\find_sigs.py
```
The build copies the plugin into `game/bin/win64/managed/plugins`; `find_sigs.py` writes the signature file next to
it and into `AbilityDraft/signatures.json`, which the next build puts inside the DLL. If Steam is not in `C:\Program Files (x86)\Steam`, add `-p:DeadlockDir="<Deadlock folder>"` to the build and pass
the folder to the script. Start the server with `tools\host.ps1` (or `tools\host.ps1 -Lan`).

## Settings
`game\bin\win64\managed\plugins\AbilityDraft.config.json` is created on the first start and read again at every
map start and on `/draft`:
```json
{
  "Blacklist": ["Hotel Guest", "ability_frank_revive"],
  "Language": "en",
  "MaxPlayersStandard": 12,
  "MaxPlayersStreetBrawl": 8,
  "UniqueHeroes": true,
  "SecondsAfterMatch": 20,
  "DraftTimeLimit": 0,
  "PregameSeconds": 10,
  "Logging": true,
  "ShuffleSeconds": 1,
  "ShufflePassiveSeconds": 30,
  "ShuffleNoWeaponItems": true,
  "Trace": false,
  "Stats": true,
  "Presets": {
    "brawl shuffle": { "Rules": "brawl", "Format": "shuffle", "ShuffleSeconds": 0 }
  }
}
```
- `Blacklist` — abilities that are never offered. Use the internal name or the English or Russian name; all of them
  are in `AbilityDraft.abilities.txt` next to the config. Names the plugin does not recognise are reported in the log.
- `Language` — the server's language: `"en"`, `"ru"` or any other language file (see below). Each player can pick
  their own with `/lang <code>`; `/lang` alone lists the languages there are.
- `MaxPlayersStandard`, `MaxPlayersStreetBrawl` — how many players the lobby takes; a player over the larger number
  is turned away. With more players in the lobby than the Street Brawl number, a vote for Street Brawl plays Standard.
- `UniqueHeroes` — `true`: a hero somebody already has cannot be picked.
- `SecondsAfterMatch` — after a match ends, seconds until everyone but the lobby leader is kicked and the lobby
  reopens. `0` switches this off.
- `DraftTimeLimit` — seconds the ability draft waits for a player; after that the rest of their picks are made at
  random. `0` (the default) is no limit: the draft waits until everyone has picked.
- `PregameSeconds` — Standard rules: seconds everyone waits behind the spawn walls before the match starts.
- `Logging` — `false`: the plugin writes nothing to the server console or to `abilitydraft.log` in `%TEMP%`. Leave
  it on while something is being looked into: the log is what a bug report needs.
- `ShuffleSeconds` — Shuffle format: the shortest time between using an ability and getting another one in its
  place. The swap also waits for the ability to finish what it is doing.
- `ShufflePassiveSeconds` — Shuffle format: a passive ability, which is never used, is swapped after this many
  seconds. `0` leaves passives alone.
- `ShuffleNoWeaponItems` — Shuffle under Street Brawl rules: `true` keeps weapon items out of the item draft.
- `Trace` — `true` also logs every client command and key press. The log grows by tens of megabytes an evening;
  switch it on to hunt a bug and off again afterwards.
- `Stats` — `true` writes one line per finished match to `AbilityDraft.stats.jsonl` next to the config: the rules,
  the format, and each player's hero, team, kit and whether they won (no names, no Steam IDs). `/stats` in chat
  shows the five most held abilities and how often their holders won. After every match the plugin also writes
  `AbilityDraft.stats.html` there: one sortable table of all abilities, to open in a browser.
- `Presets` — named bundles of settings. The lobby leader types `/preset <name>` instead of `/draft`; `/preset` on
  its own lists them. `Rules` is `standard`, `brawl` or `vote`; `Format` is `draft`, `chaos`, `shuffle` or `vote`;
  whatever is fixed is not voted on. A preset may also carry its own `Blacklist`, `DraftTimeLimit`,
  `ShuffleSeconds`, `ShufflePassiveSeconds` and `ShuffleNoWeaponItems`; the rest comes from the main settings.

### Languages
Every text the plugin shows (chat, panels, announcements, command errors) is in a language file:
`AbilityDraft.lang/<code>.json` next to the config, one text per key:

```jsonc
{
  "draft.your_kit": "[Draft] Your kit: {0}",
  "vote.count": "-   {0}/{1} VOTED"
}
```

- English (`en.json`) and Russian (`ru.json`) are inside the plugin and are written out on the first start. Edit
  them freely: an update adds its new texts to these files and keeps your changes.
- To add a language, copy `en.json` to a new name (`de.json`, `pt-br.json`), translate the texts and restart the
  map. The file name is the code for `Language` and `/lang`. A text missing from the file is shown in English.
- `{0}`, `{1}`, … stand for what the plugin puts in: a number, a name, a list of abilities. Keep them; their order
  in the sentence can change.
- Ability and hero names come from the game's own data, which the plugin has in English and Russian only: other
  languages show the English names.

## Optional client addon
[`clientside/pak01_dir.vpk`](clientside) is ready to install; [`clientside/README.md`](clientside/README.md) says
how. Players with and without it play in the same lobby. It adds:

- **Clickable draft cards with the ability's tooltip.** The server notices the addon when the player joins and
  switches the clicks on; `/vpk off` switches them off (an addon from v1.5 or older still needs `/vpk` typed in
  chat before the draft). Every ability gets a twin item (`ad_<ability>`) the stock screen can safely "buy"; the plugin answers the purchase with the ability.
  The taken card grows and the other two fade, as in a normal Street Brawl draft.
- **The TAB upgrade view.** A click on an ability trains it, and the upgrade pips show the drafted kit's real state.
  This part comes through the Deadworks UI bridge, which the VPK includes unchanged.

The VPK carries a copy of the game's ability data, so it has to match the game build (see the file in `clientside`)
and be rebuilt after every patch that touches items or abilities:
```
tools\dump-vdata.ps1 -Vrf <vrf.dll>
python tools/build_cards.py dist/cards --csdk "<Reduced_CSDK_12 folder>"
```
This needs [CSDK 12](https://deadlockmodding.pages.dev/modding-tools/csdk-12) for its resource compiler. The script
writes `dist/cards/abilitydraft_cards_dir.vpk`.

## After a game patch
Deadworks stops starting until its next release, and the plugin's own signatures may need regenerating.
[PATCHING.md](PATCHING.md) lists what to check, in order.

## Known limits
- **Without the addon, ability cards cannot be clicked.** The stock screen treats every card as an item, and buying
  an "item" that is really an ability crashes the client, so the plugin marks the cards as already taken: a click
  does nothing and picks go through chat.
- **Without the addon, the TAB upgrade view is drawn for the hero's original abilities**: a click there does nothing
  and the pips stay empty. ALT + ability key works. With the addon the pips flicker now and then, because the game
  keeps resetting them and the addon keeps putting them back.
- **The pick animation needs the addon's script to hear from the server in time.** Those messages travel as
  closed captions; when the client stutters at the start of a match (seen with the server, the game and a
  recorder on one 8 GB machine) they get lost for about a quarter of a minute, and a pick made then goes
  through without the animation.
- **The header still says "item draft".** That text is in the client's localization.
- **A player who leaves a Street Brawl match halfway finds their own hideout in Street Brawl mode.** The game keeps
  the server's mode setting after the disconnect. Restarting the game clears it; so does typing
  `citadel_gamemode_streetbrawl_enabled 0` and `citadel_active_lane 0` in the console and reloading the hideout.
  Players who stay to the end are not affected: the plugin sets the mode back the moment the match is decided.
- **With no draft time limit one idle player holds everyone up.** Set `DraftTimeLimit`, or the lobby leader types
  `/newdraft`.
- **The server needs Deadworks v0.6.0 or newer, and the client addon must be the one from this version.** An addon
  from v1.1.1 or older carries the previous Deadworks UI bridge; with a v0.6.0 server a client with it was reported
  to crash on respawn.
- **Shuffle:** an ability is kept while something it made
  is still there (a buff, a mark, a piece on the board), for a minute at most; an effect the plugin cannot see
  behind the ability may still end when the ability is swapped.
- **One open report:** a player's client stops responding when their hero respawns (1v1, both rule sets, on
  v1.2.1). It has not been reproduced: on the author's server four players respawned dozens of times in an evening
  without it. If it happens to you, the client's crash dump from `game\bin\win64` and a try without the addon
  are what will tell where it comes from.
- v1.5.1 is for build 6812 with Deadworks v0.6.2; the server was checked with bots, the addon finding itself
  was not tried in a client yet.
- v1.5 is for build 6807 with Deadworks v0.6.2 (older Deadworks versions do not start on this build); the server
  was checked with bots. Violet and Nurse Harrow (new in this version) were tried with bots only: their abilities
  on another hero, and picking them. v1.4 (build 6785) was also checked by loading the addon in a client.
- **With the Deadworks launcher installed**, the launcher rewrites `gameinfo.gi` and the client addon's line can go
  missing, so the cards stop being clickable. `clientside/README.md` says where the line goes.
- v1.3 (build 6774, Deadworks v0.6.0): the look of the panels and the two-step vote were seen in game by one player;
  v1.2.1 was played by a lobby of four. Checked with bots only: keeping a kit, a draft seat or a vote through a
  reconnect, the held result screen, presets, the match record, passives on a clock and the item filter in Shuffle.

## What is inside
- `AbilityDraft/` — the plugin (C#): `DraftPlugin.cs` (phases, vote, the text-menu draft), `NativeDraft.cs` (the
  draft on the stock screen, the switch to Standard), `HeroBinding.cs` (the kit as the hero's own), `Session.cs`
  (player cap, unique heroes, the reset after a match, `/chaos`), `Training.cs` and `Imbue.cs` (upgrades and
  ability-targeted items for kits that could not be made the hero's own), `Native.cs` (game functions found by
  signature), `Config.cs`, `Match.cs`, `Lobby.cs`, `Ui.cs`, `Debug.cs` (log and a file-driven test bridge),
  `AbilityPool.g.cs` (ability names, generated).
- `addon/` — the client addon's own script, layouts and style. `clientside/` — the addon, built.
- `tools/` — server start scripts, the ability pool generator, the signature finder, the addon builder, release
  packaging, a crash dump reader.
- `showcase/` — a clip and screenshots.

Upgrading by click, ability-targeted items and the idea of making the kit the hero's own follow
[Binger4/Deadlock-Ability-Draft](https://github.com/Binger4/Deadlock-Ability-Draft) (MIT).

The only game data in this repository is inside `clientside/pak01_dir.vpk` (see the notes next to it). The mod was
built with AI assistance (Claude Code + universal-modder). Use it on your own servers only; it does nothing to
Valve's official servers.
