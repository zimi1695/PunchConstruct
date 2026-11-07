using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;

namespace PunchConstruct;

internal static class PersistentCardKeywords
{
    // Native AddKeyword is a runtime mutation, so Soup also records its permanent grant.
    internal static readonly SavedSpireField<CardModel, bool> SoupEternal = new(() => false, "PunchConstruct_SoupEternal");
    static PersistentCardKeywords() => SoupEternal.CopyOnClone();
    internal static void MakeEternal(CardModel card)
    {
        SoupEternal[card] = true;
        card.AddKeyword(CardKeyword.Eternal);
        card.EnergyCost.SetCustomBaseCost(0);
    }
}

// Also covers Soup cards created by older builds after their saved marker loads.
// The normal temporary cost modifiers still apply to the zero base cost.
internal static class RestoreSoupFreeCost
{
    internal static void Apply(CardModel __instance, CardEnergyCost __result)
    {
        if (RegentAdaptations.Owns(__instance) && PersistentCardKeywords.SoupEternal[__instance] &&
            __result.GetWithModifiers(CostModifiers.None) != 0)
            __result.SetCustomBaseCost(0);
    }
}

internal static class RestoreSoupEternal
{
    internal static void Apply(CardModel __instance, ref IReadOnlySet<CardKeyword> __result)
    {
        if (PersistentCardKeywords.SoupEternal[__instance] && !__result.Contains(CardKeyword.Eternal))
            __result = new HashSet<CardKeyword>(__result) { CardKeyword.Eternal };
    }
}
