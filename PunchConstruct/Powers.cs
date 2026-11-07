using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

public sealed class DelayedWavePower : CustomPowerModel
{
    private List<PendingWave> _pending = [];
    private record PendingWave(CardModel Card, CardPlay Play, decimal Damage, decimal Block);
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override string CustomPackedIconPath => ConstructPowerIcons.Packed(GetType());
    public override string CustomBigIconPath => ConstructPowerIcons.Big(GetType());
    public override string CustomBigBetaIconPath => CustomBigIconPath;
    public override List<(string, string)> Localization => [("title", Loc.Pick("东尼斩波")), ("description", Loc.Pick("下回合开始时，依次结算{Amount}段东尼斩波。每段对随机敌人造成伤害，获得格挡：\n{Waves}"))];
    public override LocString Description
    {
        get
        {
            var text = base.Description;
            text.Add("Waves", string.Join("；", _pending.Select(w => $"{w.Damage}伤害、{w.Block}格挡")));
            return text;
        }
    }
    public void Enqueue(CardModel card, CardPlay play, decimal damage, decimal block)
    {
        _pending.Add(new(card, play, damage, block));
        InvokeDisplayAmountChanged();
    }
    protected override void DeepCloneFields() { base.DeepCloneFields(); _pending = [.._pending]; }
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext context, Player player)
    {
        if (player.Creature != Owner) return;
        var owner = Owner;
        var combat = owner.CombatState!;
        var waves = _pending.ToArray();
        _pending.Clear();
        await PowerCmd.Remove(this);
        foreach (var wave in waves)
        {
            if (!combat.HittableEnemies.Any()) break;
            await DelayedWaveFacing.ExecuteWave(context, wave.Card, wave.Play, wave.Damage, combat);
            await CreatureCmd.GainBlock(owner, wave.Block, ValueProp.Move, wave.Play);
        }
    }
}

public sealed class IronWaveFormPower : CustomPowerModel
{
    private int _triggeredThisTurn;
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override string CustomPackedIconPath => ConstructPowerIcons.Packed(GetType());
    public override string CustomBigIconPath => ConstructPowerIcons.Big(GetType());
    public override string CustomBigBetaIconPath => CustomBigIconPath;
    public override List<(string, string)> Localization => [("title", Loc.Pick("铁斩波形态")), ("description", Loc.Pick("每回合前{Amount}次造成伤害时，获得等同于未被格挡的伤害量的[gold]格挡[/gold]。本回合还可触发{Remaining}次。"))];
    public override LocString Description
    {
        get { var text = base.Description; text.Add("Remaining", Math.Max(0, Amount - _triggeredThisTurn)); return text; }
    }
    public override Task AfterPlayerTurnStart(PlayerChoiceContext context, Player player)
    {
        if (player.Creature == Owner) _triggeredThisTurn = 0;
        return Task.CompletedTask;
    }
    public override async Task AfterDamageGiven(PlayerChoiceContext context, Creature? dealer, DamageResult result, ValueProp props, Creature? target, CardModel? card)
    {
        if (dealer != Owner || _triggeredThisTurn >= Amount) return;
        _triggeredThisTurn++;
        if (result.UnblockedDamage > 0)
            await CreatureCmd.GainBlock(Owner, result.UnblockedDamage, ValueProp.Unpowered, null);
    }
}
