using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace PunchConstruct;

// Keep the native card, upgrade and temporary cost modifiers. Only its owned
// custom base cost changes; canonical cards and other characters stay native.
internal static class AbrasiveAdaptation
{
    private static readonly SpireField<CardModel, bool> Adapted = new SpireField<CardModel, bool>(() => false).CopyOnClone();

    internal static void Apply(CardModel card, bool reset = false)
    {
        if (card is not Abrasive) return;
        bool owned = RegentAdaptations.Owns(card);
        bool wasAdapted = Adapted[card];
        if (!reset && owned == wasAdapted) return;
        if (owned) card.EnergyCost.SetCustomBaseCost(2);
        else if (wasAdapted && !reset) card.EnergyCost.SetCustomBaseCost(card.EnergyCost.Canonical);
        Adapted[card] = owned;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Owner), MethodType.Setter)]
internal static class AbrasiveOwnerAdaptation
{
    private static void Postfix(CardModel __instance) => AbrasiveAdaptation.Apply(__instance);
}

[HarmonyPatch(typeof(CardModel), "AfterDowngraded")]
internal static class AbrasiveDowngradeAdaptation
{
    private static void Postfix(CardModel __instance) => AbrasiveAdaptation.Apply(__instance, true);
}
