using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

[HarmonyPatch(typeof(PrismaticGem), nameof(PrismaticGem.ModifyCardRewardCreationOptions))]
internal static class ConstructPrismaticGem
{
    private static bool Prefix(PrismaticGem __instance, Player player, CardCreationOptions options, ref CardCreationOptions __result)
    {
        if (!CharacterScope.IsConstruct(__instance)) return true;
        __result = options;
        if (player != __instance.Owner || options.Flags.HasFlag(CardCreationFlags.NoModifyHooks) ||
            options.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications) ||
            !options.Flags.HasFlag(CardCreationFlags.IsCardReward) ||
            !(options.Source == CardCreationSource.Encounter || options.Flags.HasFlag(CardCreationFlags.IsFromCombat))) return false;
        var previous = options.CardPoolFilter;
        // Keep the input reusable and retain existing reward filters. The native
        // energy bonus is unchanged; this replaces only this role's pool effect.
        __result = (options with { }).WithFilter(card => !card.Pool.IsColorless && (previous?.Invoke(card) ?? true));
        return false;
    }
}
