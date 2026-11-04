using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;

namespace PunchConstruct;

// A reward/shop relic has no actual owner until acquired. Keep its display context
// separately, so browsing its icon and description does not grant or mutate it.
internal static class RelicPreviewContext
{
    private static readonly ConditionalWeakTable<RelicModel, Player> Owners = new();
    internal static Player? Owner(RelicModel relic) => Owners.TryGetValue(relic, out var player) ? player : null;
    internal static void Register(RelicModel? relic, Player player)
    {
        if (relic == null) return;
        relic.AssertMutable();
        Owners.Remove(relic);
        Owners.Add(relic, player);
    }
}

[HarmonyPatch(typeof(MerchantRelicEntry), "SetModel")]
internal static class MerchantRelicPreview
{
    private static readonly AccessTools.FieldRef<MerchantEntry, Player> EntryPlayer = AccessTools.FieldRefAccess<MerchantEntry, Player>("_player");
    private static void Postfix(MerchantRelicEntry __instance) => RelicPreviewContext.Register(__instance.Model, EntryPlayer(__instance));
}

[HarmonyPatch(typeof(RelicReward), nameof(RelicReward.Populate))]
internal static class RewardRelicPreview
{
    private static void Postfix(RelicReward __instance) => RelicPreviewContext.Register(__instance.Relic, __instance.Player);
}
