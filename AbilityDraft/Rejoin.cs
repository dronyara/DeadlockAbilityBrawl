using System.Runtime.InteropServices;
using DeadworksManaged.Api;

namespace AbilityDraft;

// Players who drop out of a match and come back, and the item draft of the Shuffle format.
public sealed partial class DraftPlugin
{
    // ---- coming back ---------------------------------------------------------------------------------------------
    // A kit is kept by player slot and was forgotten the moment its player disconnected. Someone whose connection
    // dropped for half a minute came back to a hero the plugin no longer knew: in Shuffle their abilities stopped
    // changing, and a rebuilt hero would have got its original kit back.
    readonly Dictionary<int, ulong> _steamOfSlot = new();
    readonly Dictionary<ulong, (string[] Kit, int Slot)> _away = new();
    readonly HashSet<int> _back = new();

    // Mid-draft and mid-vote: the slot a player left from, to find their place again.
    readonly Dictionary<ulong, int> _awaySlot = new();

    void WentAway(int slot)
    {
        if (_steamOfSlot.TryGetValue(slot, out ulong steam))
        {
            if (_phase is Phase.Match or Phase.Voting or Phase.Starting) _awaySlot[steam] = slot;
            if (_phase == Phase.Match && _kits.TryGetValue(slot, out var kit))
            {
                _away[steam] = (kit, slot);
                Log($"slot {slot} left the match: the kit is kept for their return");
            }
        }
        _steamOfSlot.Remove(slot);
    }

    void ComeBack(CCitadelPlayerController c)
    {
        if (_awaySlot.Remove(c.PlayerSteamId, out int was))
        {
            // A draft in progress is kept by slot: move it if the player came back into another one.
            if (was != c.Slot && _native.Remove(was, out var drafting))
            {
                drafting.Slot = c.Slot;
                _native[c.Slot] = drafting;
                Log($"{c.PlayerName} is back mid-draft: seat moved from slot {was} to {c.Slot}, round {drafting.Round + 1}");
            }
            // The vote: a seat again, with nothing chosen.
            if (_phase == Phase.Voting && !_seats.ContainsKey(c.Slot))
            {
                _seats[c.Slot] = new Seat { Slot = c.Slot };
                Log($"{c.PlayerName} is back for the vote");
                RefreshVote();
            }
        }
        if (_phase != Phase.Match || !_away.Remove(c.PlayerSteamId, out var saved)) return;
        _kits[c.Slot] = saved.Kit;
        // The hero's own table is held for a slot; the returning player may have been given another one.
        foreach (var hero in _heroOwner.Where(o => o.Value == saved.Slot).Select(o => o.Key).ToList()) _heroOwner[hero] = c.Slot;
        _back.Add(c.Slot);
        Log($"{c.PlayerName} is back in slot {c.Slot} (was {saved.Slot}): kit [{string.Join(", ", saved.Kit)}]");
    }

    /// <summary>Puts the kit on a returning player's hero once that hero exists.</summary>
    void TickComeBack()
    {
        if (_back.Count == 0) return;
        foreach (int slot in _back.ToList())
        {
            if (Players.FromSlot(slot) is not { } c || !_kits.TryGetValue(slot, out var kit)) { _back.Remove(slot); continue; }
            if (c.GetHeroPawn() is not { } pawn || pawn.HeroID == 0) continue;
            _back.Remove(slot);
            BindHeroKit(pawn.HeroID, slot, kit);
            ApplyKit(pawn, kit);
        }
    }

    // ---- Shuffle under Street Brawl rules: no weapon items ------------------------------------------------------
    // With abilities that never stay, a pure weapon build is simply the best one. The item draft is the engine's:
    // while a deal holds a weapon item, the hero is given a reroll and it is spent, all within the frame of the deal,
    // so the client is only ever sent the last one.
    static readonly HashSet<uint> WeaponItemTokens = AbilityPool.WeaponItems.Select(n => MurmurHash2.HashLowerCase(n, TokenSeed)).ToHashSet();
    const int ItemDealTries = 30;
    readonly Dictionary<int, (uint A, uint B, uint C)> _itemDeals = new();      // the last deal looked at, by player slot

    void FilterItemDeals()
    {
        if (_format != Format.Shuffle || _rules != Rules.StreetBrawl || _phase != Phase.Match || !_config.ShuffleNoWeaponItems || !Native.Ready) return;
        foreach (var pawn in Players.GetAllPawns())
        {
            if (pawn.Controller is not { } c) continue;
            var state = DraftState.GetAddress(pawn.Handle);
            if (!ReadDeal(state, out var deal)) { _itemDeals.Remove(c.Slot); continue; }
            if (_itemDeals.TryGetValue(c.Slot, out var seen) && seen == deal) continue;
            int tries = 0;
            while (HasWeaponItem(deal) && tries < ItemDealTries)
            {
                tries++;
                pawn.SetCurrency(ECurrencyType.EItemDraftRerolls, pawn.GetCurrency(ECurrencyType.EItemDraftRerolls) + 1);
                Native.Reroll(pawn);
                if (!ReadDeal(state, out deal)) break;
            }
            _itemDeals[c.Slot] = deal;
            if (tries > 0) Log($"item deal for slot {c.Slot}: {tries} reroll(s) to get rid of weapon items{(HasWeaponItem(deal) ? " - one is still there" : "")}");
        }
    }

    static bool ReadDeal(IntPtr state, out (uint A, uint B, uint C) deal)
    {
        deal = default;
        var data = Marshal.ReadIntPtr(state, StateOptionData);
        if (data == IntPtr.Zero || Marshal.ReadInt32(state, StateOptionCount) < Offers) return false;
        deal = ((uint)Marshal.ReadInt32(data, OptionItemId), (uint)Marshal.ReadInt32(data, OptionSize + OptionItemId), (uint)Marshal.ReadInt32(data, 2 * OptionSize + OptionItemId));
        return true;
    }

    static bool HasWeaponItem((uint A, uint B, uint C) deal) =>
        WeaponItemTokens.Contains(deal.A) || WeaponItemTokens.Contains(deal.B) || WeaponItemTokens.Contains(deal.C);
}
