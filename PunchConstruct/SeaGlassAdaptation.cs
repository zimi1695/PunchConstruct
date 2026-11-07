using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

// Relic-domain adapter: see NativeBridge/RelicBridge.cs.
internal static class ConstructSeaGlass
{
    internal static bool Apply(SeaGlass __instance, ref Task __result)
    {
        if (!CharacterScope.IsConstruct(__instance)) return true;
        __result = Offer(__instance);
        return false;
    }
    private static async Task Offer(SeaGlass relic)
    {
        var owner = relic.Owner;
        var rewards = new List<CardCreationResult>();
        bool previous = CardAcquisition.SeaGlassSelection;
        CardAcquisition.SeaGlassSelection = true;
        try
        {
            foreach (var pool in new CardPoolModel[] { ModelDb.CardPool<RegentCardPool>(), ModelDb.CardPool<NecrobinderCardPool>(), ModelDb.CardPool<DefectCardPool>() })
            {
                var options = CardCreationOptions.ForNonCombatWithUniformOdds([pool], CardAcquisition.IsSelectedImport)
                    .WithFlags((CardCreationFlags)17);
                rewards.AddRange(CardFactory.CreateForReward(owner, 5, options));
            }
        }
        finally { CardAcquisition.SeaGlassSelection = previous; }
        var prefs = new CardSelectorPrefs(new LocString("relics", "SEA_GLASS.selectionScreenPrompt"), 0, rewards.Count);
        var selected = await CardSelectCmd.FromSimpleGridForRewards(new BlockingPlayerChoiceContext(), rewards, owner, prefs);
        foreach (var card in selected)
        {
            var result = await CardPileCmd.Add(card, PileType.Deck);
            CardCmd.PreviewCardPileAdd(result);
        }
    }
}

internal static class ConstructSeaGlassTitle
{
    internal static void Apply(SeaGlass __instance, ref LocString __result)
    {
        if (CharacterScope.IsConstruct(__instance)) __result = new LocString("relics", "SEA_GLASS.title");
    }
}
