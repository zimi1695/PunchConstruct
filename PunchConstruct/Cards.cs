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
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

[Pool(typeof(ConstructCardPool))]
public sealed class ScorchingIronWave() : CustomCardModel(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    public override CardPoolModel Pool => ModelDb.CardPool<IroncladCardPool>();
    public override int MaxUpgradeLevel => int.MaxValue;
    public override string CustomPortraitPath => "res://PunchConstruct/art/scorching_iron_wave.ctex";
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(5, ValueProp.Move), new BlockVar(5, ValueProp.Move)];
    public override List<(string, string)> Localization => Loc.Card("灼热铁斩波", "造成{Damage:diff()}点伤害。\n获得{Block:diff()}点[gold]格挡[/gold]。\n可多次升级。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play).Targeting(play.Target!).Execute(context);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play);
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(CurrentUpgradeLevel + 1);
        DynamicVars.Block.UpgradeValueBy(CurrentUpgradeLevel + 1);
    }
}

[Pool(typeof(ConstructCardPool))]
public sealed class SmallWave() : CustomCardModel(1, CardType.Attack, CardRarity.Basic, TargetType.AnyEnemy)
{
    public override string CustomPortraitPath => "res://PunchConstruct/art/small_wave.ctex";
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(3, ValueProp.Move), new BlockVar(3, ValueProp.Move)];
    public override List<(string, string)> Localization => Loc.Card("小斩波", "造成{Damage:diff()}点伤害。\n获得{Block:diff()}点[gold]格挡[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play).Targeting(play.Target!).Execute(context);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play);
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
        DynamicVars.Block.UpgradeValueBy(2);
    }
}

[Pool(typeof(ConstructCardPool))]
public sealed class TonyWave() : CustomCardModel(1, CardType.Skill, CardRarity.Basic, TargetType.Self)
{
    public override string CustomPortraitPath => "res://PunchConstruct/art/tony_wave.ctex";
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(7, ValueProp.Move), new BlockVar(7, ValueProp.Move)];
    public override List<(string, string)> Localization => Loc.Card("东尼斩波", "下回合开始时，对一名随机敌人造成{Damage:diff()}点伤害，获得{Block:diff()}点[gold]格挡[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var power = await PowerCmd.Apply<DelayedWavePower>(context, Owner.Creature, 1, Owner.Creature, this);
        power?.Enqueue(this, play, DynamicVars.Damage.BaseValue, DynamicVars.Block.BaseValue);
    }
    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(2);
        DynamicVars.Block.UpgradeValueBy(2);
    }
}

[Pool(typeof(ConstructCardPool))]
public sealed class SmallShockwave() : CustomCardModel(1, CardType.Skill, CardRarity.Basic, TargetType.AllEnemies)
{
    public override string CustomPortraitPath => ModelDb.Card<Shockwave>().PortraitPath;
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Debuff", 1)];
    public override List<(string, string)> Localization => Loc.Card("震荡", "给予所有敌人{Debuff:diff()}层[gold]易伤[/gold]和{Debuff:diff()}层[gold]虚弱[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var enemies = CombatState!.HittableEnemies.ToArray();
        foreach (var enemy in enemies)
        {
            await PowerCmd.Apply<VulnerablePower>(context, enemy, DynamicVars["Debuff"].BaseValue, Owner.Creature, this);
            await PowerCmd.Apply<WeakPower>(context, enemy, DynamicVars["Debuff"].BaseValue, Owner.Creature, this);
        }
    }
    protected override void OnUpgrade() => DynamicVars["Debuff"].UpgradeValueBy(1);
}

[Pool(typeof(ConstructCardPool))]
public sealed class IronWaveForm() : CustomCardModel(3, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    public override string CustomPortraitPath => "res://PunchConstruct/art/iron_wave_form.ctex";
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Amount", 1)];
    public override List<(string, string)> Localization => Loc.Card("铁斩波形态", "每回合前{Amount:diff()}次造成伤害时，获得等同于未被格挡的伤害量的[gold]格挡[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await PowerCmd.Apply<IronWaveFormPower>(context, Owner.Creature, DynamicVars["Amount"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars["Amount"].UpgradeValueBy(1);
}

[Pool(typeof(ConstructCardPool))]
public sealed class FightMe() : CustomCardModel(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<DexterityPower>(), HoverTipFactory.FromPower<StrengthPower>()];
    public override string CustomPortraitPath => "res://PunchConstruct/art/fight_me.ctex";
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(6, ValueProp.Move), new DynamicVar("Dexterity", 2)];
    public override List<(string, string)> Localization => Loc.Card("与我一斗", "造成{Damage:diff()}点伤害2次。\n获得{Dexterity:diff()}点[gold]敏捷[/gold]。\n该敌人获得1点[gold]力量[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var enemy = play.Target!;
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play).Targeting(enemy).WithHitCount(2).Execute(context);
        await PowerCmd.Apply<DexterityPower>(context, Owner.Creature, DynamicVars["Dexterity"].BaseValue, Owner.Creature, this);
        if (enemy.IsAlive) await PowerCmd.Apply<StrengthPower>(context, enemy, 1, Owner.Creature, this);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Dexterity"].UpgradeValueBy(1);
    }
}
