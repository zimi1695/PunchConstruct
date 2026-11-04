using BaseLib.Abstracts;
using BaseLib.Utils;
using BaseLib.Utils.Attributes;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-SPINNING_SCREW_DRAW")]
public sealed class SpinningScrew : CustomRelicModel
{
    protected override string IconBaseName => "spinning_screw";
    public override RelicRarity Rarity => RelicRarity.Common;
    public override string PackedIconPath => "res://PunchConstruct/art/spinning_screw.ctex";
    protected override string PackedIconOutlinePath => "res://PunchConstruct/art/spinning_screw_outline.ctex";
    protected override string BigIconPath => "res://PunchConstruct/art/spinning_screw_big.ctex";
    public override List<(string, string)> Localization => new RelicLoc("自旋螺丝",
        "回合开始时，若上回合造成过伤害且未受到伤害，额外抽1张牌。", "越转越顺。");
    public override Task BeforeCombatStart() { Reset(); return Task.CompletedTask; }
    public override Task AfterCombatEnd(CombatRoom room) { Reset(); return Task.CompletedTask; }
    private void Reset()
    {
        CombatRelicState.ScrewDamageGiven[this] = false;
        CombatRelicState.ScrewDamageTaken[this] = false;
        CombatRelicState.ScrewDrawReady[this] = false;
    }
    public override Task AfterDamageGiven(PlayerChoiceContext context, Creature? dealer, DamageResult result, ValueProp props, Creature? target, CardModel? card)
    {
        if (dealer == Owner.Creature && target is { IsEnemy: true } && result.UnblockedDamage > 0)
            CombatRelicState.ScrewDamageGiven[this] = true;
        return Task.CompletedTask;
    }
    public override Task AfterDamageReceived(PlayerChoiceContext context, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? card)
    {
        if (target == Owner.Creature && result.UnblockedDamage > 0)
            CombatRelicState.ScrewDamageTaken[this] = true;
        return Task.CompletedTask;
    }
    public override Task BeforeSideTurnStart(PlayerChoiceContext context, CombatSide side, IReadOnlyList<Creature> creatures, ICombatState combatState)
    {
        if (side != CombatSide.Player || !creatures.Contains(Owner.Creature)) return Task.CompletedTask;
        bool dealt = CombatRelicState.ScrewDamageGiven[this], hurt = CombatRelicState.ScrewDamageTaken[this];
        Reset();
        CombatRelicState.ScrewDrawReady[this] = Owner.Creature.IsAlive && dealt && !hurt;
        Diagnostics.Write("info", "relic.screw.round_evaluated", new { owner = Owner.NetId, dealt, hurt, draw = dealt && !hurt });
        return Task.CompletedTask;
    }
    public override decimal ModifyHandDraw(Player player, decimal count)
        => player == Owner && CombatRelicState.ScrewDrawReady[this] ? count + 1 : count;
    public override Task AfterModifyingHandDraw()
    {
        CombatRelicState.ScrewDrawReady[this] = false;
        Flash();
        return Task.CompletedTask;
    }
}
