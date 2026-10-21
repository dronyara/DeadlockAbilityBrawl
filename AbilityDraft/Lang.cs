using System.Globalization;
using System.Reflection;
using System.Text;
using System.Text.Json;
using DeadworksManaged.Api;

namespace AbilityDraft;

// Every text the plugin shows lives in a language file, one per language, keyed by name:
//   AbilityDraft.lang/en.json, AbilityDraft.lang/ru.json, ... next to the plugin DLL.
// English and Russian ship inside the DLL and are written out on the first start; a server owner can edit them, and a
// copy saved under another code (de.json, pt-br.json) is a new language. A text missing from a file falls back to
// English, so an old translation keeps working when a new version adds texts - and the next start adds the new keys
// to the shipped languages' files, keeping the owner's edits.
// "Language" in the config is the server's default; every player can pick their own with /lang.
public sealed partial class DraftPlugin
{
    const string FallbackLang = "en";
    static string _lang = FallbackLang;                     // the server's default language (config "Language")
    static readonly Dictionary<string, Dictionary<string, string>> Languages = new(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<ulong, string> _playerLang = new();  // /lang choices, by Steam ID, for as long as the server runs

    static string LangDir => Path.Combine(PluginDir, "AbilityDraft.lang");

    static readonly JsonSerializerOptions LangJson = new()
    {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>The languages built into the DLL: code -> texts.</summary>
    static Dictionary<string, Dictionary<string, string>> Shipped()
    {
        var shipped = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        var asm = Assembly.GetExecutingAssembly();
        foreach (var name in asm.GetManifestResourceNames().Where(n => n.EndsWith(".json") && n.Contains(".Lang.")))
        {
            using var stream = asm.GetManifestResourceStream(name)!;
            var texts = JsonSerializer.Deserialize<Dictionary<string, string>>(stream, LangJson) ?? new();
            string code = name[(name.IndexOf(".Lang.", StringComparison.Ordinal) + 6)..^5];
            shipped[code] = texts;
        }
        return shipped;
    }

    /// <summary>Reads every language file, writing out or completing the shipped ones first.</summary>
    static void LoadLanguages()
    {
        Languages.Clear();
        var shipped = Shipped();
        foreach (var (code, texts) in shipped) Languages[code] = new(texts);
        try
        {
            Directory.CreateDirectory(LangDir);
            foreach (var (code, defaults) in shipped)
            {
                string path = Path.Combine(LangDir, code + ".json");
                var file = File.Exists(path) ? Read(path) : new Dictionary<string, string>();
                bool complete = defaults.Keys.All(file.ContainsKey);
                foreach (var (key, text) in file) Languages[code][key] = text;
                if (!complete) Write(path, code, defaults.Keys.Select(k => (k, Languages[code][k])).Concat(file.Where(f => !defaults.ContainsKey(f.Key)).Select(f => (f.Key, f.Value))));
            }
            foreach (var path in Directory.GetFiles(LangDir, "*.json"))
            {
                string code = Path.GetFileNameWithoutExtension(path);
                if (shipped.ContainsKey(code)) continue;
                Languages[code] = Read(path);
            }
        }
        catch (Exception ex)
        {
            Log($"language files: {ex.Message} - using the built-in texts");
        }
    }

    static Dictionary<string, string> Read(string path)
    {
        try { return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path), LangJson) ?? new(); }
        catch (Exception ex)
        {
            Log($"language file {Path.GetFileName(path)} could not be read: {ex.Message}");
            return new();
        }
    }

    static void Write(string path, string code, IEnumerable<(string Key, string Text)> texts)
    {
        var sb = new StringBuilder();
        sb.Append("// Ability Brawl texts, language \"").Append(code).Append("\". Edit freely; a text left out falls back to English.\n");
        sb.Append("// {0}, {1}, ... stand for what the plugin puts in (a number, a name): keep them, in any order.\n");
        sb.Append("// Save a copy under another language code (de.json, pt-br.json) to add a language; players pick it with /lang.\n");
        var ordered = new Dictionary<string, string>();
        foreach (var (key, text) in texts) ordered[key] = text;
        sb.Append(JsonSerializer.Serialize(ordered, LangJson)).Append('\n');
        File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
    }

    /// <summary>A text in the given language.</summary>
    static string Tl(string lang, string key, params object[] args)
    {
        string text = Languages.TryGetValue(lang, out var texts) && texts.TryGetValue(key, out var t) ? t
            : Languages.TryGetValue(FallbackLang, out var english) && english.TryGetValue(key, out var e) ? e
            : key;
        if (args is null || args.Length == 0) return text;
        try { return string.Format(CultureInfo.InvariantCulture, text, args); }
        catch (FormatException) { return text; }       // a translation with a broken placeholder still shows something
    }

    /// <summary>A text in the server's language, for messages everyone sees.</summary>
    static string T(string key, params object[] args) => Tl(_lang, key, args);

    /// <summary>The language a player reads: their /lang choice, else the server's.</summary>
    string LangOf(int slot) =>
        Players.FromSlot(slot) is { IsBot: false } c && _playerLang.TryGetValue(c.PlayerSteamId, out var lang) ? lang : _lang;

    /// <summary>A command's refusal, in the language of whoever typed it (the server's for the console).</summary>
    CommandException Fail(CCitadelPlayerController? caller, string key, params object[] args) =>
        new(Tl(caller != null ? LangOf(caller.Slot) : _lang, key, args));

    static string LangCode(string text) => text.Trim().ToLowerInvariant();

    [Command("lang", Description = "Your language for the plugin's texts: /lang <code>, /lang alone lists them")]
    public void CmdLang(CCitadelPlayerController caller, string lang = "")
    {
        string code = LangCode(lang);
        if (code.Length == 0 || !Languages.ContainsKey(code))
        {
            Chat.PrintToChat(caller, Tl(LangOf(caller.Slot), "lang.list", string.Join(", ", Languages.Keys.OrderBy(k => k))));
            return;
        }
        _playerLang[caller.PlayerSteamId] = code;
        Chat.PrintToChat(caller, Tl(code, "lang.set", code));
        if (_seats.TryGetValue(caller.Slot, out var s))
        {
            if (_phase == Phase.Drafting && !s.Done) ShowOffer(s);
            if (_phase == Phase.Voting) ShowVote(s);
        }
    }
}
