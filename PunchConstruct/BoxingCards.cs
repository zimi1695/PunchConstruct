using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

internal static class BoxingGeneration
{
    internal static bool Restricted(CardModel card) => card is RightHook or QuickPunch or CombinationPunch or BoxingGuard;
    internal static async Task Add(CardModel card)
    {
        var added = await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, card.Owner);
        if (added.cardAdded?.Pile?.Type == PileType.Discard) CardCmd.PreviewCardPileAdd(added);
    }
}

[Pool(typeof(ConstructCardPool))]
public sealed class BrambleField() : CustomCardModel(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    public override string CustomPortraitPath => "res://PunchConstruct/art/bramble_field.ctex";
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<ThornsPower>()];
    public override List<(string, string)> Localization => Loc.Card("披荆斩棘", "所有单位获得2层[gold]荆棘[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        // Snapshot both sides, including teammates and summoned creatures. The
        // native Thorns power handles reflection without recursive attack damage.
        var targets = CombatState!.GetCreaturesOnSide(CombatSide.Player)
            .Concat(CombatState.GetCreaturesOnSide(CombatSide.Enemy)).Where(c => c.IsAlive).ToArray();
        VfxCmd.PlayOnCreatureCenter(Owner.Creature, "vfx/vfx_flying_slash");
        foreach (var target in targets)
            await PowerCmd.Apply<ThornsPower>(context, target, 2, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

[Pool(typeof(ConstructCardPool))]
public sealed class LeftHook() : CustomCardModel(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    public override string CustomPortraitPath => "res://PunchConstruct/art/left_hook.ctex";
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(6, ValueProp.Move), new BlockVar(4, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromCard<RightHook>(IsUpgraded)];
    public override List<(string, string)> Localization => Loc.Card("左勾拳", "造成{Damage:diff()}点伤害。\n获得{Block:diff()}点[gold]格挡[/gold]。\n将一张[gold]{RightTitle}[/gold]加入你的手牌。");
    protected override void AddExtraArgsToDescription(LocString description)
    {
        base.AddExtraArgsToDescription(description);
        description.Add("RightTitle", IsUpgraded ? "右勾拳+" : "右勾拳");
    }
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play).Targeting(play.Target!).Execute(context);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play);
        var right = CombatState!.CreateCard<RightHook>(Owner);
        if (IsUpgraded) CardCmd.Upgrade(right);
        await BoxingGeneration.Add(right);
    }
    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2); DynamicVars.Block.UpgradeValueBy(2); }
}

[Pool(typeof(ConstructCardPool))]
public sealed class RightHook() : CustomCardModel(2, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override string CustomPortraitPath => "res://PunchConstruct/art/right_hook.ctex";
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain, CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(12, ValueProp.Move), new DamageVar(10, ValueProp.Move)];
    public override List<(string, string)> Localization => Loc.Card("右勾拳", "获得{Block:diff()}点[gold]格挡[/gold]。\n造成{Damage:diff()}点伤害。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play).Targeting(play.Target!).Execute(context);
    }
    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2); DynamicVars.Block.UpgradeValueBy(2); }
}

[Pool(typeof(ConstructCardPool))]
public sealed class CombatRoutine() : CustomCardModel(1, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    public override string CustomPortraitPath => "res://PunchConstruct/art/combat_routine.ctex";
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromCard<QuickPunch>(), HoverTipFactory.FromCard<CombinationPunch>(), HoverTipFactory.FromCard<BoxingGuard>()];
    public override List<(string, string)> Localization => Loc.Card("格斗流程", "将[gold]快速拳[/gold]、[gold]连环拳[/gold]、[gold]格挡[/gold]各一张加入你的手牌。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await BoxingGeneration.Add(CombatState!.CreateCard<QuickPunch>(Owner));
        await BoxingGeneration.Add(CombatState!.CreateCard<CombinationPunch>(Owner));
        await BoxingGeneration.Add(CombatState!.CreateCard<BoxingGuard>(Owner));
    }
    protected override void OnUpgrade() => AddKeyword(CardKeyword.Innate);
}

[Pool(typeof(ConstructCardPool))]
public sealed class QuickPunch() : CustomCardModel(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override string CustomPortraitPath => "res://PunchConstruct/art/quick_punch.ctex";
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(16, ValueProp.Move)];
    public override List<(string, string)> Localization => Loc.Card("快速拳", "造成{Damage:diff()}点伤害。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play) =>
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play).Targeting(play.Target!).Execute(context);
    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);
}

[Pool(typeof(ConstructCardPool))]
public sealed class CombinationPunch() : CustomCardModel(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override string CustomPortraitPath => "res://PunchConstruct/art/combination_punch.ctex";
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(6, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<FrailPower>()];
    public override List<(string, string)> Localization => Loc.Card("连环拳", "造成{Damage:diff()}点伤害2次。\n给予该敌人2层[gold]脆弱[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var enemy = play.Target!;
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play).Targeting(enemy).WithHitCount(2).Execute(context);
        if (enemy.IsAlive) await PowerCmd.Apply<FrailPower>(context, enemy, 2, Owner.Creature, this);
    }
    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);
}

[Pool(typeof(ConstructCardPool))]
public sealed class BoxingGuard() : CustomCardModel(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override string CustomPortraitPath => "res://PunchConstruct/art/boxing_guard.ctex";
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(10, ValueProp.Move)];
    public override List<(string, string)> Localization => Loc.Card("格挡", "获得{Block:diff()}点[gold]格挡[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play);
    }
    protected override void OnUpgrade() => AddKeyword(CardKeyword.Retain);
}

[Pool(typeof(ConstructCardPool))]
public sealed class FurnaceOverload() : CustomCardModel(0, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    public override string CustomPortraitPath => "res://PunchConstruct/art/furnace_overload.ctex";
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(6, ValueProp.Move), new DynamicVar("Strength", 1), new CardsVar(1)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<StrengthPower>(), HoverTipFactory.FromCard<Burn>()];
    public override List<(string, string)> Localization => Loc.Card("过载", "获得{Block:diff()}点[gold]格挡[/gold]。\n获得{Strength:diff()}点[gold]力量[/gold]。\n抽{Cards:diff()}张牌。\n将两张[gold]灼烧[/gold]加入你的手牌。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play);
        await PowerCmd.Apply<StrengthPower>(context, Owner.Creature, DynamicVars["Strength"].BaseValue, Owner.Creature, this);
        await CardPileCmd.Draw(context, DynamicVars.Cards.BaseValue, Owner);
        for (int i = 0; i < 2; i++) await BoxingGeneration.Add(CombatState!.CreateCard<Burn>(Owner));
    }
    protected override void OnUpgrade() { DynamicVars.Block.UpgradeValueBy(2); DynamicVars["Strength"].UpgradeValueBy(1); DynamicVars.Cards.UpgradeValueBy(1); }
}
