using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace PunchConstruct;

// One visible brass-nut indicator covers both queued and active Strength.
// The internal expiry helper below stays hidden.
public abstract class GuardStrengthPower : CustomPowerModel
{
    public override string CustomPackedIconPath => "res://PunchConstruct/art/power_brass_nut.ctex";
    public override string CustomBigIconPath => "res://PunchConstruct/art/power_brass_nut_big.ctex";
    public override string CustomBigBetaIconPath => CustomBigIconPath;
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
}

public sealed class GuardStrengthNextTurnPower : GuardStrengthPower
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Active", 0)];
    internal int ActiveAmount => DynamicVars["Active"].IntValue;
    internal int PendingAmount => Amount - ActiveAmount;
    public override List<(string, string)> Localization => [("title", "临时力量"), ("description", "{Status}")];
    public override LocString Description
    {
        get
        {
            var text = base.Description;
            var parts = new List<string>();
            if (ActiveAmount > 0) parts.Add($"本回合已获得{ActiveAmount}点[gold]临时力量[/gold]，回合结束时失去。");
            if (PendingAmount > 0) parts.Add($"下回合获得{PendingAmount}点[gold]临时力量[/gold]。");
            text.Add("Status", string.Join("\n", parts));
            return text;
        }
    }
    public override async Task BeforeSideTurnStart(PlayerChoiceContext context, CombatSide side, IReadOnlyList<Creature> creatures, ICombatState combatState)
    {
        if (!creatures.Contains(Owner) || PendingAmount <= 0) return;
        int amount = PendingAmount;
        DynamicVars["Active"].BaseValue += amount;
        await PowerCmd.Apply<GuardTemporaryStrengthPower>(context, Owner, amount, Owner, null);
        InvokeDisplayAmountChanged();
        Diagnostics.Write("info", "guard.strength.activated", new { owner = Owner.Player?.NetId.ToString(), amount });
    }
    public override async Task AfterSideTurnEnd(PlayerChoiceContext context, CombatSide side, IEnumerable<Creature> participants)
    {
        if (!participants.Contains(Owner) || ActiveAmount <= 0) return;
        int expired = ActiveAmount;
        DynamicVars["Active"].BaseValue = 0;
        await PowerCmd.ModifyAmount(context, this, -expired, Owner, null);
    }
}

// BaseLib's native-style temporary-power wrapper grants Strength, tracks its
// own contribution and subtracts only that contribution at the owner's turn end.
public sealed class GuardTemporaryStrengthPower : CustomTemporaryPowerModel
{
    protected override bool IsVisibleInternal => false;
    public override string CustomPackedIconPath => "res://PunchConstruct/ui/empty_power_icon.tres";
    public override string CustomBigIconPath => CustomPackedIconPath;
    public override string CustomBigBetaIconPath => CustomPackedIconPath;
    public override AbstractModel OriginModel => ModelDb.Relic<AncientGuardStance>();
    public override PowerModel InternallyAppliedPower => ModelDb.Power<StrengthPower>();
    protected override Func<PlayerChoiceContext, Creature, decimal, Creature?, CardModel?, bool, Task> ApplyPowerFunc
        => async (context, target, amount, applier, card, silent) => { await PowerCmd.Apply<StrengthPower>(context, target, amount, applier, card, silent); };
    public override List<(string, string)> Localization => new PowerLoc("临时力量", "本回合结束时失去{Amount}点[gold]力量[/gold]。", "本回合结束时失去{Amount}点[gold]力量[/gold]。");
}
