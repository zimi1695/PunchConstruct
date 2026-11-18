using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Random;

namespace PunchConstruct;

internal static class AncientShopCards
{
    internal static void Apply(MerchantInventory __instance)
    {
        var player = __instance.Player;
        // The 5% check, card choice and slot choice are mod-only. Use a copy of
        // the Shops stream so the native counter a predictor replays stays
        // untouched. The copy advances one extra time for the card choice
        // before picking a slot; that divergence is intentional and stays
        // inside this mod-only branch.
        var rng = new Rng(player.PlayerRng.Shops.ToSerializable());
        if (!CharacterScope.IsConstruct(player) || rng.NextFloat() >= CardAcquisition.AncientShopChance) return;
        Install(__instance, rng);
    }
    internal static void Install(MerchantInventory inventory, Rng rng)
    {
        var player = inventory.Player;
        var entries = inventory.CharacterCardEntries;
        if (entries.Count == 0) return;
        var options = new CardModel[] { ModelDb.Card<Suppress>(), ModelDb.Card<MeteorShower>(), ModelDb.Card<Protector>(), ModelDb.Card<Whistle>() }
            .Where(c => c is not Protector || player.Deck.Cards.Any(d => d is Bodyguard or UnarmToArms or DoomResonance)).ToArray();
        var chosen = rng.NextItem(options)!;
        var card = player.RunState.CreateCard(chosen, player);
        // Keep the native entry so its id and purchase subscription stay valid;
        // Courier restocking still draws from its regular eligible pool.
        var entry = entries[rng.NextInt(entries.Count)];
        AccessTools.PropertySetter(typeof(MerchantCardEntry), nameof(MerchantCardEntry.CreationResult)).Invoke(entry, [new CardCreationResult(card)]);
        entry.CalcCost();
    }
}

internal static class AncientShopPrice
{
    internal static void Apply(CardModel card, ref int __result)
    {
        if (!RegentAdaptations.Owns(card) || !CardAcquisition.ShopAncients.Contains(card.GetType())) return;
        int rare = (int)AccessTools.Method(typeof(MerchantCardEntry), "GetCost").Invoke(null, [ModelDb.Card<Juggernaut>()])!;
        __result = (int)Math.Ceiling(rare * CardAcquisition.AncientPriceMultiplier);
    }
}
