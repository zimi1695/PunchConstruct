using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

internal static class ConstructDingyRug
{
    // Trial balance value: affects only selection within the native rarity roll.
    internal const int ColorlessMultiplier = 5;
    internal static bool Apply(DingyRug __instance, Player player, CardCreationOptions options, ref CardCreationOptions __result)
    {
        if (!CharacterScope.IsConstruct(__instance)) return true;
        // Selected colorless cards are already in the character's reward pool.
        // Weight those cards at the final draw instead of adding the global pool.
        __result = options;
        return false;
    }
}
