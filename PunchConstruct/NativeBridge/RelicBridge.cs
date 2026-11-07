using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
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
[PatchPolicy(true)]
internal static class BridgeConstructCapsuleStarters
{
    private static IEnumerable<MethodBase> TargetMethods() => ConstructCapsuleStarters.Targets();

    [HarmonyPriority(Priority.First)]
    private static bool Prefix(CharacterModel character, ref CardModel __result)
    {
        var card = __result;
        var runNative = AdaptationRegistry.Guard("relic", "ConstructCapsuleStarters",
            () => ConstructCapsuleStarters.Apply(character, ref card));
        __result = card;
        return runNative;
    }
}

[HarmonyPatch(typeof(SeaGlass), nameof(SeaGlass.AfterObtained))]
internal static class BridgeConstructSeaGlass
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(SeaGlass __instance, ref Task __result)
    {
        var result = __result;
        var runNative = AdaptationRegistry.Guard("relic", "ConstructSeaGlass",
            () => ConstructSeaGlass.Apply(__instance, ref result));
        __result = result;
        return runNative;
    }
}

[HarmonyPatch(typeof(SeaGlass), nameof(SeaGlass.Title), MethodType.Getter)]
internal static class BridgeConstructSeaGlassTitle
{
    private static void Postfix(SeaGlass __instance, ref LocString __result)
    {
        var title = __result;
        AdaptationRegistry.Run("relic", "ConstructSeaGlassTitle",
            () => ConstructSeaGlassTitle.Apply(__instance, ref title));
        __result = title;
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

[HarmonyPatch(typeof(NeowsTalisman), nameof(NeowsTalisman.AfterObtained))]
internal static class BridgeConstructNeowsTalisman
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(NeowsTalisman __instance, ref Task __result)
    {
        var result = __result;
        var runNative = AdaptationRegistry.Guard("relic", "ConstructNeowsTalisman",
            () => ConstructNeowsTalisman.Apply(__instance, ref result));
        __result = result;
        return runNative;
    }
}

[HarmonyPatch]
internal static class BridgeWaveDummy
{
    private static IEnumerable<MethodBase> TargetMethods() => WaveDummyPatch.Targets();

    private static bool Prefix(RelicModel __instance, Creature? dealer, ValueProp props, CardModel? cardSource, ref decimal __result)
    {
        var value = __result;
        var runNative = AdaptationRegistry.Guard("relic", "WaveDummy",
            () => WaveDummyPatch.Apply(__instance, dealer, props, cardSource, ref value));
        __result = value;
        return runNative;
    }
}

[HarmonyPatch(typeof(GhostSeed), "CanAffect")]
internal static class BridgeGhostSeedWave
{
    private static void Postfix(CardModel card, ref bool __result)
    {
        var affected = __result;
        AdaptationRegistry.Run("relic", "GhostSeedWave", () => GhostSeedWavePatch.Apply(card, ref affected));
        __result = affected;
    }
}

[HarmonyPatch(typeof(RippleBasin), nameof(RippleBasin.BeforeSideTurnEnd))]
internal static class BridgeAttackingBasin
{
    private static bool Prefix(RippleBasin __instance, IEnumerable<Creature> participants, ref Task __result)
    {
        var result = __result;
        var runNative = AdaptationRegistry.Guard("relic", "AttackingBasin",
            () => AttackingBasinPatch.Apply(__instance, participants, ref result));
        __result = result;
        return runNative;
    }
}

[HarmonyPatch(typeof(RippleBasin), nameof(RippleBasin.AfterCardPlayed))]
internal static class BridgeBasinStatus
{
    private static void Postfix(RippleBasin __instance, CardPlay cardPlay) =>
        AdaptationRegistry.Run("relic", "BasinStatus", () => BasinStatusPatch.Apply(__instance, cardPlay));
}

[HarmonyPatch(typeof(NutritiousSoup), nameof(NutritiousSoup.AfterObtained))]
internal static class BridgeSoupWave
{
    private static bool Prefix(NutritiousSoup __instance, ref Task __result)
    {
        var result = __result;
        var runNative = AdaptationRegistry.Guard("relic", "SoupWave",
            () => SoupWavePatch.Apply(__instance, ref result));
        __result = result;
        return runNative;
    }
}

[HarmonyPatch(typeof(NutritiousSoup), "ExtraHoverTips", MethodType.Getter)]
internal static class BridgeSoupWaveHoverTips
{
    private static void Postfix(NutritiousSoup __instance, ref IEnumerable<IHoverTip> __result)
    {
        var tips = __result;
        AdaptationRegistry.Run("relic", "SoupWaveHoverTips", () => SoupWaveHoverTips.Apply(__instance, ref tips));
        __result = tips;
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

[HarmonyPatch(typeof(LeafyPoultice), nameof(LeafyPoultice.AfterObtained))]
internal static class BridgeLeafyPoultice
{
    private static bool Prefix(LeafyPoultice __instance, ref Task __result)
    {
        var result = __result;
        var runNative = AdaptationRegistry.Guard("relic", "LeafyPoultice",
            () => LeafyPoulticeWaves.Apply(__instance, ref result));
        __result = result;
        return runNative;
    }
}

[HarmonyPatch(typeof(RuinedHelmet), nameof(RuinedHelmet.TryModifyPowerAmountReceived))]
internal static class BridgeNativeHelmetDisableDouble
{
    private static bool Prefix(RuinedHelmet __instance, ref bool __result)
    {
        var modified = __result;
        var runNative = AdaptationRegistry.Guard("relic", "NativeHelmetDisableDouble",
            () => NativeHelmetDisableDouble.Apply(__instance, ref modified));
        __result = modified;
        return runNative;
    }
}

[HarmonyPatch(typeof(AbstractModel), nameof(AbstractModel.AfterPowerAmountChanged))]
internal static class BridgeNativeHelmetMirror
{
    private static void Postfix(AbstractModel __instance, PlayerChoiceContext choiceContext, PowerModel power, decimal amount, ref Task __result)
    {
        var result = __result;
        AdaptationRegistry.Run("relic", "NativeHelmetMirror",
            () => NativeHelmetMirrorHook.Apply(__instance, choiceContext, power, amount, ref result));
        __result = result;
    }
}

[HarmonyPatch(typeof(RuinedHelmet), nameof(RuinedHelmet.AfterCombatEnd))]
internal static class BridgeNativeHelmetReset
{
    private static void Postfix(RuinedHelmet __instance) =>
        AdaptationRegistry.Run("relic", "NativeHelmetReset", () => NativeHelmetReset.Apply(__instance));
}

[HarmonyPatch(typeof(HelicalDart), nameof(HelicalDart.AfterCardPlayed))]
internal static class BridgeNativeDart
{
    private static bool Prefix(HelicalDart __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
    {
        var result = __result;
        var runNative = AdaptationRegistry.Guard("relic", "NativeDart",
            () => NativeDartAdaptation.Apply(__instance, choiceContext, cardPlay, ref result));
        __result = result;
        return runNative;
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
