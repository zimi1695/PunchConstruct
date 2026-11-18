using BaseLib.Abstracts;
using BaseLib.Utils;
using BaseLib.Utils.Attributes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

// Keep the original kill/heal/gold relic's saved ID while changing its name/art.
// The new draw-based screw has a separate stable ID and cannot collide on load.
[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-SPINNING_SCREW")]
public sealed class OpenEndWrench : CustomRelicModel
{
    protected override string IconBaseName => "open_end_wrench";
    public override RelicRarity Rarity => RelicRarity.Common;
    public override string PackedIconPath => "res://PunchConstruct/art/open_end_wrench.ctex";
    protected override string PackedIconOutlinePath => "res://PunchConstruct/art/open_end_wrench_outline.ctex";
    protected override string BigIconPath => "res://PunchConstruct/art/open_end_wrench_big.ctex";
    public override List<(string, string)> Localization => Loc.Relic("开口扳手",
        "每击败一个敌人，回复1点生命值，获得5[gold]金币[/gold]。", "我说胧光怪刷钱特别多");
    public override async Task AfterDeath(PlayerChoiceContext context, Creature target, CardModel? source, bool wasRemovalPrevented, float deathAnimLength)
    {
        if (!target.IsEnemy || target.CombatState != Owner.Creature.CombatState || wasRemovalPrevented || !Owner.Creature.IsAlive) return;
        Flash();
        await CreatureCmd.Heal(Owner.Creature, 1);
        await PlayerCmd.GainGold(5, Owner);
    }
}

[Pool(typeof(ConstructRelicPool))]
public sealed class SpareKnuckles : CustomRelicModel
{
    protected override string IconBaseName => "spare_knuckles";
    public override RelicRarity Rarity => RelicRarity.Uncommon;
    public override string PackedIconPath => "res://PunchConstruct/art/spare_knuckles.ctex";
    protected override string PackedIconOutlinePath => "res://PunchConstruct/art/spare_knuckles_outline.ctex";
    protected override string BigIconPath => "res://PunchConstruct/art/spare_knuckles_big.ctex";
    public override List<(string, string)> Localization => Loc.Relic("备用指节",
        "每击败一个敌人，获得16点[gold]格挡[/gold]。", "东尼严父");
    public override async Task AfterDeath(PlayerChoiceContext context, Creature target, CardModel? source, bool wasRemovalPrevented, float deathAnimLength)
    {
        if (!target.IsEnemy || target.CombatState != Owner.Creature.CombatState || wasRemovalPrevented || !Owner.Creature.IsAlive) return;
        Flash();
        await CreatureCmd.GainBlock(Owner.Creature, 16, ValueProp.Unpowered, null);
    }
}

[Pool(typeof(ConstructRelicPool))]
public sealed class ConstructClockworkSouvenir : CustomRelicModel
{
    protected override string IconBaseName => "clockwork_souvenir";
    public override RelicRarity Rarity => RelicRarity.Shop;
    public override string PackedIconPath => "res://PunchConstruct/art/clockwork_souvenir.ctex";
    protected override string PackedIconOutlinePath => "res://PunchConstruct/art/clockwork_souvenir_outline.ctex";
    protected override string BigIconPath => "res://PunchConstruct/art/clockwork_souvenir_big.ctex";
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<ArtifactPower>()];
    public override List<(string, string)> Localization => Loc.Relic("齿轮工艺品",
        "每场战斗开始时，获得1层[gold]人工制品[/gold]。", "发条还在走，滴答，滴答");
    public override async Task BeforeCombatStart()
    {
        Flash();
        await PowerCmd.Apply<ArtifactPower>(new ThrowingPlayerChoiceContext(), Owner.Creature, 1, Owner.Creature, null);
    }
}

[Pool(typeof(ConstructRelicPool))]
public sealed class EmergencyOilCan : CustomRelicModel
{
    protected override string IconBaseName => "emergency_oil_can";
    public override RelicRarity Rarity => RelicRarity.Uncommon;
    public override string PackedIconPath => "res://PunchConstruct/art/emergency_oil_can.ctex";
    protected override string PackedIconOutlinePath => "res://PunchConstruct/art/emergency_oil_can_outline.ctex";
    protected override string BigIconPath => "res://PunchConstruct/art/emergency_oil_can_big.ctex";
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<VulnerablePower>(), HoverTipFactory.FromPower<WeakPower>()];
    public override List<(string, string)> Localization => Loc.Relic("应急油壶",
        "回合结束时，若有未花费的{energyPrefix:energyIcons(1)}，给予所有敌人1层[gold]易伤[/gold]和1层[gold]虚弱[/gold]。", "我说配佩尔之泪等于震荡波特别强");
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext context, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side != CombatSide.Player || !participants.Contains(Owner.Creature) || !Owner.Creature.IsAlive || Owner.PlayerCombatState?.Energy is not > 0) return;
        var enemies = Owner.Creature.CombatState?.HittableEnemies.Where(e => e.IsAlive).ToArray() ?? [];
        if (enemies.Length == 0) return;
        Flash();
        Diagnostics.Write("info", "relic.oiler.debuff", new { owner = Owner.NetId, energy = Owner.PlayerCombatState!.Energy, targets = enemies.Select(e => e.CombatId).ToArray() });
        foreach (var target in enemies)
        {
            await PowerCmd.Apply<VulnerablePower>(context, target, 1, Owner.Creature, null);
            await PowerCmd.Apply<WeakPower>(context, target, 1, Owner.Creature, null);
        }
    }
}
