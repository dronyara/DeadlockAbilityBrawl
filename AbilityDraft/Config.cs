using System.Text;
using System.Text.Json;
using DeadworksManaged.Api;

namespace AbilityDraft;

// The server owner's settings: AbilityDraft.config.json next to the plugin DLL. Read at every map start, so an edit takes
// effect in the next lobby without restarting the server.
public sealed partial class DraftPlugin
{
    sealed class ConfigFile
    {
        /// <summary>Abilities never offered in the draft: internal names, or the English or Russian names shown in game.</summary>
        public List<string> Blacklist { get; set; } = new();
        /// <summary>The server's language: a file name in AbilityDraft.lang ("en", "ru", ...). A player can still pick their own with /lang.</summary>
        public string Language { get; set; } = "en";
        /// <summary>How many players the lobby takes. More than MaxPlayersStreetBrawl can only play Standard.</summary>
        public int MaxPlayersStandard { get; set; } = 12;
        public int MaxPlayersStreetBrawl { get; set; } = 8;
        /// <summary>No two players on the same hero.</summary>
        public bool UniqueHeroes { get; set; } = true;
        /// <summary>After a match ends: seconds until everyone but the lobby leader is kicked and the lobby reopens. 0 = never.</summary>
        public int SecondsAfterMatch { get; set; } = 20;
        /// <summary>Seconds the draft waits for a player before making the rest of their picks at random. 0 = no limit.</summary>
        public int DraftTimeLimit { get; set; } = 0;
        /// <summary>Standard rules: seconds everyone waits behind the spawn walls before the match starts. 0 = none.</summary>
        public int PregameSeconds { get; set; } = 10;
        /// <summary>Write the plugin's log (the server console and the abilitydraft.log file in %TEMP%).</summary>
        public bool Logging { get; set; } = true;
        /// <summary>Shuffle format: seconds between using an ability and getting another one in its place.</summary>
        public float ShuffleSeconds { get; set; } = 1f;
        /// <summary>Shuffle format: a passive ability is swapped after this many seconds. 0 = passives stay.</summary>
        public float ShufflePassiveSeconds { get; set; } = 30f;
        /// <summary>Shuffle format under Street Brawl rules: weapon items are kept out of the item draft.</summary>
        public bool ShuffleNoWeaponItems { get; set; } = true;
        /// <summary>Also log every client command and key press. Large: for hunting a bug, not for everyday play.</summary>
        public bool Trace { get; set; } = false;
        /// <summary>Record every finished match in AbilityDraft.stats.jsonl (heroes, kits, the winner; no names).</summary>
        public bool Stats { get; set; } = true;
        /// <summary>Named bundles of settings for /preset.</summary>
        public Dictionary<string, Preset> Presets { get; set; } = new();
    }

    ConfigFile _config = new();

    static readonly JsonSerializerOptions ConfigJson = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
    };

    static string PluginDir => Path.Combine(Path.GetDirectoryName(Environment.ProcessPath) ?? ".", "managed", "plugins");
    static string ConfigPath => Path.Combine(PluginDir, "AbilityDraft.config.json");
    static string AbilityListPath => Path.Combine(PluginDir, "AbilityDraft.abilities.txt");

    readonly HashSet<string> _blacklist = new(StringComparer.OrdinalIgnoreCase);     // internal ability names

    /// <summary>Everything the draft may offer: the generated pool minus the host's blacklist.</summary>
    IEnumerable<AbilityDef> Pool => AbilityPool.All.Where(a => !_blacklist.Contains(a.Name));

    void LoadConfig()
    {
        _blacklist.Clear();
        try
        {
            if (!File.Exists(ConfigPath))
                WriteConfig(new ConfigFile());
            WriteAbilityList();

            string text = File.ReadAllText(ConfigPath);
            var config = JsonSerializer.Deserialize<ConfigFile>(text, ConfigJson) ?? new ConfigFile();
            // A config written by an older version: add the settings it does not have yet, keep the ones it has.
            if (!text.Contains(nameof(ConfigFile.Presets), StringComparison.OrdinalIgnoreCase)) WriteConfig(config);
            var preset = ActivePreset(config);
            if (preset != null) ApplyPreset(config, preset);
            _config = config;
            _trace = config.Trace;
            LoadLanguages();
            _lang = LangCode(config.Language);
            if (!Languages.ContainsKey(_lang))
            {
                Log($"config: no language file for \"{config.Language}\" in {LangDir} - using English");
                _lang = FallbackLang;
            }
            if (_logging && !config.Logging) Log("config: logging is switched off from here on");
            _logging = config.Logging;
            var unknown = new List<string>();
            foreach (var entry in (preset?.Blacklist ?? config.Blacklist).Select(e => e.Trim()).Where(e => e.Length > 0))
            {
                var hits = AbilityPool.All.Where(a =>
                    a.Name.Equals(entry, StringComparison.OrdinalIgnoreCase) ||
                    a.En.Equals(entry, StringComparison.OrdinalIgnoreCase) ||
                    a.Ru.Equals(entry, StringComparison.OrdinalIgnoreCase)).ToList();
                if (hits.Count == 0) unknown.Add(entry);
                foreach (var a in hits) _blacklist.Add(a.Name);
            }
            Log($"config: language {_lang} (available: {string.Join(", ", Languages.Keys.OrderBy(k => k))}), players {config.MaxPlayersStandard} (Street Brawl {config.MaxPlayersStreetBrawl}), unique heroes {config.UniqueHeroes}, " +
                $"{_blacklist.Count} abilities blacklisted{(_blacklist.Count > 0 ? " (" + string.Join(", ", _blacklist) + ")" : "")}" +
                (unknown.Count > 0 ? $"; NOT RECOGNISED: {string.Join(", ", unknown)}" : ""));
        }
        catch (Exception ex)
        {
            Log($"config: could not read {ConfigPath} ({ex.Message}) - no blacklist applied");
        }
    }

    static void WriteConfig(ConfigFile config) =>
        File.WriteAllText(ConfigPath, "// Ability Draft settings. Read at every map start and on /draft.\n" +
            "// Blacklist: abilities that are never offered. Use the internal name or the English / Russian name\n" +
            "//   from AbilityDraft.abilities.txt, for example: \"Blacklist\": [\"Hotel Guest\", \"ability_frank_revive\"]\n" +
            "// Language: the server's language - \"en\", \"ru\" or any other file in the AbilityDraft.lang folder.\n" +
            "//   Every text is in those files: edit them, or copy en.json to a new code (de.json) to translate.\n" +
            "//   Each player can pick their own language with /lang.\n" +
            "// MaxPlayersStandard / MaxPlayersStreetBrawl: how many players the lobby takes; with more than the\n" +
            "//   Street Brawl number in the lobby only Standard can be played.\n" +
            "// UniqueHeroes: true - a hero somebody already has cannot be picked.\n" +
            "// SecondsAfterMatch: after a match ends, seconds until everyone but the lobby leader is kicked and the\n" +
            "//   lobby reopens. 0 switches this off.\n" +
            "// DraftTimeLimit: seconds the ability draft waits for a player; after that the rest of their picks are\n" +
            "//   made at random. 0 - no limit, the draft waits until everyone has picked.\n" +
            "// PregameSeconds: Standard rules - seconds everyone waits behind the spawn walls before the match starts.\n" +
            "// Logging: false - the plugin writes nothing to the server console or to its log file in %TEMP%.\n" +
            "// ShuffleSeconds: Shuffle format - seconds between using an ability and getting another one in its place.\n" +
            "// ShufflePassiveSeconds: Shuffle format - a passive ability is swapped after this many seconds. 0 - never.\n" +
            "// ShuffleNoWeaponItems: Shuffle under Street Brawl rules - no weapon items in the item draft.\n" +
            "// Trace: true - also log every client command and key press (a large log; for hunting a bug).\n" +
            "// Stats: true - one line per finished match in AbilityDraft.stats.jsonl; /stats reads it.\n" +
            "// Presets: named settings for /preset <name>, for example\n" +
            "//   \"Presets\": { \"brawl shuffle\": { \"Rules\": \"brawl\", \"Format\": \"shuffle\", \"ShuffleSeconds\": 0 } }\n" +
            "//   Rules: standard | brawl | vote. Format: draft | chaos | shuffle | vote. A preset may also set\n" +
            "//   Blacklist, DraftTimeLimit, ShuffleSeconds, ShufflePassiveSeconds and ShuffleNoWeaponItems.\n" +
            JsonSerializer.Serialize(config, ConfigJson) + "\n", new UTF8Encoding(false));

    /// <summary>A plain list of every ability name the blacklist accepts, kept next to the config for the host.</summary>
    static void WriteAbilityList()
    {
        var sb = new StringBuilder("internal name | English | Russian | hero | ultimate\n");
        foreach (var a in AbilityPool.All.OrderBy(a => a.HeroEn).ThenBy(a => a.Slot))
            sb.Append($"{a.Name} | {a.En} | {a.Ru} | {a.HeroEn} | {(a.Ult ? "yes" : "no")}\n");
        var text = sb.ToString();
        if (!File.Exists(AbilityListPath) || File.ReadAllText(AbilityListPath) != text)
            File.WriteAllText(AbilityListPath, text, new UTF8Encoding(false));
    }
}
