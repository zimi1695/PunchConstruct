using BaseLib.Abstracts;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

public abstract class ConstructSystemPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override string CustomPackedIconPath => ConstructPowerIcons.Packed(GetType());
    public override string CustomBigIconPath => ConstructPowerIcons.Big(GetType());
    public override string CustomBigBetaIconPath => CustomBigIconPath;
}

public sealed class ForgeWorkshopPower : ConstructSystemPower
{
    public override List<(string, string)> Localization => Loc.Power("锻造车间", "每次[gold]锻造[/gold]时，获得{Amount}点[gold]格挡[/gold]。", "每次[gold]锻造[/gold]时，获得{Amount}点[gold]格挡[/gold]。");
    public override async Task AfterForge(decimal amount, Player forger, AbstractModel? source)
    {
        if (forger.Creature == Owner) await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Unpowered, null);
    }
}

public sealed class RoyalBearingPower : ConstructSystemPower
{
    public override List<(string, string)> Localization => Loc.Power("王者风范", "回合结束时，如果手牌中有[gold]君王之剑[/gold]，获得{Amount}点[gold]格挡[/gold]。", "回合结束时，如果手牌中有[gold]君王之剑[/gold]，获得{Amount}点[gold]格挡[/gold]。");
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext context, CombatSide side, IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner) && Owner.Player!.PlayerCombatState!.Hand.Cards.Any(c => c is SovereignBlade))
            await CreatureCmd.GainBlock(Owner, Amount, ValueProp.Unpowered, null);
    }
}

public sealed class StrikeWhileHotPower : ConstructSystemPower
{
    public override List<(string, string)> Localization => Loc.Power("趁热打铁", "回合结束时，如果格挡大于等于10点，[gold]锻造[/gold]{Amount}。", "回合结束时，如果格挡大于等于10点，[gold]锻造[/gold]{Amount}。");
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext context, CombatSide side, IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner) && Owner.Block >= 10) await ForgeCmd.Forge(Amount, Owner.Player!, this);
    }
}

public sealed class FinalJudgmentPower : ConstructSystemPower
{
    public override PowerStackType StackType => PowerStackType.None;
    public override List<(string, string)> Localization => Loc.Power("终局裁定", "灾厄大于等于生命值的敌人无法对你造成伤害。", "灾厄大于等于生命值的敌人无法对你造成伤害。");
    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay) =>
        target == Owner && dealer is { IsEnemy: true } && dealer.GetPowerAmount<DoomPower>() >= dealer.CurrentHp ? 0 : 1;
}

public sealed class UnarmToArmsPower : ConstructSystemPower
{
    public override PowerStackType StackType => PowerStackType.None;
    public override List<(string, string)> Localization => Loc.Power("卸甲成军", "本回合获得格挡时，改为等量召唤。已有格挡保留。", "本回合获得格挡时，改为等量召唤。已有格挡保留。");
    public override async Task AfterSideTurnEnd(PlayerChoiceContext context, CombatSide side, IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner)) await PowerCmd.Remove(this);
    }
}

// Replace the single numeric GainBlock entry point; all BlockVar overloads delegate here.
// Run native block modifiers once, then summon without firing block-gained hooks or history.
// Combat-domain adapter: see NativeBridge/CombatBridge.cs.

internal static class UnarmBlockReplacement
{
    internal static bool Apply(Creature creature, decimal amount, ValueProp props, CardPlay? cardPlay, ref Task<decimal> __result)
    {
        if (!CharacterScope.IsConstruct(creature.Player) || !creature.HasPower<UnarmToArmsPower>() || creature.IsDead || CombatManager.Instance.IsOverOrEnding) return true;
        __result = Replace(creature, amount, props, cardPlay);
        return false;
    }
    private static async Task<decimal> Replace(Creature creature, decimal amount, ValueProp props, CardPlay? cardPlay)
    {
        var state = creature.CombatState!;
        await Hook.BeforeBlockGained(state, creature, amount, props, cardPlay?.Card);
        decimal modified = Math.Max(0, Hook.ModifyBlock(state, creature, amount, props, cardPlay?.Card, cardPlay, out var modifiers));
        await Hook.AfterModifyingBlockAmount(state, modified, cardPlay?.Card, cardPlay, modifiers);
        if (modified > 0) await OstyCmd.Summon(new BlockingPlayerChoiceContext(), creature.Player!, modified, creature.GetPower<UnarmToArmsPower>()!);
        return 0;
    }
}

public sealed class NeverBackDownPower : ConstructSystemPower
{
    public override List<(string, string)> Localization => [("title", Loc.Pick("不倒不休")), ("description", Loc.Pick("回合开始时，将{Amount}张[gold]拳斗[/gold]加入你的手牌，将{Amount}张[gold]灼烧[/gold]加入弃牌堆。"))];
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext context, Player player)
    {
        if (player.Creature != Owner) return;
        for (int i = 0; i < Amount; i++)
        {
            var card = ((CombatState)Owner.CombatState!).CreateCard<Fisticuffs>(player);
            var fistAdded = await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
            if (fistAdded.cardAdded?.Pile?.Type == PileType.Discard) CardCmd.PreviewCardPileAdd(fistAdded);
            var burn = ((CombatState)Owner.CombatState!).CreateCard<Burn>(player);
            var added = await CardPileCmd.AddGeneratedCardToCombat(burn, PileType.Discard, player);
            CardCmd.PreviewCardPileAdd(added);
        }
    }
}

public sealed class BoxingChampionFormPower : ConstructSystemPower
{
    public override List<(string, string)> Localization => Loc.Power("拳王形态", "每回合开始时，获得{Amount}点力量和{Amount}点敏捷。", "每回合开始时，获得{Amount}点力量和{Amount}点敏捷。");
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext context, Player player)
    {
        if (player.Creature != Owner) return;
        await PowerCmd.Apply<StrengthPower>(context, Owner, Amount, Owner, null);
        await PowerCmd.Apply<DexterityPower>(context, Owner, Amount, Owner, null);
    }
}
