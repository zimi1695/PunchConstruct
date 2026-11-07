using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct.NativeBridge;

// Combat and card-lifecycle hooks. Native calculations, RNG and state history
// stay in place; the adapter changes only this character's own branch.
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.GainBlock), [typeof(Creature), typeof(decimal), typeof(ValueProp), typeof(CardPlay), typeof(bool)])]
[PatchPolicy(true)]
internal static class BridgeUnarmBlock
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(Creature creature, decimal amount, ValueProp props, CardPlay? cardPlay, ref Task<decimal> __result)
    {
        var result = __result;
        var runNative = AdaptationRegistry.Guard("combat", "UnarmBlock",
            () => UnarmBlockReplacement.Apply(creature, amount, props, cardPlay, ref result));
        __result = result;
        return runNative;
    }
}

[HarmonyPatch(typeof(VigorPower), nameof(VigorPower.AfterAttack))]
internal static class BridgeGuardVigorConsumption
{
    private static void Postfix(VigorPower __instance, AttackCommand command, ref Task __result)
    {
        var result = __result;
        AdaptationRegistry.Run("combat", "GuardVigorConsumption",
            () => GuardVigorConsumption.Apply(__instance, command, ref result));
        __result = result;
    }
}

[HarmonyPatch]
internal static class BridgeGuardStancePowerGain
{
    private static IEnumerable<MethodBase> TargetMethods() => GuardStancePowerGain.Targets();

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var original = instructions.ToList();
        return AdaptationRegistry.Run("combat", "GuardStancePowerGain", original,
            () => GuardStancePowerGain.Transpile(original, __originalMethod));
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.EnergyCost), MethodType.Getter)]
internal static class BridgeRestoreSoupFreeCost
{
    private static void Postfix(CardModel __instance, CardEnergyCost __result) =>
        AdaptationRegistry.Run("combat", "RestoreSoupFreeCost", () => RestoreSoupFreeCost.Apply(__instance, __result));
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Keywords), MethodType.Getter)]
internal static class BridgeRestoreSoupEternal
{
    private static void Postfix(CardModel __instance, ref IReadOnlySet<CardKeyword> __result)
    {
        var keywords = __result;
        AdaptationRegistry.Run("combat", "RestoreSoupEternal", () => RestoreSoupEternal.Apply(__instance, ref keywords));
        __result = keywords;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.FromSerializable))]
internal static class BridgeObserverSaveMigration
{
    private static void Prefix(SerializableCard __0) =>
        AdaptationRegistry.Run("combat", "ObserverSaveMigration", () => ObserverSaveMigration.Apply(__0));
}

[HarmonyPatch(typeof(SicEm), "OnPlay")]
internal static class BridgeConstructSicEm
{
    private static bool Prefix(SicEm __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
    {
        var result = __result;
        var runNative = AdaptationRegistry.Guard("combat", "ConstructSicEm",
            () => ConstructSicEm.Apply(__instance, choiceContext, cardPlay, ref result));
        __result = result;
        return runNative;
    }
}

[HarmonyPatch(typeof(OstyDamageVar), nameof(OstyDamageVar.UpdateCardPreview))]
internal static class BridgeConstructSicEmPreview
{
    private static bool Prefix(OstyDamageVar __instance, CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks) =>
        AdaptationRegistry.Guard("combat", "ConstructSicEmPreview",
            () => ConstructSicEmPreview.Apply(__instance, card, previewMode, target, runGlobalHooks));
}

[HarmonyPatch(typeof(SicEm), "ShouldGlowRedInternal", MethodType.Getter)]
internal static class BridgeConstructSicEmWarning
{
    private static bool Prefix(SicEm __instance, ref bool __result)
    {
        var warning = __result;
        var runNative = AdaptationRegistry.Guard("combat", "ConstructSicEmWarning",
            () => ConstructSicEmWarning.Apply(__instance, ref warning));
        __result = warning;
        return runNative;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Tags), MethodType.Getter)]
internal static class BridgeConstructSicEmTags
{
    private static void Postfix(CardModel __instance, ref IEnumerable<CardTag> __result)
    {
        var tags = __result;
        AdaptationRegistry.Run("combat", "ConstructSicEmTags", () => ConstructSicEmTags.Apply(__instance, ref tags));
        __result = tags;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Description), MethodType.Getter)]
internal static class BridgeConstructSicEmDescription
{
    private static void Postfix(CardModel __instance, ref LocString __result)
    {
        var text = __result;
        AdaptationRegistry.Run("combat", "ConstructSicEmDescription", () => ConstructSicEmDescription.Apply(__instance, ref text));
        __result = text;
    }
}

[HarmonyPatch]
internal static class BridgeConstructOstyAttack
{
    private static IEnumerable<MethodBase> TargetMethods() => ConstructOstyHealthAttack.Targets();

    private static bool Prefix(CardModel __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
    {
        var result = __result;
        var runNative = AdaptationRegistry.Guard("combat", "ConstructOstyAttack",
            () => ConstructOstyHealthAttack.Apply(__instance, choiceContext, cardPlay, ref result));
        __result = result;
        return runNative;
    }
}

[HarmonyPatch(typeof(CalculatedDamageVar), nameof(CalculatedDamageVar.UpdateCardPreview))]
internal static class BridgeConstructOstyPreview
{
    private static bool Prefix(CalculatedDamageVar __instance, CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks) =>
        AdaptationRegistry.Guard("combat", "ConstructOstyPreview",
            () => ConstructOstyHealthPreview.Apply(__instance, card, previewMode, target, runGlobalHooks));
}

[HarmonyPatch]
internal static class BridgeConstructOstyWarning
{
    private static IEnumerable<MethodBase> TargetMethods() => ConstructOstyHealthWarning.Targets();

    private static bool Prefix(CardModel __instance, ref bool __result)
    {
        var warning = __result;
        var runNative = AdaptationRegistry.Guard("combat", "ConstructOstyWarning",
            () => ConstructOstyHealthWarning.Apply(__instance, ref warning));
        __result = warning;
        return runNative;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Tags), MethodType.Getter)]
internal static class BridgeConstructOstyTags
{
    private static void Postfix(CardModel __instance, ref IEnumerable<CardTag> __result)
    {
        var tags = __result;
        AdaptationRegistry.Run("combat", "ConstructOstyTags", () => ConstructOstyHealthTags.Apply(__instance, ref tags));
        __result = tags;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Description), MethodType.Getter)]
internal static class BridgeConstructOstyDescription
{
    private static void Postfix(CardModel __instance, ref LocString __result)
    {
        var text = __result;
        AdaptationRegistry.Run("combat", "ConstructOstyDescription",
            () => ConstructOstyHealthDescription.Apply(__instance, ref text));
        __result = text;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Owner), MethodType.Setter)]
internal static class BridgeAbrasiveOwner
{
    private static void Postfix(CardModel __instance) =>
        AdaptationRegistry.Run("combat", "AbrasiveOwner", () => AbrasiveOwnerAdaptation.Apply(__instance));
}

[HarmonyPatch(typeof(CardModel), "AfterDowngraded")]
internal static class BridgeAbrasiveDowngrade
{
    private static void Postfix(CardModel __instance) =>
        AdaptationRegistry.Run("combat", "AbrasiveDowngrade", () => AbrasiveDowngradeAdaptation.Apply(__instance));
}
