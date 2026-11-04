using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

// The native Surrounded hook only turns for a card's original selected target.
// Tony's target is chosen later by AttackCommand. Let that command choose with
// its usual RNG and hit-count hooks, then turn before the actual damage command.
[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), [typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>),
    typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay)])]
internal static class DelayedWaveFacing
{
    private static readonly AsyncLocal<CardModel?> Active = new();
    private static readonly System.Reflection.MethodInfo UpdateDirection = AccessTools.Method(typeof(SurroundedPower), "UpdateDirection");

    internal static async Task ExecuteWave(PlayerChoiceContext context, CardModel card, CardPlay play, decimal damage, ICombatState combat)
    {
        var previous = Active.Value;
        Active.Value = card;
        try { await DamageCmd.Attack(damage).FromCard(card, play).TargetingRandomOpponents(combat).Execute(context); }
        finally { Active.Value = previous; }
    }
    private static bool Prefix(PlayerChoiceContext choiceContext, IEnumerable<Creature> targets, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource, CardPlay? cardPlay, ref Task<IEnumerable<DamageResult>> __result)
    {
        if (cardSource is not TonyWave || Active.Value != cardSource || dealer != cardSource.Owner.Creature ||
            dealer.GetPower<SurroundedPower>() is not { } surrounded) return true;
        var actual = targets.ToArray();
        if (actual.Length != 1 || !actual[0].HasPower<BackAttackLeftPower>() && !actual[0].HasPower<BackAttackRightPower>()) return true;
        __result = TurnAndDamage(choiceContext, actual, amount, props, dealer, cardSource, cardPlay, surrounded);
        return false;
    }
    private static async Task<IEnumerable<DamageResult>> TurnAndDamage(PlayerChoiceContext context, Creature[] targets, decimal amount,
        ValueProp props, Creature dealer, CardModel card, CardPlay? play, SurroundedPower surrounded)
    {
        var previous = Active.Value;
        // Prevent reentry and unrelated reaction attacks from inheriting this scope.
        Active.Value = null;
        try
        {
            var before = surrounded.Facing;
            await (Task)UpdateDirection.Invoke(surrounded, [targets[0]])!;
            Diagnostics.Observe(() => Diagnostics.Write("info", "delayed_wave.facing", new {
                owner = Diagnostics.Slot(card.Owner.NetId), card = card.Id.ToString(), before = before.ToString(),
                after = surrounded.Facing.ToString(), left = targets[0].HasPower<BackAttackLeftPower>(), right = targets[0].HasPower<BackAttackRightPower>() }));
            return await CreatureCmd.Damage(context, targets, amount, props, dealer, card, play);
        }
        finally { Active.Value = previous; }
    }
}
