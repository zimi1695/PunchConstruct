using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;

namespace PunchConstruct;

[Pool(typeof(ConstructCardPool))]
public sealed class ForgeWorkshop() : CustomCardModel(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<RegentCardPool>();
    protected override IEnumerable<IHoverTip> ExtraHoverTips => HoverTipFactory.FromForge();
    public override string CustomPortraitPath => "res://PunchConstruct/art/forge_workshop.ctex";
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Amount", 4)];
    public override List<(string, string)> Localization => Loc.Card("锻造车间", "每次[gold]锻造[/gold]时，获得{Amount:diff()}点[gold]格挡[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await PowerCmd.Apply<ForgeWorkshopPower>(context, Owner.Creature, DynamicVars["Amount"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars["Amount"].UpgradeValueBy(2);
}

[Pool(typeof(ConstructCardPool))]
public sealed class RoyalBearing() : CustomCardModel(1, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<RegentCardPool>();
    protected override IEnumerable<IHoverTip> ExtraHoverTips => HoverTipFactory.FromForge();
    public override string CustomPortraitPath => "res://PunchConstruct/art/royal_bearing.ctex";
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Forge", 3), new DynamicVar("Amount", 4)];
    public override List<(string, string)> Localization => Loc.Card("王者风范", "[gold]锻造[/gold]{Forge:diff()}。\n你的回合结束时，如果手牌中有[gold]君王之剑[/gold]，获得{Amount:diff()}点[gold]格挡[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await ForgeCmd.Forge(DynamicVars["Forge"].BaseValue, Owner, this);
        await PowerCmd.Apply<RoyalBearingPower>(context, Owner.Creature, DynamicVars["Amount"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Forge"].UpgradeValueBy(2);
        DynamicVars["Amount"].UpgradeValueBy(2);
    }
}

[Pool(typeof(ConstructCardPool))]
public sealed class StrikeWhileHot() : CustomCardModel(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<RegentCardPool>();
    protected override IEnumerable<IHoverTip> ExtraHoverTips => HoverTipFactory.FromForge();
    public override string CustomPortraitPath => "res://PunchConstruct/art/strike_while_hot.ctex";
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Amount", 7)];
    public override List<(string, string)> Localization => Loc.Card("趁热打铁", "你的回合结束时，如果[gold]格挡[/gold]大于等于10点，[gold]锻造[/gold]{Amount:diff()}。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await PowerCmd.Apply<StrikeWhileHotPower>(context, Owner.Creature, DynamicVars["Amount"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars["Amount"].UpgradeValueBy(3);
}

[Pool(typeof(ConstructCardPool))]
public sealed class FinalJudgment() : CustomCardModel(1, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<NecrobinderCardPool>();
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<DoomPower>()];
    public override string CustomPortraitPath => "res://PunchConstruct/art/final_judgment.ctex";
    public override List<(string, string)> Localization => Loc.Card("终局裁定", "[gold]灾厄[/gold]大于等于生命值的敌人无法对你造成伤害。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await PowerCmd.Apply<FinalJudgmentPower>(context, Owner.Creature, 1, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

[Pool(typeof(ConstructCardPool))]
public sealed class UnarmToArms() : CustomCardModel(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<NecrobinderCardPool>();
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.Static(StaticHoverTip.SummonStatic)];
    public override string CustomPortraitPath => "res://PunchConstruct/art/unarm_to_arms.ctex";
    public override List<(string, string)> Localization => Loc.Card("卸甲成军", "本回合获得[gold]格挡[/gold]时，改为等量[gold]召唤[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await PowerCmd.Apply<UnarmToArmsPower>(context, Owner.Creature, 1, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

[Pool(typeof(ConstructCardPool))]
public sealed class DoomResonance() : CustomCardModel(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<NecrobinderCardPool>();
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.Static(StaticHoverTip.SummonDynamic, [DynamicVars.Summon]), HoverTipFactory.FromPower<DoomPower>()];
    public override string CustomPortraitPath => "res://PunchConstruct/art/doom_resonance.ctex";
    protected override IEnumerable<DynamicVar> CanonicalVars => [new SummonVar(3)];
    public override List<(string, string)> Localization => Loc.Card("厄运共鸣", "[gold]召唤[/gold]{Summon:diff()}。\n给予所有敌人等同于奥斯提当前生命值的[gold]灾厄[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await OstyCmd.Summon(context, Owner, DynamicVars["Summon"].BaseValue, this);
        decimal amount = Owner.Osty?.CurrentHp ?? 0;
        foreach (var enemy in CombatState!.HittableEnemies.ToArray())
            await PowerCmd.Apply<DoomPower>(context, enemy, amount, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars["Summon"].UpgradeValueBy(2);
}

[Pool(typeof(ConstructCardPool))]
public sealed class NeverBackDown() : CustomCardModel(2, CardType.Power, CardRarity.Rare, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromCard<Fisticuffs>(), HoverTipFactory.FromCard<Burn>()];
    public override string CustomPortraitPath => "res://PunchConstruct/art/never_back_down.ctex";
    public override List<(string, string)> Localization => Loc.Card("不倒不休", "每回合开始时，将一张[gold]拳斗[/gold]加入你的手牌，将一张[gold]灼烧[/gold]加入弃牌堆。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await PowerCmd.Apply<NeverBackDownPower>(context, Owner.Creature, 1, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

[Pool(typeof(ConstructCardPool))]
public sealed class BoxingChampionForm() : CustomCardModel(3, CardType.Power, CardRarity.Ancient, TargetType.Self)
{
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<StrengthPower>(), HoverTipFactory.FromPower<DexterityPower>()];
    public override string CustomPortraitPath => "res://PunchConstruct/art/boxing_champion_form.ctex";
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Amount", 2)];
    public override List<(string, string)> Localization => Loc.Card("拳王形态", "每回合开始时，获得{Amount:diff()}点[gold]力量[/gold]和{Amount:diff()}点[gold]敏捷[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await PowerCmd.Apply<BoxingChampionFormPower>(context, Owner.Creature, DynamicVars["Amount"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars["Amount"].UpgradeValueBy(1);
}
