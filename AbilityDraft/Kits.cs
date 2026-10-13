using DeadworksManaged.Api;

namespace AbilityDraft;

// What an ability drags along, and what has to follow it when it leaves a slot.
public sealed partial class DraftPlugin
{
    // ---- dependent abilities ------------------------------------------------------------------------------------
    // An ability may come with others: its second stage ("..._trigger"), an alternate fire, a weapon. The engine
    // creates them together with the ability, but taking the ability away by hand leaves them on the hero - after a
    // few swaps a hero carried a pile of stale second stages and somebody else's alternate fire.

    /// <summary>Removes what a just-removed ability brought with it, unless something the hero still has needs it.</summary>
    void RemoveDependents(CCitadelPlayerPawn pawn, string removed)
    {
        if (!AbilityPool.Dependents.TryGetValue(removed, out var deps)) return;
        var held = pawn.AbilityComponent.Abilities.Select(a => a.AbilityName).ToHashSet();
        foreach (var dep in deps)
        {
            if (!held.Contains(dep)) continue;
            if (held.Any(h => h != removed && AbilityPool.Dependents.TryGetValue(h, out var theirs) && theirs.Contains(dep))) continue;
            if (pawn.AbilityComponent.FindAbilityByName(dep) is CCitadelBaseAbility stale && !pawn.RemoveAbility(stale))
                Log($"  could not remove {dep}, left behind by {removed}");
        }
    }

    // ---- companions ---------------------------------------------------------------------------------------------
    // Solomon moves his pieces with a separate ability on alternate fire, and the game data hangs that ability on his
    // ultimate alone. A hero with his bishop, knight or rook but without the queen could put a piece down and never
    // move it. (Sinclair copying the ultimate gets it, which is why it works there.)
    const string ChessMove = "ability_chessmaster_movechesspiece";
    const ushort ChessMoveSlot = 20;            // where the engine puts it for Solomon himself: the alternate fire
    static readonly string[] ChessPieces = ["ability_chessmaster_throwbishop", "ability_chessmaster_knightbuff", "ability_chessmaster_forkpiece"];

    void EnsureCompanions(CCitadelPlayerPawn pawn)
    {
        var held = pawn.AbilityComponent.Abilities.Select(a => a.AbilityName).ToHashSet();
        bool wanted = ChessPieces.Any(held.Contains);
        bool has = held.Contains(ChessMove);
        if (wanted && !has)
        {
            bool ok = pawn.AddAbility(ChessMove, ChessMoveSlot) != null;
            Log($"  {ChessMove} added for slot {pawn.Controller?.Slot}: {ok}");
        }
        else if (!wanted && has && !held.Any(h => AbilityPool.Dependents.TryGetValue(h, out var deps) && deps.Contains(ChessMove))
            && pawn.AbilityComponent.FindAbilityByName(ChessMove) is CCitadelBaseAbility stale)
            pawn.RemoveAbility(stale);
    }

    // ---- items attached to an ability -----------------------------------------------------------------------------
    // It is the item that remembers which ability it is attached to, by name. When that ability leaves its slot the
    // name points at nothing and the item does nothing, so the item is attached again to whatever took the slot.

    /// <summary>The hero's items that are attached to this ability.</summary>
    static List<CCitadelBaseAbility> AttachedItems(CCitadelPlayerPawn pawn, CCitadelBaseAbility ability)
    {
        string name = ability.AbilityName;
        return pawn.AbilityComponent.Abilities.Where(i => i.IsItem && i.IsImbued && i.ImbuedAbilities.Contains(name)).ToList();
    }

    void ReattachItems(CCitadelPlayerPawn pawn, int slot, List<CCitadelBaseAbility> items)
    {
        foreach (var item in items)
        {
            if (!item.IsValid) continue;
            var result = pawn.ImbueItem(item, (EAbilitySlot)slot);
            Log($"  {item.AbilityName} moved to the new ability in slot {slot + 1}: {result}");
        }
    }
}
