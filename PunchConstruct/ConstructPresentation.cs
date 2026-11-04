using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace PunchConstruct;

// Presentation only: no game RNG, saved combat state or damage values change.
internal static class ConstructPresentation
{
    internal const string SingleSfx = "event:/sfx/enemy/enemy_attacks/punch_construct/punch_construct_attack_single";
    internal const string DoubleSfx = "event:/sfx/enemy/enemy_attacks/punch_construct/punch_construct_attack_double";
    internal const string GuardSfx = "event:/sfx/enemy/enemy_attacks/punch_construct/punch_construct_buff";
    private sealed class CombatSequence
    {
        internal ICombatState? Combat;
        internal int Singles;
    }
    private sealed class PlayPresentation
    {
        internal bool GuardPlayed;
        internal bool AttackPlayed;
    }
    private static readonly ConditionalWeakTable<Creature, CombatSequence> Sequences = new();
    private static readonly ConditionalWeakTable<CardPlay, PlayPresentation> Plays = new();
    private static readonly AsyncLocal<CardPlay?> Active = new();
    internal static readonly MethodInfo NativeOnPlay = AccessTools.Method(typeof(CardModel), "OnPlay");
    private static readonly Func<CardModel, PlayerChoiceContext, CardPlay, Task> PlayNative =
        NativeOnPlay.CreateDelegate<Func<CardModel, PlayerChoiceContext, CardPlay, Task>>();

    internal static Task OnPlay(CardModel card, PlayerChoiceContext context, CardPlay play) =>
        card.Owner.Character is ConstructCharacter ? PlayConstruct(card, context, play) : PlayNative(card, context, play);

    private static async Task PlayConstruct(CardModel card, PlayerChoiceContext context, CardPlay play)
    {
        var previous = Active.Value;
        Active.Value = play;
        try
        {
            // Start the defensive pose without adding another wait before card effects.
            if (card.Type is CardType.Skill or CardType.Power)
                await CreatureCmd.TriggerAnim(card.Owner.Creature, "Cast", 0);
            await PlayNative(card, context, play);
        }
        finally { Active.Value = previous; }
    }

    internal static string NextAttack(Creature creature, int hits)
    {
        var sequence = Sequences.GetValue(creature, _ => new CombatSequence());
        if (!ReferenceEquals(sequence.Combat, creature.CombatState))
        {
            sequence.Combat = creature.CombatState;
            sequence.Singles = 0;
        }
        if (hits > 1 || sequence.Singles >= 2)
        {
            sequence.Singles = 0;
            return "DoubleAttack";
        }
        sequence.Singles++;
        return "Attack";
    }

    internal static bool SuppressDuplicateGuard(Creature creature, ref string trigger)
    {
        var play = Active.Value;
        if (play?.Player.Creature != creature || play.Card.Type is not (CardType.Skill or CardType.Power) ||
            trigger is not ("Cast" or "PowerUp")) return false;
        var presentation = Plays.GetValue(play, _ => new PlayPresentation());
        if (presentation.GuardPlayed) return true;
        presentation.GuardPlayed = true;
        trigger = "Cast";
        return false;
    }

    internal static bool ClaimAttack(CardPlay? play)
    {
        if (play == null) return true;
        var presentation = Plays.GetValue(play, _ => new PlayPresentation());
        if (presentation.AttackPlayed) return false;
        presentation.AttackPlayed = true;
        return true;
    }
}

// Wrap the virtual card-effect call, not the whole play queue. The async scope
// follows nested/auto/replayed effects and is restored even if an effect fails.
[HarmonyPatch]
internal static class ConstructCardPresentation
{
    private static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.Method(typeof(CardModel), nameof(CardModel.OnPlayWrapper))
            .GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType, "MoveNext");
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
    {
        int changed = 0;
        foreach (var instruction in source)
        {
            if (instruction.Calls(ConstructPresentation.NativeOnPlay))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(ConstructPresentation), nameof(ConstructPresentation.OnPlay));
                changed++;
            }
            yield return instruction;
        }
        if (changed != 1) throw new InvalidOperationException("Construct card animation hook expected exactly one native OnPlay call; found " + changed);
    }
}

[HarmonyPatch(typeof(AttackCommand), nameof(AttackCommand.Execute))]
internal static class ConstructAttackPresentation
{
    private static void Prefix(AttackCommand __instance, int ____hitCount, ref string? ____attackerSfx, ref string? ____tmpAttackerSfx)
    {
        if (__instance.ModelSource is not CardModel card || card.Owner.Character is not ConstructCharacter ||
            __instance.Attacker != card.Owner.Creature) return; // Osty and other characters keep their own presentation.
        ____attackerSfx = null;
        ____tmpAttackerSfx = null;
        if (card.Type != CardType.Attack || !ConstructPresentation.ClaimAttack(__instance.CardPlay))
        {
            __instance.WithNoAttackerAnim();
            return;
        }
        string trigger = ConstructPresentation.NextAttack(__instance.Attacker, ____hitCount);
        __instance.WithAttackerAnim(trigger, trigger == "DoubleAttack" ? 0.2f : 0.25f).OnlyPlayAnimOnce();
        Diagnostics.Observe(() => Diagnostics.Write("info", "character.animation.attack", new {
            owner = Diagnostics.Slot(card.Owner.NetId), card = card.Id.ToString(), hits = ____hitCount, trigger }));
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.TriggerAnim))]
internal static class ConstructAnimationSound
{
    private static bool Prefix(Creature creature, ref string triggerName, ref Task __result)
    {
        if (creature.Player?.Character is not ConstructCharacter) return true;
        if (ConstructPresentation.SuppressDuplicateGuard(creature, ref triggerName))
        {
            __result = Task.CompletedTask;
            return false;
        }
        if (triggerName == "DoubleAttack" && !creature.IsDead && creature.GetCreatureNode() != null)
            SfxCmd.Play(ConstructPresentation.DoubleSfx);
        return true;
    }
}
