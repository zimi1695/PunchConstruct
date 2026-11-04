using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;

namespace PunchConstruct;

internal static class RegentAdaptations
{
    // Confirmed: energy replaces the original star cost for this character.
    internal static bool ReplaceStarCost = true;
    internal static readonly Dictionary<Type, (int Cost, int Damage)> Changes = new()
    {
        [typeof(FallingStar)] = (1, 0), [typeof(MeteorShower)] = (1, 0), [typeof(GammaBlast)] = (2, 3),
        [typeof(Reflect)] = (2, 0), [typeof(Resonance)] = (1, 0), [typeof(Comet)] = (3, 6), [typeof(DyingStar)] = (2, 0)
    };
    private static readonly SpireField<CardModel, bool> Adapted = new SpireField<CardModel, bool>(() => false).CopyOnClone();
    private static readonly AccessTools.FieldRef<CardModel, Player?> OwnerField = AccessTools.FieldRefAccess<CardModel, Player?>("_owner");
    private static readonly AccessTools.FieldRef<CardModel, int> StarBase = AccessTools.FieldRefAccess<CardModel, int>("_baseStarCost");
    private static readonly AccessTools.FieldRef<CardModel, bool> StarSet = AccessTools.FieldRefAccess<CardModel, bool>("_starCostSet");
    internal static bool Owns(CardModel card) => OwnerField(card)?.Character is ConstructCharacter;
    internal static void Apply(CardModel card, bool reset = false)
    {
        if (!Changes.TryGetValue(card.GetType(), out var change)) return;
        bool shouldAdapt = Owns(card);
        bool wasAdapted = Adapted[card];
        if (!reset && wasAdapted == shouldAdapt) return;
        if (shouldAdapt)
        {
            card.EnergyCost.SetCustomBaseCost(change.Cost);
            if (change.Damage != 0) card.DynamicVars.Damage.BaseValue += change.Damage;
            if (ReplaceStarCost) { StarBase(card) = 0; StarSet(card) = true; }
        }
        else if (wasAdapted && !reset)
        {
            card.EnergyCost.SetCustomBaseCost(card.EnergyCost.Canonical);
            if (change.Damage != 0) card.DynamicVars.Damage.BaseValue -= change.Damage;
            StarBase(card) = card.CanonicalStarCost; StarSet(card) = true;
        }
        Adapted[card] = shouldAdapt;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Owner), MethodType.Setter)]
internal static class RegentOwnerAdaptation
{
    private static void Postfix(CardModel __instance) => RegentAdaptations.Apply(__instance);
}

[HarmonyPatch(typeof(CardModel), "AfterDowngraded")]
internal static class RegentDowngradeAdaptation
{
    private static void Postfix(CardModel __instance) => RegentAdaptations.Apply(__instance, true);
}

[HarmonyPatch]
internal static class RegentStarCostAdaptation
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => RegentAdaptations.Changes.Keys
        .Select(t => AccessTools.PropertyGetter(t, nameof(CardModel.CanonicalStarCost))).Distinct();
    private static void Postfix(CardModel __instance, ref int __result)
    {
        if (RegentAdaptations.ReplaceStarCost && RegentAdaptations.Owns(__instance)) __result = 0;
    }
}

// Zero star cost is still a displayed resource in the native renderer. Hide its
// whole badge (including the child label) only on cards converted to energy.
[HarmonyPatch]
internal static class RegentStarCostVisuals
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => [
        AccessTools.Method(typeof(NCard), nameof(NCard.UpdateVisuals)),
        AccessTools.Method(typeof(NCard), "UpdateStarCostVisuals"),
        AccessTools.Method(typeof(NCard), "UpdateStarCostText")
    ];
    private static void Postfix(NCard __instance, TextureRect? ____starIcon)
    {
        if (__instance.Model is { } card && RegentAdaptations.ReplaceStarCost &&
            RegentAdaptations.Owns(card) && RegentAdaptations.Changes.ContainsKey(card.GetType()))
            ____starIcon?.Hide();
    }
}
