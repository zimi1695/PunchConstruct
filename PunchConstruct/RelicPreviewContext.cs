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

