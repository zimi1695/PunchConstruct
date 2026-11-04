using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace PunchConstruct;

[HarmonyPatch]
internal static class ConstructOstyHealthAttack
{
    internal static bool Applies(CardModel card) => card is Unleash or Protector && RegentAdaptations.Owns(card);
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
        new[] { typeof(Unleash), typeof(Protector) }.Select(t => AccessTools.Method(t, "OnPlay"));
    private static bool Prefix(CardModel __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
    {
        if (!Applies(__instance)) return true;
        __result = Play(__instance, choiceContext, cardPlay);
        return false;
    }
    private static async Task Play(CardModel card, PlayerChoiceContext context, CardPlay play)
    {
        // Preserve the native current/max-HP calculation, upgrades and card ID.
        // FromCard makes the owner attack and applies the owner's combat powers.
        await DamageCmd.Attack(card.DynamicVars.CalculatedDamage.Calculate(play.Target))
            .FromCard(card, play).Targeting(play.Target!)
            .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3").Execute(context);
    }
}

[HarmonyPatch(typeof(CalculatedDamageVar), nameof(CalculatedDamageVar.UpdateCardPreview))]
internal static class ConstructOstyHealthPreview
{
    private static bool Prefix(CalculatedDamageVar __instance, CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
    {
        if (!ConstructOstyHealthAttack.Applies(card)) return true;
        var damage = new DamageVar(__instance.Name, __instance.Calculate(target), __instance.Props);
        damage.SetOwner(card);
        damage.UpdateCardPreview(card, previewMode, target, runGlobalHooks);
        __instance.EnchantedValue = damage.EnchantedValue;
        __instance.PreviewValue = damage.PreviewValue;
        return false;
    }
}

[HarmonyPatch]
internal static class ConstructOstyHealthWarning
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
        new[] { typeof(Unleash), typeof(Protector) }.Select(t => AccessTools.PropertyGetter(t, "ShouldGlowRedInternal"));
    private static bool Prefix(CardModel __instance, ref bool __result)
    {
        if (!ConstructOstyHealthAttack.Applies(__instance)) return true;
        __result = false;
        return false;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Tags), MethodType.Getter)]
internal static class ConstructOstyHealthTags
{
    private static void Postfix(CardModel __instance, ref IEnumerable<CardTag> __result)
    {
        if (ConstructOstyHealthAttack.Applies(__instance))
            __result = __result.Where(t => t != CardTag.OstyAttack).ToArray();
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Description), MethodType.Getter)]
internal static class ConstructOstyHealthDescription
{
    private static void Postfix(CardModel __instance, ref LocString __result)
    {
        if (!ConstructOstyHealthAttack.Applies(__instance)) return;
        var text = new LocString("cards", __instance is Unleash
            ? "PUNCHCONSTRUCT_ADAPTED_UNLEASH.description" : "PUNCHCONSTRUCT_ADAPTED_PROTECTOR.description");
        text.AddVariablesFrom(__result);
        __result = text;
    }
}
