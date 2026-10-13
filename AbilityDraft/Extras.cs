using System.Text;
using System.Text.Json;
using DeadworksManaged.Api;

namespace AbilityDraft;

// Presets (a named bundle of settings the lobby leader starts a match with) and the match record.
public sealed partial class DraftPlugin
{
    // ---- presets -------------------------------------------------------------------------------------------------
    /// <summary>One entry of "Presets" in the config. Anything left out keeps the value of the main settings.</summary>
    sealed class Preset
    {
        /// <summary>"standard", "brawl" or "vote".</summary>
        public string Rules { get; set; } = "vote";
        /// <summary>"draft", "chaos", "shuffle" or "vote".</summary>
        public string Format { get; set; } = "vote";
        public List<string>? Blacklist { get; set; }
        public int? DraftTimeLimit { get; set; }
        public float? ShuffleSeconds { get; set; }
        public float? ShufflePassiveSeconds { get; set; }
        public bool? ShuffleNoWeaponItems { get; set; }
    }

    string? _preset;        // the preset this match was started with; applied again whenever the config is read

    Preset? ActivePreset(ConfigFile config) =>
        _preset != null && config.Presets.FirstOrDefault(p => p.Key.Equals(_preset, StringComparison.OrdinalIgnoreCase)).Value is { } preset ? preset : null;

    static void ApplyPreset(ConfigFile config, Preset preset)
    {
        if (preset.DraftTimeLimit is { } limit) config.DraftTimeLimit = limit;
        if (preset.ShuffleSeconds is { } swap) config.ShuffleSeconds = swap;
        if (preset.ShufflePassiveSeconds is { } passive) config.ShufflePassiveSeconds = passive;
        if (preset.ShuffleNoWeaponItems is { } noWeapons) config.ShuffleNoWeaponItems = noWeapons;
    }

    [Command("preset", Description = "Lobby leader: start a match with a preset from the config: /preset <name> (no name lists them)")]
    public void CmdPreset(CCitadelPlayerController? caller = null, string name = "")
    {
        if (caller != null && caller.Slot != Host()) throw new CommandException("Only the lobby leader can start a match.");
        if (_phase != Phase.Lobby) throw new CommandException($"A match is already being set up ({_phase}).");
        LoadConfig();
        var match = _config.Presets.Keys.FirstOrDefault(k => k.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase));
        if (match == null)
            throw new CommandException(_config.Presets.Count == 0 ? "The config has no presets." : "Presets: " + string.Join(", ", _config.Presets.Keys));
        StartPreset(match);
    }

    void StartPreset(string name)
    {
        _preset = name;
        var preset = _config.Presets[name];
        Log($"PRESET {name}: rules {preset.Rules}, format {preset.Format}");
        // What the preset fixes is set before the vote opens, so the vote only asks for the rest.
        var format = ParseFormat(preset.Format);
        _forcedRules = preset.Rules.Trim().ToLowerInvariant() switch
        {
            "standard" or "std" => Rules.Standard,
            "brawl" or "streetbrawl" or "street brawl" => Rules.StreetBrawl,
            _ => Rules.None,
        };
        _forcedFormat = format;
        if (format == Format.Chaos) StartChaos();       // both read the config again, with the preset applied
        else StartDraft();
        Chat.PrintToChatAll(L($"[Draft] Пресет: {name}.", $"[Draft] Preset: {name}."));
        // Nothing left to vote on: the vote that was just opened ends at once.
        if (_forcedRules != Rules.None && _forcedFormat != Format.None) _phaseEnd = Now;
    }

    // ---- match record --------------------------------------------------------------------------------------------
    // One line per finished match in AbilityDraft.stats.jsonl next to the plugin: what was played, who had what, who
    // won. /stats reads it back. Players are recorded by hero and team, not by name or Steam ID.
    static string StatsPath => Path.Combine(PluginDir, "AbilityDraft.stats.jsonl");

    sealed record StatsPlayer(string Hero, int Team, bool Won, string[] Kit);
    sealed record StatsMatch(string Time, string Rules, string Format, int Winner, List<StatsPlayer> Players);

    void RecordMatch()
    {
        if (!_config.Stats || _kits.Count == 0) return;
        try
        {
            int winner = GameRules.WinningTeam;
            var players = new List<StatsPlayer>();
            foreach (var (slot, kit) in _kits)
            {
                if (Players.FromSlot(slot) is not { IsBot: false } c || c.GetHeroPawn() is not { } pawn) continue;
                players.Add(new StatsPlayer(pawn.HeroID.ToHeroName(), c.TeamNum, c.TeamNum == winner, kit.ToArray()));
            }
            if (players.Count == 0) return;
            var match = new StatsMatch(DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"), _rules.ToString(), _format.ToString(), winner, players);
            File.AppendAllText(StatsPath, JsonSerializer.Serialize(match) + "\n", new UTF8Encoding(false));
            Log($"match recorded: {players.Count} players, winner team {winner}");
            WriteStatsPage();
        }
        catch (Exception ex)
        {
            Log($"match record failed: {ex.Message}");
        }
    }

    [Command("stats", Description = "The most picked abilities and how often their holders won")]
    public void CmdStats(CCitadelPlayerController caller)
    {
        if (!File.Exists(StatsPath)) throw new CommandException("No matches recorded yet.");
        var seen = new Dictionary<string, (int Games, int Wins)>();
        int matches = 0;
        foreach (var line in File.ReadLines(StatsPath))
        {
            StatsMatch? match;
            try { match = JsonSerializer.Deserialize<StatsMatch>(line); } catch { continue; }
            if (match?.Players == null) continue;
            matches++;
            foreach (var p in match.Players)
                foreach (var ability in p.Kit)
                {
                    seen.TryGetValue(ability, out var s);
                    seen[ability] = (s.Games + 1, s.Wins + (p.Won ? 1 : 0));
                }
        }
        bool ru = _seats.TryGetValue(caller.Slot, out var seat) ? seat.Ru : _ru;
        if (WriteStatsPage() != null && caller.Slot == Host())
            Chat.PrintToChat(caller, ru ? "[Draft] Полная таблица: AbilityDraft.stats.html рядом с плагином на сервере." : "[Draft] The full table: AbilityDraft.stats.html next to the plugin on the server.");
        Chat.PrintToChat(caller, ru ? $"[Draft] Записано матчей: {matches}. Чаще всего:" : $"[Draft] Matches recorded: {matches}. Most held:");
        foreach (var (ability, s) in seen.OrderByDescending(s => s.Value.Games).ThenByDescending(s => s.Value.Wins).Take(5))
            Chat.PrintToChat(caller, $"  {AbilityPool.Find(ability)?.Title(ru) ?? ability}: {s.Games} ({100 * s.Wins / s.Games}% {(ru ? "побед" : "won")})");
    }
}
