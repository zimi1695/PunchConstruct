using BaseLib.Abstracts;
using BaseLib.Utils;
using BaseLib.Utils.Attributes;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

// The three Fool designs below are re-authored with the 0.113 values the
// construct shipped with. 0.114 changed their numbers; keeping native models
// would silently import a balance pass aimed at the Fool character. Portraits,
// tags and melody VFX stay native, so cross-card Fool behaviour is unchanged.
[Pool(typeof(ConstructCardPool))]
[CustomID("PUNCHCONSTRUCT-CHO_BARBS")]
public sealed class ConstructChoBarbs() : CustomCardModel(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    // Acquisition stays in ConstructCardPool via [Pool]; ownership and visuals
    // stay Fool's, matching the seven native Fool cards this character imports.
    public override CardPoolModel Pool => ModelDb.CardPool<FoolCardPool>();
    public override bool GainsBlock => true;
    public override string CustomPortraitPath => ModelDb.Card<ChoBarbs>().PortraitPath;
    protected override HashSet<CardTag> CanonicalTags => new() { CardTag.Cho };
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<ThornsPower>()];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(7m, ValueProp.Move), new PowerVar<ChoBarbsPower>(3m)];
    protected override IEnumerable<string> ExtraRunAssetPaths => NChoVfx.AssetPaths;
    public override List<(string, string)> Localization => Loc.Card("啾 尖刺",
        "获得{Block:diff()}点[gold]格挡[/gold]。\n在本回合获得{ChoBarbsPower:diff()}点[gold]荆棘[/gold]。");

    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await MelodyCmd.PlayMelodyVfx(Owner.Creature, CardTag.Cho);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play);
        await PowerCmd.Apply<ChoBarbsPower>(context, Owner.Creature,
            DynamicVars["ChoBarbsPower"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(2m);
        DynamicVars["ChoBarbsPower"].UpgradeValueBy(1m);
    }
}

[Pool(typeof(ConstructCardPool))]
[CustomID("PUNCHCONSTRUCT-CHO_READY")]
public sealed class ConstructChoReady() : CustomCardModel(1, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<FoolCardPool>();
    public override bool GainsBlock => true;
    public override string CustomPortraitPath => ModelDb.Card<ChoReady>().PortraitPath;
    protected override HashSet<CardTag> CanonicalTags => new() { CardTag.Cho };
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<AimPower>()];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new BlockVar(8m, ValueProp.Move), new PowerVar<AimPower>(3m)];
    protected override IEnumerable<string> ExtraRunAssetPaths => NChoVfx.AssetPaths;
    public override List<(string, string)> Localization => Loc.Card("啾 就绪",
        "获得{Block:diff()}点[gold]格挡[/gold]。\n获得{AimPower:diff()}点[gold]瞄准[/gold]。");

    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await MelodyCmd.PlayMelodyVfx(Owner.Creature, CardTag.Cho);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play);
        await PowerCmd.Apply<AimPower>(context, Owner.Creature,
            DynamicVars["AimPower"].BaseValue, Owner.Creature, this);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Block.UpgradeValueBy(3m);
        DynamicVars["AimPower"].UpgradeValueBy(2m);
    }
}

[Pool(typeof(ConstructCardPool))]
[CustomID("PUNCHCONSTRUCT-WAH_CHO_KI")]
public sealed class ConstructWahChoKi() : CustomCardModel(1, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
{
    public override CardPoolModel Pool => ModelDb.CardPool<FoolCardPool>();
    public override bool GainsBlock => true;
    public override string CustomPortraitPath => ModelDb.Card<WahChoKi>().PortraitPath;
    protected override HashSet<CardTag> CanonicalTags => new() { CardTag.Wah, CardTag.Cho, CardTag.Ki };
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DamageVar(9m, ValueProp.Move), new BlockVar(9m, ValueProp.Move), new CardsVar(1)];
    protected override IEnumerable<string> ExtraRunAssetPaths =>
        NWahVfx.AssetPaths.Concat(NChoVfx.AssetPaths).Concat(NKiVfx.AssetPaths);
    public override List<(string, string)> Localization => Loc.Card("哇 啾 叽",
        "获得{Block:diff()}点[gold]格挡[/gold]。\n造成{Damage:diff()}点伤害。\n抽{Cards:diff()}张牌。");

    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        ArgumentNullException.ThrowIfNull(play.Target, "play.Target");
        await MelodyCmd.PlayMelodyVfx(Owner.Creature, CardTag.Wah);
        await MelodyCmd.PlayMelodyVfx(Owner.Creature, CardTag.Cho);
        await MelodyCmd.PlayMelodyVfx(Owner.Creature, CardTag.Ki);
        // 0.113 gains block before attacking; 0.114 swapped the order along with
        // the cost/damage nerf. Keep the sequence this character shipped with.
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play).Targeting(play.Target)
            .WithHitFx("vfx/vfx_attack_blunt").Execute(context);
        await CardPileCmd.Draw(context, DynamicVars.Cards.BaseValue, Owner);
    }

    protected override void OnUpgrade()
    {
        DynamicVars.Damage.UpgradeValueBy(3m);
        DynamicVars.Block.UpgradeValueBy(3m);
        DynamicVars.Cards.UpgradeValueBy(1m);
    }
}
