using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace PunchConstruct;

internal static class AncientShopCards
{
    internal static void Apply(MerchantInventory __instance)
    {
        var player = __instance.Player;
        if (!CharacterScope.IsConstruct(player) || player.PlayerRng.Shops.NextFloat() >= CardAcquisition.AncientShopChance) return;
        Install(__instance);
    }
    internal static void Install(MerchantInventory inventory)
    {
        var player = inventory.Player;
        var options = new CardModel[] { ModelDb.Card<Suppress>(), ModelDb.Card<MeteorShower>(), ModelDb.Card<Protector>(), ModelDb.Card<Whistle>() }
            .Where(c => c is not Protector || player.Deck.Cards.Any(d => d is Bodyguard or UnarmToArms or DoomResonance)).ToArray();
        var chosen = player.PlayerRng.Shops.NextItem(options)!;
        var card = player.RunState.CreateCard(chosen, player);
        // Courier restocking must still draw from the regular eligible pool.
        var entry = new MerchantCardEntry(player, inventory, player.Character.CardPool.AllCards, chosen.Type);
        AccessTools.PropertySetter(typeof(MerchantCardEntry), nameof(MerchantCardEntry.CreationResult)).Invoke(entry, [new CardCreationResult(card)]);
        entry.CalcCost();
        var entries = (List<MerchantCardEntry>)AccessTools.Field(typeof(MerchantInventory), "_characterCardEntries").GetValue(inventory)!;
        entries[player.PlayerRng.Shops.NextInt(entries.Count)] = entry;
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
