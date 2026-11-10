using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct.NativeBridge;

// Relic acquisition and relic-lifecycle hooks. Pool fallbacks, weighting and
// native relic callbacks stay authoritative.
[HarmonyPatch]
internal static class BridgeRelicFactoryScope
{
    private static IEnumerable<MethodBase> TargetMethods() => RelicFactoryScope.Targets();

    private static void Prefix(Player player, out Player? __state)
    {
        Player? state = null;
        AdaptationRegistry.Run("relic", "RelicFactoryScope", () => RelicFactoryScope.Apply(player, out state));
        __state = state;
    }

    private static void Finalizer(Player? __state) => RelicFactoryScope.Restore(__state);
}

[HarmonyPatch]
internal static class BridgeConstructRelicRolls
{
    private static IEnumerable<MethodBase> TargetMethods() => ConstructRelicRolls.Targets();

    [HarmonyPriority(Priority.First)]
    private static bool Prefix(RelicGrabBag __instance, RelicRarity rarity, Func<RelicModel, bool> filter, IRunState runState,
        MethodBase __originalMethod, ref RelicModel? __result)
    {
        var relic = __result;
        var runNative = AdaptationRegistry.Guard("relic", "ConstructRelicRolls",
            () => ConstructRelicRolls.Apply(__instance, rarity, filter, runState, __originalMethod, ref relic));
        __result = relic;
        return runNative;
    }
}

[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
internal static class BridgeConstructTreasureRelics
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = instructions.ToList();
        return AdaptationRegistry.Run("relic", "ConstructTreasureRelics", original,
            () => ConstructTreasureRelics.Transpile(original));
    }
}

[HarmonyPatch(typeof(Neow), "GenerateInitialOptions")]
internal static class BridgeNeowScrollExclusion
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Neow __instance, ref IReadOnlyList<EventOption> __result)
    {
        var options = __result;
        AdaptationRegistry.Run("relic", "NeowScrollExclusion", () => NeowScrollExclusion.Apply(__instance, ref options));
        __result = options;
    }
}

[HarmonyPatch(typeof(FakeMerchant), "BeforeEventStarted")]
internal static class BridgeFakeMerchantExclusions
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = instructions.ToList();
        return AdaptationRegistry.Run("relic", "FakeMerchantExclusions", original,
            () => FakeMerchantExclusions.Transpile(original));
    }
}

[HarmonyPatch]
internal static class BridgeWindDollPriority
{
    private static IEnumerable<MethodBase> TargetMethods() => WindDollPriority.Targets();

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var original = instructions.ToList();
        return AdaptationRegistry.Run("relic", "WindDollPriority", original,
            () => WindDollPriority.Transpile(original, __originalMethod));
    }
}

[HarmonyPatch]
internal static class BridgeConstructSoupOffer
{
    private static MethodBase TargetMethod() => ConstructSoupOffer.Target();

    [HarmonyPriority(Priority.First)]
    private static bool Prefix(CardModel __0, ref bool __result)
    {
        var eligible = __result;
        var runNative = AdaptationRegistry.Guard("relic", "ConstructSoupOffer",
            () => ConstructSoupOffer.Apply(__0, ref eligible));
        __result = eligible;
        return runNative;
    }
}

// Normal acquisition swaps a construct player's shared relic for its variant.
// A scripted RelicCmd.Obtain<T> caller is left native because it casts the
// result back to the requested type.
[HarmonyPatch(typeof(RelicCmd), nameof(RelicCmd.Obtain), [typeof(RelicModel), typeof(Player), typeof(int)])]
[PatchPolicy(true)]
internal static class BridgeConstructRelicVariants
{
    [HarmonyPriority(Priority.First)]
    private static void Prefix(ref RelicModel relic, Player player)
    {
        var original = relic;
        var value = relic;
        AdaptationRegistry.Run("relic", "ConstructRelicVariants",
            () => AdaptedRelicVariants.Substitute(ref value, player));
        relic = value;
        if (ReferenceEquals(original, relic)) return;
        // The native bag entries are the shared relic; remove them so the roll
        // cannot offer it again while this player owns the variant.
        player.RelicGrabBag.Remove(original);
        player.RunState.SharedRelicGrabBag.Remove(original);
    }
}

// Saves and multiplayer syncs created before variants still deserialize the
// shared relic. Convert them silently while the inventory loads; AfterObtained
// must not run again.
[HarmonyPatch(typeof(Player), "PopulateRelics")]
internal static class BridgeConstructRelicMigration
{
    private static void Prefix(Player __instance, ref IEnumerable<RelicModel> relics)
    {
        var loaded = relics;
        AdaptationRegistry.Run("relic", "ConstructRelicMigration",
            () => AdaptedRelicVariants.Migrate(__instance, ref loaded));
        relics = loaded;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.IsTransformable), MethodType.Getter)]
internal static class BridgeSoupExplicitReplacement
{
    private static void Postfix(CardModel __instance, ref bool __result)
    {
        var transformable = __result;
        AdaptationRegistry.Run("relic", "SoupExplicitReplacement",
            () => SoupExplicitReplacement.Apply(__instance, ref transformable));
        __result = transformable;
    }
}

[HarmonyPatch]
internal static class BridgeAdaptedRelicDescriptions
{
    private static IEnumerable<MethodBase> TargetMethods() => AdaptedRelicDescriptions.Targets();

    private static void Postfix(RelicModel __instance, ref LocString __result)
    {
        var text = __result;
        AdaptationRegistry.Run("relic", "AdaptedRelicDescriptions",
            () => AdaptedRelicDescriptions.Apply(__instance, ref text));
        __result = text;
    }
}
