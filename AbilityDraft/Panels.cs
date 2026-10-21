using System.Net;
using System.Text;
using System.Text.Json;
using DeadworksManaged.Api;
using DeadworksManaged.Api.UI;

namespace AbilityDraft;

// The smaller panels for clients with the UI bridge - the lobby leader's panel, the kits before a match, the note
// about a swapped ability - and the statistics page.
public sealed partial class DraftPlugin
{
    bool HasBridge(int slot) => PanelUi && Players.FromSlot(slot) is { IsBot: false } && UI.HasClientBootstrap(slot);

    // ---- the lobby leader's panel ----------------------------------------------------------------------------------
    // Docked at the right edge while the lobby is open: the presets, who is here and with which hero, and Start.
    // The mouse is free while TAB is held, which is when its buttons can be clicked.
    const string LobbyPanelId = "abilitydraft_lobby";
    int _lobbyPanelSlot = -1;
    string _lobbyPanelShown = "";
    string? _lobbyPreset;           // null: nothing fixed, the lobby votes for the rules and the format
    bool _lobbyWired;
    float _lobbyNextLook;

    void TickLobbyPanel()
    {
        int leader = _phase == Phase.Lobby ? Host() : -1;
        if (leader >= 0 && !HasBridge(leader)) leader = -1;
        if (_lobbyPanelSlot >= 0 && _lobbyPanelSlot != leader)
        {
            UI.Panel(LobbyPanelId).DestroyLayout(RecipientFilter.Single(_lobbyPanelSlot));
            _lobbyPanelSlot = -1;
            _lobbyPanelShown = "";
        }
        if (leader < 0 || Now < _lobbyNextLook) return;
        _lobbyNextLook = Now + 1f;
        if (!_lobbyWired)
        {
            _lobbyWired = true;
            var panel = UI.Panel(LobbyPanelId);
            panel.On("lobbypreset", e =>
            {
                if (e.Caller?.Slot != Host() || _phase != Phase.Lobby) return;
                string name = e.ArgAt(0) ?? "";
                _lobbyPreset = _config.Presets.Keys.FirstOrDefault(k => k.Equals(name, StringComparison.OrdinalIgnoreCase));
                _lobbyNextLook = 0;
            });
            panel.On("lobbystart", e =>
            {
                if (e.Caller is not { } caller || caller.Slot != Host() || _phase != Phase.Lobby) return;
                try
                {
                    LoadConfig();
                    if (_lobbyPreset != null && _config.Presets.ContainsKey(_lobbyPreset)) StartPreset(_lobbyPreset);
                    else StartDraft();
                }
                catch (CommandException ex)
                {
                    Chat.PrintToChat(caller, "[Draft] " + ex.Message);
                }
            });
            UI.ClientResync += LobbyResync;
        }

        var players = Players.GetAll().Where(p => !p.IsBot).OrderBy(p => p.Slot).ToList();
        string lang = LangOf(leader);
        string shown = $"{lang}|{_lobbyPreset}|{string.Join(",", _config.Presets.Keys)}|" + string.Join(";", players.Select(p => $"{p.Slot}:{p.PlayerName}:{p.GetHeroPawn()?.HeroID}"));
        if (leader == _lobbyPanelSlot && shown == _lobbyPanelShown) return;
        _lobbyPanelSlot = leader;
        _lobbyPanelShown = shown;
        UI.Panel(LobbyPanelId).BuildLayout(RecipientFilter.Single(leader), LobbyLayout(players, lang));
    }

    void LobbyResync(int slot)
    {
        if (slot == _lobbyPanelSlot) _lobbyPanelShown = "";
        _summaryShown.Remove(slot);
    }

    UINode LobbyLayout(List<CCitadelPlayerController> players, string lang)
    {
        (string, string)[] section = [("font-size", "15px"), ("color", VoteDim), ("letter-spacing", "2px"), ("margin-top", "12px")];
        var body = UI.Vertical("al_body").WithStyles(("width", "100%"), ("padding", "2px 16px 12px 16px"));

        body.Add(UI.Label("al_presets", Tl(lang, "panel.preset")).WithStyles(section));
        body.Add(Outlined("al_p_none", Tl(lang, "panel.vote_everything"), "lobbypreset", "", _lobbyPreset == null).WithStyles(("width", "100%"), ("margin-top", "6px")));
        int n = 0;
        foreach (var name in _config.Presets.Keys)
            body.Add(Outlined($"al_p{n++}", name, "lobbypreset", name, name.Equals(_lobbyPreset, StringComparison.OrdinalIgnoreCase)).WithStyles(("width", "100%"), ("margin-top", "6px")));

        var list = UI.Vertical("al_list").WithStyles(("width", "100%"), ("padding", "0px 8px 10px 8px"));
        n = 0;
        foreach (var p in players)
        {
            string? icon = HeroIcon(p.Slot);
            bool amber = p.TeamNum != 3;
            var row = UI.Horizontal($"al_r{n}").WithStyles(("width", "100%"), ("height", "38px"), ("margin-top", "4px"), ("background-color", RowFill),
                ("border-left", $"2px solid {(icon == null ? "#d85a30" : amber ? AmberEdge : SapphireEdge)}"));
            row.Add(icon != null
                ? UI.Image($"al_i{n}", icon).WithStyles(("width", "30px"), ("height", "30px"), ("margin", "4px 8px 0px 6px"), ("border-radius", "50%"))
                : UI.Label($"al_i{n}", "?").WithStyles(("width", "30px"), ("font-size", "22px"), ("color", "#d85a30"), ("margin", "4px 8px 0px 14px")));
            row.Add(UI.Label($"al_n{n}", p.PlayerName + (p.Slot == Host() ? Tl(lang, "panel.leader") : "")).WithStyles(("font-size", "17px"), ("color", VoteText), ("vertical-align", "center")));
            if (icon == null)
                row.Add(UI.Label($"al_h{n}", Tl(lang, "panel.no_hero")).WithStyles(("font-size", "14px"), ("color", "#f09a7a"), ("vertical-align", "center")));
            list.Add(row);
            n++;
        }
        body.Add(Card("al_players", Tl(lang, "panel.players", players.Count, Math.Max(_config.MaxPlayersStandard, _config.MaxPlayersStreetBrawl)), true, list,
            ("width", "100%"), ("margin-top", "14px")));

        body.Add(Outlined("al_start", Tl(lang, "panel.start"), "lobbystart", "", chosen: true).WithStyles(("width", "100%"), ("height", "50px"), ("margin-top", "14px")));

        var sheet = Sheet("al_window", Tl(lang, "panel.lobby"), [Caps("al_sub", "ABILITY BRAWL")], body,
            Tl(lang, "panel.lobby_hint"),
            ("width", "360px"), ("horizontal-align", "right"), ("vertical-align", "center"), ("margin-right", "24px"));
        return UI.Container("al_root").WithStyles(("width", "100%"), ("height", "100%")).Add(sheet);
    }

    // ---- the kits, before the fight ------------------------------------------------------------------------------
    const string SummaryPanelId = "abilitydraft_summary";
    readonly HashSet<int> _summaryShown = new();
    int _summarySeq;

    /// <summary>Shows everyone's kit for a few seconds: both teams, a row per player, the four ability icons.</summary>
    void ShowKits(float seconds)
    {
        var viewers = _kits.Keys.Where(HasBridge).ToList();
        if (viewers.Count == 0 || _kits.Count == 0) return;
        _summaryShown.Clear();
        foreach (int slot in viewers)
        {
            UI.Panel(SummaryPanelId).BuildLayout(RecipientFilter.Single(slot), KitsLayout(LangOf(slot)));
            _summaryShown.Add(slot);
        }
        int seq = ++_summarySeq;
        Timer.Once(TimeSpan.FromSeconds(seconds), () =>
        {
            if (seq != _summarySeq) return;
            foreach (int slot in _summaryShown.ToList())
                if (Players.FromSlot(slot) != null) UI.Panel(SummaryPanelId).DestroyLayout(RecipientFilter.Single(slot));
            _summaryShown.Clear();
        });
    }

    UINode KitsLayout(string lang)
    {
        UINode Team(string id, int team, string title, bool amber)
        {
            var list = UI.Vertical(id + "_list").WithStyles(("width", "100%"), ("padding", "0px 8px 10px 8px"));
            int n = 0;
            foreach (var (slot, kit) in _kits.OrderBy(k => k.Key))
            {
                if (Players.FromSlot(slot) is not { } c || c.TeamNum != team) continue;
                var row = UI.Horizontal($"{id}_r{n}").WithStyles(("width", "100%"), ("height", "54px"), ("margin-top", "4px"), ("background-color", RowFill),
                    ("border-left", $"2px solid {(amber ? AmberEdge : SapphireEdge)}"));
                string? hero = HeroIcon(slot);
                if (hero != null) row.Add(UI.Image($"{id}_h{n}", hero).WithStyles(("width", "40px"), ("height", "40px"), ("margin", "7px 8px 0px 8px"), ("border-radius", "50%")));
                row.Add(UI.Label($"{id}_n{n}", c.PlayerName).WithStyles(("width", "150px"), ("font-size", "17px"), ("color", VoteText), ("vertical-align", "center"),
                    ("margin-left", hero == null ? "12px" : "0px"), ("text-overflow", "ellipsis")));
                for (int i = 0; i < kit.Length; i++)
                    if (AbilityPool.Find(kit[i]) is { } ability)
                        row.Add(UI.Image($"{id}_a{n}_{i}", ability.Icon).WithStyles(("width", "42px"), ("height", "42px"), ("margin", "6px 4px 0px 4px"),
                            ("border-radius", "50%"), ("border", $"2px solid {(ability.Ult ? VoteChosen : TileEdge)}"), ("background-color", TileFill)));
                list.Add(row);
                n++;
            }
            return Card(id, title, amber, list, ("width", "440px"), ("margin", "0px 8px"));
        }

        var teams = UI.Horizontal("ak_teams").WithStyles(("horizontal-align", "center"), ("margin", "16px 8px 14px 8px"));
        teams.Add(Team("ak_amber", 2, Tl(lang, "panel.team_amber"), true), Team("ak_sapphire", 3, Tl(lang, "panel.team_sapphire"), false));
        var sheet = Sheet("ak_window", Tl(lang, "panel.kits"),
            [Caps("ak_sub", FormatName(_format, lang).ToUpperInvariant()), Caps("ak_rules", "-   " + RulesName(_rules).ToUpperInvariant())], teams, null,
            ("horizontal-align", "center"), ("vertical-align", "top"), ("margin-top", "90px"));
        return UI.Container("ak_root").WithStyles(("width", "100%"), ("height", "100%")).Add(sheet);
    }

    // ---- "your ability changed" ------------------------------------------------------------------------------------
    const string TossPanelId = "abilitydraft_swap";
    readonly Dictionary<int, int> _tossSeq = new();

    /// <summary>A small note above the ability bar for a client with the bridge. Returns false when there is none.</summary>
    bool ShowSwap(CCitadelPlayerController c, int slot, AbilityDef next, string lang)
    {
        if (!HasBridge(c.Slot)) return false;
        var to = RecipientFilter.Single(c.Slot);
        var note = UI.Horizontal("as_note").WithStyles(("horizontal-align", "center"), ("vertical-align", "bottom"), ("margin-bottom", "190px"),
            ("background-color", SapphireFill), ("border-top", $"2px solid {SapphireEdge}"), ("padding", "6px 16px 6px 8px")).Add(
            UI.Image("as_icon", next.Icon).WithStyles(("width", "40px"), ("height", "40px"), ("border-radius", "50%"), ("background-color", TileFill)),
            UI.Label("as_text", $"{slot + 1}  ·  {next.Title(lang)}").WithStyles(("font-size", "20px"), ("color", VoteText), ("vertical-align", "center"), ("margin-left", "10px")));
        UI.Panel(TossPanelId).BuildLayout(to, UI.Container("as_root").WithStyles(("width", "100%"), ("height", "100%")).Add(note));
        int player = c.Slot, seq = _tossSeq[player] = _tossSeq.GetValueOrDefault(player) + 1;
        Timer.Once(TimeSpan.FromSeconds(2.5), () =>
        {
            if (_tossSeq.GetValueOrDefault(player) == seq && Players.FromSlot(player) != null) UI.Panel(TossPanelId).DestroyLayout(RecipientFilter.Single(player));
        });
        return true;
    }

    // ---- the statistics page ---------------------------------------------------------------------------------------
    static string StatsPagePath => Path.Combine(PluginDir, "AbilityDraft.stats.html");

    /// <summary>Writes AbilityDraft.stats.html next to the plugin from the match record: one sortable table of abilities.</summary>
    string? WriteStatsPage()
    {
        try
        {
            if (!File.Exists(StatsPath)) return null;
            var seen = new Dictionary<string, (int Games, int Wins)>();
            var formats = new Dictionary<string, int>();
            int matches = 0;
            foreach (var line in File.ReadLines(StatsPath))
            {
                StatsMatch? match;
                try { match = JsonSerializer.Deserialize<StatsMatch>(line); } catch { continue; }
                if (match?.Players == null) continue;
                matches++;
                formats[$"{match.Format} / {match.Rules}"] = formats.GetValueOrDefault($"{match.Format} / {match.Rules}") + 1;
                foreach (var p in match.Players)
                    foreach (var ability in p.Kit)
                    {
                        seen.TryGetValue(ability, out var s);
                        seen[ability] = (s.Games + 1, s.Wins + (p.Won ? 1 : 0));
                    }
            }
            static string E(string s) => WebUtility.HtmlEncode(s);
            var html = new StringBuilder();
            html.Append("<!doctype html><html lang=\"en\"><head><meta charset=\"utf-8\"><meta name=\"viewport\" content=\"width=device-width,initial-scale=1\">")
                .Append("<title>Ability Brawl statistics</title><style>")
                .Append("body{background:#16130f;color:#e9e2d2;font:15px/1.5 system-ui,sans-serif;margin:0;padding:24px}h1{font-size:22px;letter-spacing:2px;margin:0 0 4px}")
                .Append("p{color:#8f8777;margin:0 0 16px}table{border-collapse:collapse;width:100%;max-width:900px}th,td{padding:6px 10px;border-bottom:1px solid #3b352c;text-align:left}")
                .Append("th{color:#d9a441;cursor:pointer;user-select:none;background:#0d0b09;position:sticky;top:0}td.n{text-align:right;font-variant-numeric:tabular-nums}")
                .Append(".bar{display:inline-block;height:8px;background:#d9a441;vertical-align:middle;margin-right:8px}.u{color:#d9a441}</style></head><body>")
                .Append("<h1>ABILITY BRAWL</h1><p>").Append(matches).Append(" matches recorded: ")
                .Append(E(string.Join(", ", formats.OrderByDescending(f => f.Value).Select(f => $"{f.Key} {f.Value}"))))
                .Append(". Click a column to sort.</p><table id=\"t\"><thead><tr><th>Ability</th><th>Hero</th><th>Kind</th><th>Held</th><th>Won</th><th>Win rate</th></tr></thead><tbody>");
            foreach (var (ability, s) in seen.OrderByDescending(s => s.Value.Games).ThenBy(s => s.Key))
            {
                var def = AbilityPool.Find(ability);
                int rate = 100 * s.Wins / s.Games;
                html.Append("<tr><td>").Append(E(def?.En ?? ability)).Append("</td><td>").Append(E(def?.HeroEn ?? "")).Append("</td><td")
                    .Append(def?.Ult == true ? " class=\"u\">ultimate" : ">ability").Append("</td><td class=\"n\">").Append(s.Games).Append("</td><td class=\"n\">").Append(s.Wins)
                    .Append("</td><td data-v=\"").Append(rate).Append("\"><span class=\"bar\" style=\"width:").Append(rate).Append("px\"></span>").Append(rate).Append("%</td></tr>");
            }
            html.Append("</tbody></table><script>document.querySelectorAll('th').forEach(function(th,i){var up=false;th.onclick=function(){var b=document.querySelector('#t tbody');")
                .Append("var v=function(r){var c=r.children[i];var x=c.dataset.v||c.textContent;return isNaN(x)?x.toLowerCase():+x};up=!up;")
                .Append("Array.from(b.rows).sort(function(a,c){var x=v(a),y=v(c);return(x<y?-1:x>y?1:0)*(up?1:-1)}).forEach(function(r){b.appendChild(r)})}});</script></body></html>");
            File.WriteAllText(StatsPagePath, html.ToString(), new UTF8Encoding(false));
            return StatsPagePath;
        }
        catch (Exception ex)
        {
            Log($"statistics page failed: {ex.Message}");
            return null;
        }
    }
}
