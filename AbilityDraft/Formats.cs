using DeadworksManaged.Api;

namespace AbilityDraft;

// How the kits come about. Draft: everyone picks four abilities. Chaos: a random hero and four random abilities.
// Shuffle: your own hero, four random abilities, and an ability is swapped for another random one after each use.
// The lobby votes for the format together with the rules; /chaos is a shortcut that fixes the format in advance.
public sealed partial class DraftPlugin
{
    enum Format { None, Draft, Chaos, Shuffle }

    Format _format = Format.None;
    Format _forcedFormat;               // set by /chaos (and the test bridge): the vote is then about the rules only

    static string FormatName(Format f, bool ru) => f switch
    {
        Format.Chaos => ru ? "Хаос" : "Chaos",
        Format.Shuffle => ru ? "Перетасовка" : "Shuffle",
        _ => ru ? "Драфт" : "Draft",
    };

    static string FormatHint(Format f, bool ru) => f switch
    {
        Format.Chaos => ru ? "случайные герои и способности" : "random heroes and abilities",
        Format.Shuffle => ru ? "способность меняется после каждого применения" : "an ability changes after each use",
        _ => ru ? "каждый выбирает 4 способности" : "everyone picks 4 abilities",
    };

    void CastFormat(int slot, Format f)
    {
        if (_phase != Phase.Voting || _forcedFormat != Format.None || f == Format.None || !_seats.TryGetValue(slot, out var s)) return;
        s.FormatVote = f;
        Log($"slot {slot} votes for format {f}");
        var name = Players.FromSlot(slot)?.PlayerName ?? $"#{slot}";
        Chat.PrintToChatAll($"[Draft] {name}: {FormatName(f, _ru)}");
        RefreshVote();
    }

    [Command("format", Description = "Vote for the format: /format draft | chaos | shuffle")]
    public void CmdFormat(CCitadelPlayerController caller, string format)
    {
        if (_phase != Phase.Voting) throw new CommandException("There is no vote right now.");
        if (_forcedFormat != Format.None) throw new CommandException("The format is already set for this match.");
        CastFormat(caller.Slot, ParseFormat(format) is var f && f != Format.None ? f : throw new CommandException("Use /format draft, /format chaos or /format shuffle."));
    }

    static Format ParseFormat(string text) => text.Trim().ToLowerInvariant() switch
    {
        "3" or "d" or "draft" or "драфт" => Format.Draft,
        "4" or "c" or "chaos" or "random" or "хаос" or "рандом" => Format.Chaos,
        "5" or "s" or "shuffle" or "swap" or "перетасовка" => Format.Shuffle,
        _ => Format.None,
    };

    /// <summary>The format most humans voted for; a tie goes to the lobby leader's choice, then to the plainer format.</summary>
    Format TallyFormat(List<Seat> humans)
    {
        var votes = humans.Where(s => s.FormatVote != Format.None).GroupBy(s => s.FormatVote).ToDictionary(g => g.Key, g => g.Count());
        if (votes.Count == 0) return Format.Draft;
        int best = votes.Values.Max();
        var top = votes.Where(v => v.Value == best).Select(v => v.Key).OrderBy(f => f).ToList();
        if (top.Count > 1 && _seats.TryGetValue(Host(), out var h) && top.Contains(h.FormatVote)) return h.FormatVote;
        return top[0];
    }

    // ---- shuffle ---------------------------------------------------------------------------------------------
    /// <summary>Everyone keeps their hero and gets four random abilities: no two players the same, while the pool lasts.</summary>
    void AssignShuffleKits()
    {
        var normals = Pool.Where(a => !a.Ult).ToList();
        var ults = Pool.Where(a => a.Ult).ToList();
        Shuffle(normals);
        Shuffle(ults);
        _kits.Clear();
        _taken.Clear();
        RestoreHeroTables();
        int n = 0, u = 0;
        foreach (var seat in _seats.Values)
        {
            if (Players.FromSlot(seat.Slot) is not { } c || c.GetHeroPawn() is not { } pawn) continue;
            var names = new string[Slots];
            for (int i = 0; i < Slots - 1; i++) names[i] = normals[n++ % normals.Count].Name;
            names[Slots - 1] = ults[u++ % ults.Count].Name;
            names.CopyTo(seat.Kit, 0);
            seat.Round = Slots;
            _kits[seat.Slot] = names;
            BindHeroKit(pawn.HeroID, seat.Slot, names);
            ApplyKit(pawn, names);
            if (!seat.Bot)
                Chat.PrintToChat(c, "[Draft] " + (seat.Ru ? "Твой набор: " : "Your kit: ") + string.Join(", ", names.Select(k => AbilityPool.Find(k)?.Title(seat.Ru) ?? k)));
            Log($"shuffle slot {seat.Slot} ({c.PlayerName}): {pawn.HeroID} [{string.Join(", ", names)}]");
        }
    }

    // What each ability slot looked like at the last look, to notice a use: a new cooldown, or a charge gone.
    readonly Dictionary<(int Player, int Slot), (float CooldownStart, int Charges)> _shuffleSeen = new();
    readonly Dictionary<(int Player, int Slot), float> _shuffleUsedAt = new();

    void TickShuffle()
    {
        if (_format != Format.Shuffle) return;
        foreach (var (player, kit) in _kits)
        {
            if (Players.FromSlot(player) is not { } c || c.GetHeroPawn() is not { IsAlive: true } pawn) continue;
            for (int i = 0; i < Slots; i++)
            {
                if (pawn.GetAbilityBySlot((EAbilitySlot)i) is not CCitadelBaseAbility ability || ability.AbilityName != kit[i]) continue;
                var key = (player, i);
                var now = (ability.CooldownStart, ability.RemainingCharges);
                if (!_shuffleSeen.TryGetValue(key, out var seen)) { _shuffleSeen[key] = now; continue; }
                bool used = ability.IsOnCooldown && now.CooldownStart > seen.CooldownStart + 0.05f || now.RemainingCharges < seen.Charges;
                _shuffleSeen[key] = now;
                if (used) _shuffleUsedAt.TryAdd(key, Now);
                // The swap waits for the cast to be over: a channel or a toggle cut off halfway would simply be lost.
                if (!_shuffleUsedAt.TryGetValue(key, out float usedAt) || Now < usedAt + _config.ShuffleSeconds || ability.IsChanneling || ability.ToggleState) continue;
                _shuffleUsedAt.Remove(key);
                SwapUsedAbility(c, pawn, kit, i, ability);
            }
        }
    }

    void SwapUsedAbility(CCitadelPlayerController c, CCitadelPlayerPawn pawn, string[] kit, int slot, CCitadelBaseAbility old)
    {
        bool ult = slot == Slots - 1;
        var pool = Pool.Where(a => a.Ult == ult && !kit.Contains(a.Name)).ToList();
        if (pool.Count == 0) return;
        var next = pool[Random.Shared.Next(pool.Count)];
        // The new ability takes over what is left of the old one's cooldown: a use still costs its wait.
        bool cooling = old.IsOnCooldown;
        float start = old.CooldownStart, end = old.CooldownEnd;
        string was = old.AbilityName;
        if (!ReplaceAbility(pawn, slot, next.Name)) { Log($"shuffle slot {c.Slot}: could not put {next.Name} into slot {slot + 1}"); return; }
        kit[slot] = next.Name;
        if (pawn.GetAbilityBySlot((EAbilitySlot)slot) is CCitadelBaseAbility fresh)
        {
            if (cooling) { fresh.CooldownStart = start; fresh.CooldownEnd = end; }
            _shuffleSeen[(c.Slot, slot)] = (fresh.CooldownStart, fresh.RemainingCharges);
        }
        Log($"shuffle slot {c.Slot}: {was} -> {next.Name} (slot {slot + 1})");
        if (c.IsBot) return;
        bool ru = _seats.TryGetValue(c.Slot, out var seat) ? seat.Ru : _ru;
        Chat.PrintToChat(c, $"[Draft] {slot + 1}: {next.Title(ru)}");
    }
}
