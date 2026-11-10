using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task ScorchingWaveChecks(Player player, Creature enemy)
    {
        var run = player.RunState;
        var starter = player.Deck.Cards.OfType<IronWave>().Single();
        var original = ModelDb.Card<IronWave>();
        var native = Player.CreateForNewRun<Ironclad>(new MegaCrit.Sts2.Core.Unlocks.UnlockState([], [], 0), 9999);
        Check(starter.Rarity == CardRarity.Basic && !CardAcquisition.Eligible(player, original) &&
            run.CreateCard<IronWave>(native).Rarity == CardRarity.Common && original.Rarity == CardRarity.Common,
            "Ordinary Iron Wave is starter-only for Construct; native Ironclad and canonical rarity remain Common");
        var options = CardCreationOptions.ForRoom(player, RoomType.Monster).GetPossibleCards(player).ToArray();
        Check(options.Any(c => c is ScorchingIronWave) && options.All(c => c is not IronWave),
            "Native reward candidates replace ordinary Iron Wave with common Scorching Iron Wave");
        var transforms = CardFactory.GetDefaultTransformationOptions(starter, false).ToArray();
        Check(transforms.Any(c => c is ScorchingIronWave) && transforms.All(c => c is not IronWave),
            "Native transformation candidates contain Scorching Iron Wave and exclude the starter");
        var card = run.CreateCard<ScorchingIronWave>(player);
        Check(card.EnergyCost.GetWithModifiers(CostModifiers.None) == 1 && card.DynamicVars.Damage.BaseValue == 5 &&
            card.DynamicVars.Block.BaseValue == 5 && card.Rarity == CardRarity.Common && card.Portrait != null,
            "Scorching Iron Wave loads its portrait as a one-cost 5/5 common");
        for (int level = 1; level <= 8; level++)
        {
            CardCmd.Upgrade(card);
            Check(card.CurrentUpgradeLevel == level && card.IsUpgradable && card.DynamicVars.Damage.BaseValue == 5 + level * (level + 3) / 2 &&
                card.DynamicVars.Block.BaseValue == 5 + level * (level + 3) / 2,
                "Native upgrade command adds successive +2/+3/+4 damage and block: " + level);
        }
        foreach (var copy in new[] { run.CloneCard(card), run.LoadCard(card.ToSerializable(), player) })
        {
            Check(copy.CurrentUpgradeLevel == 8 && copy.DynamicVars.Damage.BaseValue == 49 && copy.DynamicVars.Block.BaseValue == 49,
                "Cloning/native serialization preserve all eight upgrades");
            CardCmd.Upgrade(copy);
            Check(copy.CurrentUpgradeLevel == 9 && copy.DynamicVars.Damage.BaseValue == 59 && copy.DynamicVars.Block.BaseValue == 59,
                "Cloned/restored card can upgrade again exactly once");
            copy.DowngradeInternal(); copy.FinalizeUpgradeInternal();
            Check(copy.CurrentUpgradeLevel == 0 && copy.DynamicVars.Damage.BaseValue == 5 && copy.DynamicVars.Block.BaseValue == 5,
                "Native downgrade resets repeated upgrades to 5/5");
        }
        Check(card.CurrentUpgradeLevel == 8 && card.DynamicVars.Damage.BaseValue == 49 &&
            ModelDb.Card<ScorchingIronWave>().DynamicVars.Damage.BaseValue == 5 && original.MaxUpgradeLevel == 1,
            "Copy upgrades do not mutate source/canonical cards or ordinary Iron Wave's upgrade limit");
        // Real queued plays check the two relics independently and together.
        RelicModel? dummy = null, fake = null;
        foreach (int bonus in new[] { 0, 3, 1, 4 })
        {
            if (dummy != null) { await RelicCmd.Remove(dummy); dummy = null; }
            if (fake != null) { await RelicCmd.Remove(fake); fake = null; }
            if (bonus is 3 or 4) dummy = await RelicCmd.Obtain(ModelDb.Relic<ConstructStrikeDummy>().ToMutable(), player);
            if (bonus is 1 or 4) fake = await RelicCmd.Obtain(ModelDb.Relic<ConstructFakeStrikeDummy>().ToMutable(), player);
            foreach (int level in new[] { 0, 3 })
            {
                var played = ((CombatState)player.Creature.CombatState!).CreateCard<ScorchingIronWave>(player);
                for (int i = 0; i < level; i++) CardCmd.Upgrade(played);
                await CardPileCmd.AddGeneratedCardToCombat(played, PileType.Hand, player);
                await PlayerCmd.SetEnergy(20, player);
                decimal hp = enemy.CurrentHp, block = player.Creature.Block;
                Check(played.TryManualPlay(enemy), $"Queue Scorching Iron Wave level={level}, dummy bonus={bonus}");
                await Until(() => played.Pile?.Type == PileType.Discard, "Scorching Iron Wave resolves");
                await Frames(15);
                Check(enemy.CurrentHp == hp - (5 + level * (level + 3) / 2 + bonus) && player.Creature.Block == block + 5 + level * (level + 3) / 2,
                    $"Scorching Iron Wave damage/block and independent dummy bonuses: level={level}, bonus={bonus}");
            }
        }
        if (dummy != null) await RelicCmd.Remove(dummy);
        if (fake != null) await RelicCmd.Remove(fake);
    }
}
