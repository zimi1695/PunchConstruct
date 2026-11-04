using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;

namespace PunchConstruct;

[HarmonyPatch]
internal static class ConstructSoupOffer
{
    internal static MethodBase Predicate => typeof(Tezcatara).GetNestedTypes(BindingFlags.NonPublic)
        .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
        .Single(m => m.Name.StartsWith("<GenerateInitialOptions>") && m.ReturnType == typeof(bool) &&
            m.GetParameters() is [{ ParameterType: var p }] && p == typeof(CardModel));
    private static MethodBase TargetMethod() => Predicate;
    private static bool Prefix(CardModel __0, ref bool __result)
    {
        if (!RegentAdaptations.Owns(__0)) return true;
        __result = __0 is SmallWave && __0.Enchantment == null;
        return false;
    }
}

[HarmonyPatch(typeof(NeowsTalisman), nameof(NeowsTalisman.AfterObtained))]
internal static class ConstructNeowsTalisman
{
    private static bool Prefix(NeowsTalisman __instance, ref Task __result)
    {
        if (!CharacterScope.IsConstruct(__instance)) return true;
        __result = Upgrade(__instance);
        return false;
    }
    private static async Task Upgrade(NeowsTalisman relic)
    {
        int count = Math.Min(2, relic.Owner.Deck.Cards.Count(c => c is SmallWave && c.IsUpgradable));
        if (count == 0) return;
        var cards = await CardSelectCmd.FromDeckGeneric(relic.Owner,
            new CardSelectorPrefs(CardSelectorPrefs.UpgradeSelectionPrompt, count), c => c is SmallWave && c.IsUpgradable);
        foreach (var card in cards) CardCmd.Upgrade(card);
    }
}
