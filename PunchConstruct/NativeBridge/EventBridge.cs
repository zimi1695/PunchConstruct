using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;

namespace PunchConstruct.NativeBridge;

// Event and ancient hooks. Native option/dialogue generation stays authoritative;
// this bridge only forwards this character's scoped replacement.
[HarmonyPatch]
internal static class BridgeScopedAncientTooth
{
    private static IEnumerable<MethodBase> TargetMethods() => ScopedAncientTooth.Targets();

    private static void Prefix(Player player, out bool __state)
    {
        bool state = false;
        AdaptationRegistry.Run("event", "ScopedAncientTooth", () => ScopedAncientTooth.Apply(player, out state));
        __state = state;
    }

    private static void Finalizer(bool __state) => ScopedAncientTooth.Restore(__state);
}

[HarmonyPatch(typeof(ArchaicTooth), "GetTranscendenceTransformedCard")]
internal static class BridgeScopedAncientToothTransformation
{
    private static bool Prefix(ArchaicTooth __instance, CardModel __0, ref CardModel __result)
    {
        var result = __result;
        var runNative = AdaptationRegistry.Guard("event", "ScopedAncientToothTransformation",
            () => ScopedAncientToothTransformation.Apply(__instance, __0, ref result));
        __result = result;
        return runNative;
    }
}

[HarmonyPatch(typeof(ArchaicTooth), "TranscendenceUpgrades", MethodType.Getter)]
internal static class BridgeAncientWaveMapping
{
    private static void Postfix(ref Dictionary<ModelId, CardModel> __result)
    {
        var mapping = __result;
        AdaptationRegistry.Run("event", "AncientWaveMapping", () => AncientWaveMapping.Apply(ref mapping));
        __result = mapping;
    }
}

[HarmonyPatch]
internal static class BridgePunchOffText
{
    private static IEnumerable<MethodBase> TargetMethods() => ConstructPunchOffText.Targets();

    private static void Postfix(EventModel __instance, ref LocString __result)
    {
        var text = __result;
        AdaptationRegistry.Run("event", "PunchOffText", () => ConstructPunchOffText.Apply(__instance, ref text));
        __result = text;
    }
}

[HarmonyPatch]
internal static class BridgeAncientDialogues
{
    private static IEnumerable<MethodBase> TargetMethods() => ConstructAncientDialogues.Targets();

    private static void Postfix(EventModel __instance, AncientDialogueSet __result) =>
        AdaptationRegistry.Run("event", "AncientDialogues", () => ConstructAncientDialogues.Apply(__instance, __result));
}

[HarmonyPatch(typeof(AncientDialogueSet), nameof(AncientDialogueSet.GetValidDialogues))]
internal static class BridgeAncientDialogueSelection
{
    private static void Postfix(AncientDialogueSet __instance, ModelId __0, ref IEnumerable<AncientDialogue> __result)
    {
        var dialogues = __result;
        AdaptationRegistry.Run("event", "AncientDialogueSelection",
            () => ConstructAncientDialogueSelection.Apply(__instance, __0, ref dialogues));
        __result = dialogues;
    }
}

[HarmonyPatch(typeof(Bugslayer), "GenerateInitialOptions")]
internal static class BridgeConstructBugslayer
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Bugslayer __instance, ref IReadOnlyList<EventOption> __result)
    {
        var options = __result;
        AdaptationRegistry.Run("event", "ConstructBugslayer", () => ConstructBugslayer.Apply(__instance, ref options));
        __result = options;
    }
}

[HarmonyPatch(typeof(Spiral), nameof(Spiral.CanEnchant))]
internal static class BridgeSmallWaveSpiral
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(CardModel __0, ref bool __result)
    {
        var canEnchant = __result;
        AdaptationRegistry.Run("event", "SmallWaveSpiral", () => SmallWaveSpiral.Apply(__0, ref canEnchant));
        __result = canEnchant;
    }
}

[HarmonyPatch(typeof(Amalgamator), "GenerateInitialOptions")]
internal static class BridgeConstructAmalgamation
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(Amalgamator __instance, ref IReadOnlyList<EventOption> __result)
    {
        var options = __result;
        AdaptationRegistry.Run("event", "ConstructAmalgamation", () => ConstructAmalgamation.Apply(__instance, ref options));
        __result = options;
    }
}

[HarmonyPatch]
internal static class BridgeConstructFusionReward
{
    private static MethodBase TargetMethod() => ConstructFusionReward.Target();

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = instructions.ToList();
        return AdaptationRegistry.Run("event", "ConstructFusionReward", original,
            () => ConstructFusionReward.Transpile(original));
    }
}

[HarmonyPatch]
internal static class BridgeConstructAmalgamationEligibility
{
    private static MethodBase TargetMethod() => ConstructAmalgamationEligibility.Target();

    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Player __0, ref bool __result)
    {
        var eligible = __result;
        var runNative = AdaptationRegistry.Guard("event", "ConstructAmalgamationEligibility",
            () => ConstructAmalgamationEligibility.Apply(__0, ref eligible));
        __result = eligible;
        return runNative;
    }
}

[HarmonyPatch(typeof(Amalgamator), "IsValid")]
internal static class BridgeConstructFusionIngredients
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(CardTag __0, CardModel __1, ref bool __result)
    {
        var valid = __result;
        var runNative = AdaptationRegistry.Guard("event", "ConstructFusionIngredients",
            () => ConstructFusionIngredients.Apply(__0, __1, ref valid));
        __result = valid;
        return runNative;
    }
}
