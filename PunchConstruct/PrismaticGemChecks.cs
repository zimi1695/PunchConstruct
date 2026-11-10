using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task PrismaticGemChecks(Player player, Player native)
    {
        CardCreationOptions Options(bool combat) => CardCreationOptions.ForNonCombatWithUniformOdds([player.Character.CardPool], c => c is FightMe or Uppercut)
            .WithFlags(CardCreationFlags.IsCardReward | (combat ? CardCreationFlags.IsFromCombat : 0)).WithRngOverride(new Rng(722));
        foreach (bool rugFirst in new[] { false, true })
        {
            RelicModel? rug = rugFirst ? await RelicCmd.Obtain(ModelDb.Relic<ConstructDingyRug>().ToMutable(), player) : null;
            var gem = await RelicCmd.Obtain(ModelDb.Relic<ConstructPrismaticGem>().ToMutable(), player);
            rug ??= await RelicCmd.Obtain(ModelDb.Relic<ConstructDingyRug>().ToMutable(), player);
            var input = Options(true);
            var originalFilter = input.CardPoolFilter;
            var modified = gem.ModifyCardRewardCreationOptions(player, input);
            Check(!ReferenceEquals(input, modified) && ReferenceEquals(input.CardPoolFilter, originalFilter) &&
                input.GetPossibleCards(player).Any(c => c is FightMe) && modified.GetPossibleCards(player).All(c => !c.Pool.IsColorless),
                "Prismatic Gem filters a copied combat query without mutating reusable input; rugFirst=" + rugFirst);
            Check(Enumerable.Range(0, 30).All(_ => CardFactory.CreateForReward(player, 1, input).Single().Card is Uppercut),
                "Actual combat rewards exclude colorless even with the rug, in either relic order; rugFirst=" + rugFirst);
            foreach (var room in new[] { RoomType.Monster, RoomType.Elite, RoomType.Boss })
            {
                var battle = CardCreationOptions.ForRoom(player, room).WithFlags(CardCreationFlags.IsCardReward);
                Check(gem.ModifyCardRewardCreationOptions(player, battle).GetPossibleCards(player).All(c => !c.Pool.IsColorless),
                    "Encounter-sourced reward filtering covers native room options: " + room);
            }
            Check(gem.ModifyMaxEnergy(player, 3) == 3 + gem.DynamicVars.Energy.IntValue &&
                gem.DynamicDescription.GetFormattedText().Contains("战斗卡牌奖励"),
                "Adapted Gem retains native energy and accurately describes its combat-only drawback");
            var nonCombat = Options(false);
            Check(gem.ModifyCardRewardCreationOptions(player, nonCombat).GetPossibleCards(player).Any(c => c is FightMe),
                "Noncombat event card rewards still allow selected colorless cards");
            Check(gem.ModifyCardRewardCreationOptions(player, input.WithFlags(CardCreationFlags.NoCardPoolModifications | CardCreationFlags.IsCardReward | CardCreationFlags.IsFromCombat))
                .GetPossibleCards(player).Any(c => c is FightMe), "Explicit native no-pool-modification rewards retain their bypass");
            await RelicCmd.Remove(rug);
            await RelicCmd.Remove(gem);
            Check(CardFactory.CreateForReward(player, 2, Options(true)).Any(c => c.Card is FightMe),
                "Removing Gem restores colorless combat rewards");
        }
        var nativeGem = ModelDb.Relic<PrismaticGem>().ToMutable();
        native.AddRelicInternal(nativeGem);
        var nativeOptions = CardCreationOptions.ForNonCombatWithUniformOdds([native.Character.CardPool, ModelDb.CardPool<ColorlessCardPool>()], c => c is Fisticuffs or Uppercut)
            .WithFlags(CardCreationFlags.IsCardReward | CardCreationFlags.IsFromCombat);
        Check(nativeGem.ModifyCardRewardCreationOptions(native, nativeOptions).GetPossibleCards(native).Any(c => c is Fisticuffs) &&
            !nativeGem.DynamicDescription.GetFormattedText().Contains("战斗卡牌奖励"),
            "Other characters retain native Gem reward expansion and description");
    }
}
