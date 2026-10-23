using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using DeadworksManaged.Api;

namespace AbilityDraft;

/// <summary>
/// Two server.dll functions the stock Street Brawl draft uses, called directly so the server can drive the native
/// draft screen: Advance uses up a draft pick (and closes the screen after the last one), Reroll deals three new options.
/// They are located by byte signature; tools/find_sigs.py regenerates the signatures after a game patch.
/// </summary>
static unsafe class Native
{
    static delegate* unmanaged<IntPtr, void> _advance, _reroll;
    // CCitadelPlayerPawn::TrainOrUpgradeAbility(pawn, ability): what ALT+number ends in. Optional - without it only
    // the click in the upgrade menu is lost.
    static delegate* unmanaged<IntPtr, IntPtr, void> _train;

    public static bool Ready => _advance != null && _reroll != null;
    public static bool CanTrain => _train != null;

    /// <summary>
    /// The signatures come from AbilityDraft.signatures.json next to the plugin when it is there and matches the game
    /// (a server owner drops in a new one after a patch), else from the copy built into the DLL - the one a host that
    /// only takes the DLL, like the Deadworks launcher, gets.
    /// </summary>
    public static string Load(string signatureFile)
    {
        var sources = new List<(string From, string Json)>();
        if (File.Exists(signatureFile)) sources.Add((Path.GetFileName(signatureFile), File.ReadAllText(signatureFile)));
        using (var builtIn = typeof(Native).Assembly.GetManifestResourceStream("AbilityDraft.signatures.json"))
            if (builtIn != null) sources.Add(("the built-in signatures", new StreamReader(builtIn).ReadToEnd()));
        if (sources.Count == 0) return $"{signatureFile} is missing - run tools/find_sigs.py";
        var said = new List<string>();
        foreach (var (from, json) in sources)
        {
            said.Add($"{from}: {Load(JsonSerializer.Deserialize<Dictionary<string, string>>(json) ?? new())}");
            if (Ready) break;
        }
        return string.Join("; ", said);
    }

    static string Load(Dictionary<string, string> sigs)
    {
        _advance = _reroll = null;
        _train = null;
        ProcessModule? server = null;
        foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
            if (m.ModuleName.Equals("server.dll", StringComparison.OrdinalIgnoreCase)) server = m;
        if (server == null) return "server.dll is not loaded";

        var image = new ReadOnlySpan<byte>((void*)server.BaseAddress, server.ModuleMemorySize);
        var advance = Find(image, sigs.GetValueOrDefault("ItemDraftAdvance"));
        var reroll = Find(image, sigs.GetValueOrDefault("ItemDraftReroll"));
        if (advance < 0 || reroll < 0)
            return $"signature not found (advance={advance >= 0}, reroll={reroll >= 0}) - the game was patched, run tools/find_sigs.py";
        _advance = (delegate* unmanaged<IntPtr, void>)(server.BaseAddress + advance);
        _reroll = (delegate* unmanaged<IntPtr, void>)(server.BaseAddress + reroll);
        var train = Find(image, sigs.GetValueOrDefault("TrainAbility"));
        if (train >= 0) _train = (delegate* unmanaged<IntPtr, IntPtr, void>)(server.BaseAddress + train);
        return $"native draft functions resolved: advance=server.dll+0x{advance:x} reroll=server.dll+0x{reroll:x} train={(train >= 0 ? $"server.dll+0x{train:x}" : "not found")}";
    }

    /// <summary>Offset of the only match of a "48 8B ? ?" style pattern, or -1 when it is missing or ambiguous.</summary>
    static int Find(ReadOnlySpan<byte> image, string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern)) return -1;
        var parts = pattern.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var bytes = parts.Select(p => p == "?" ? (short)-1 : Convert.ToInt16(p, 16)).ToArray();
        byte first = (byte)bytes[0];
        int found = -1;
        for (int i = 0; i <= image.Length - bytes.Length; i++)
        {
            if (image[i] != first) continue;
            int j = 1;
            while (j < bytes.Length && (bytes[j] < 0 || image[i + j] == bytes[j])) j++;
            if (j < bytes.Length) continue;
            if (found >= 0) return -1;
            found = i;
        }
        return found;
    }

    /// <summary>Uses up one of the hero's draft picks: deals the next one, or ends the draft and closes the client's screen.</summary>
    public static void Advance(CCitadelPlayerPawn pawn) => _advance(pawn.Handle);
    public static void Reroll(CCitadelPlayerPawn pawn) => _reroll(pawn.Handle);
    /// <summary>Unlocks or upgrades the ability the stock way: it checks and spends the points itself.</summary>
    public static void Train(CCitadelPlayerPawn pawn, CCitadelBaseAbility ability) => _train(pawn.Handle, ability.Handle);
}
