using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

[Pool(typeof(ConstructRelicPool))]
public sealed class GuardStance : CustomRelicModel
{
    private int NextAmount { get => CombatRelicState.GuardNext[this]; set => CombatRelicState.GuardNext[this] = value; }
    public override int DisplayAmount => NextAmount;
    public override Task BeforeCombatStart() { ResetGrowth(); return Task.CompletedTask; }
    public override Task AfterCombatEnd(MegaCrit.Sts2.Core.Rooms.CombatRoom room) { ResetGrowth(); return Task.CompletedTask; }
    private void ResetGrowth() { NextAmount = 1; InvokeDisplayAmountChanged(); }
    protected override string IconBaseName => "anchor";
    public override RelicRarity Rarity => RelicRarity.Starter;
    public override RelicModel GetUpgradeReplacement() => ModelDb.Relic<AncientGuardStance>();
    public override string PackedIconPath => "res://PunchConstruct/art/guard_stance.ctex";
    protected override string PackedIconOutlinePath => "res://PunchConstruct/art/guard_stance_outline.ctex";
    protected override string BigIconPath => "res://PunchConstruct/art/guard_stance_big.ctex";
    public override List<(string, string)> Localization => new RelicLoc("架势", "每次完全格挡伤害，获得1点[gold]活力[/gold]。此效果本场战斗中每次触发后+1。", "站稳，然后出拳。");
    public override async Task AfterDamageReceived(PlayerChoiceContext context, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? card)
    {
        if (target != Owner.Creature || !result.WasFullyBlocked) return;
        int amount = NextAmount++;
        InvokeDisplayAmountChanged();
        Flash();
        await GuardStancePowerGain.Apply<VigorPower>(context, Owner.Creature, amount);
    }
}
