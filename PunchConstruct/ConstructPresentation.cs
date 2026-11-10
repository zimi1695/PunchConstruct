using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;

namespace PunchConstruct;

// Card presentation that survives an arbitrary set of third-party patches:
// nothing here inspects or rewrites native IL. The construct's skill/power pose
// and attack triggers come from the animator and CreatureCmd.TriggerAnim, so a
// mod that rewrites CardModel.OnPlayWrapper cannot disable this module.
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
        internal bool AttackPlayed;
    }
    private static readonly ConditionalWeakTable<Creature, CombatSequence> Sequences = new();
    private static readonly ConditionalWeakTable<CardPlay, PlayPresentation> Plays = new();

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

    internal static bool ClaimAttack(CardPlay? play)
    {
        if (play == null) return true;
        var presentation = Plays.GetValue(play, _ => new PlayPresentation());
        if (presentation.AttackPlayed) return false;
        presentation.AttackPlayed = true;
        return true;
    }
}
