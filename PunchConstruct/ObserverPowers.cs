using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

public abstract class ObserverPower : CustomPowerModel
{
    public override PowerStackType StackType => PowerStackType.Counter;
    public override string CustomPackedIconPath => "res://PunchConstruct/observer/" + GetType().Name + ".ctex";
    public override string CustomBigIconPath => CustomPackedIconPath;
    public override string CustomBigBetaIconPath => CustomPackedIconPath;
}

public sealed class ObserverMantra : ObserverPower
{
    private bool _resolving;
    public override PowerType Type => PowerType.Buff;
    public override List<(string, string)> Localization => new PowerLoc("真言", "达到10点时，消耗10点真言，进入[gold]神格[/gold]。", "达到10点时，消耗10点真言，进入[gold]神格[/gold]。当前{Amount}点。");
    public override async Task AfterPowerAmountChanged(PlayerChoiceContext context, PowerModel power, decimal amount, Creature? applier, CardModel? cardSource)
    {
        if (power != this || _resolving || Amount < 10 || Owner.Player == null) return;
        _resolving = true;
        try
        {
            ObserverDivinityVfx.Mantra(Owner);
            await PowerCmd.ModifyAmount(context, this, -10, applier, cardSource, silent: true);
            if (Owner.GetPower<ObserverDivinity>() == null)
                await PowerCmd.Apply<ObserverDivinity>(context, Owner, 1, applier, cardSource);
        }
        finally { _resolving = false; }
    }
}

public sealed class ObserverDivinity : ObserverPower
{
    protected override bool IsVisibleInternal => false;
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.None;
    public override List<(string, string)> Localization => new PowerLoc("神格", "进入时获得3点能量。攻击造成三倍伤害。回合结束时退出神格。", "进入时获得3点能量。攻击造成三倍伤害。回合结束时退出神格。");
    public override async Task AfterApplied(Creature? applier, CardModel? cardSource)
    {
        ObserverDivinityVfx.Enter(Owner);
        if (Owner.Player is { } player) await PlayerCmd.GainEnergy(3, player);
    }
    public override Task AfterRemoved(Creature oldOwner)
    {
        ObserverDivinityVfx.Remove(oldOwner);
        return Task.CompletedTask;
    }
    public override Task AfterCombatEnd(MegaCrit.Sts2.Core.Rooms.CombatRoom room)
    {
        ObserverDivinityVfx.Remove(Owner);
        return Task.CompletedTask;
    }
    public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
        => dealer == Owner && props.HasFlag(ValueProp.Move) && !props.HasFlag(ValueProp.Unpowered) ? 3 : 1;
    public override async Task AfterSideTurnEnd(PlayerChoiceContext context, CombatSide side, IEnumerable<Creature> participants)
    {
        if (side == Owner.Side) await PowerCmd.Remove(this);
    }
}

public sealed class ObserverEnergyDown : ObserverPower
{
    public override PowerType Type => PowerType.Debuff;
    public override List<(string, string)> Localization => new PowerLoc("能量下降", "每回合开始时少获得{Amount}点能量。", "每回合开始时少获得{Amount}点能量。");
    public override async Task AfterEnergyReset(Player player)
    {
        if (player.Creature == Owner) await PlayerCmd.LoseEnergy(Amount, player);
    }
}

public sealed class ObserverBlockReturn : ObserverPower
{
    public override PowerType Type => PowerType.Debuff;
    public override List<(string, string)> Localization => new PowerLoc("以手拒之", "受到攻击时，攻击者获得{Amount}点格挡。", "受到攻击时，攻击者获得{Amount}点格挡。");
    public override async Task AfterDamageGiven(PlayerChoiceContext context, Creature? dealer, DamageResult result, ValueProp props, Creature? target, CardModel? cardSource)
    {
        if (target != Owner || dealer == null || dealer == Owner || cardSource?.Type != CardType.Attack ||
            !props.HasFlag(ValueProp.Move) || props.HasFlag(ValueProp.Unpowered) || result.TotalDamage <= 0) return;
        var receiver = dealer.Player?.Creature ?? dealer.PetOwner?.Creature;
        if (receiver is { IsAlive: true }) await CreatureCmd.GainBlock(receiver, Amount, ValueProp.Unpowered, null);
    }
}

public sealed class ObserverColdObservationPower : ObserverPower
{
    private CardPlay? _activePlay;
    public override PowerType Type => PowerType.Buff;
    public override List<(string, string)> Localization => new PowerLoc("冷眼旁观", "接下来的{Amount}张攻击牌，每段伤害获得等量格挡。", "接下来的{Amount}张攻击牌，每段伤害获得等量格挡。");
    public override Task BeforeCardPlayed(CardPlay play)
    {
        if (play.Player.Creature == Owner && play.Card.Type == CardType.Attack && Amount > 0) _activePlay = play;
        return Task.CompletedTask;
    }
    public override async Task AfterDamageGiven(PlayerChoiceContext context, Creature? dealer, DamageResult result, ValueProp props, Creature? target, CardModel? cardSource)
    {
        if (_activePlay?.Card != cardSource || dealer != Owner || !props.HasFlag(ValueProp.Move) || props.HasFlag(ValueProp.Unpowered) || result.TotalDamage <= 0) return;
        await CreatureCmd.GainBlock(Owner, result.TotalDamage, ValueProp.Unpowered, null);
    }
    public override async Task AfterCardPlayedLate(PlayerChoiceContext context, CardPlay play)
    {
        if (play != _activePlay) return;
        _activePlay = null;
        await PowerCmd.ModifyAmount(context, this, -1, Owner, play.Card);
    }
}
