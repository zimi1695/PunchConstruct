using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task FacingChecks()
    {
        var run = await ReliabilityRun("CRABFACING");
        var player = run.Players.Single();
        await RunManager.Instance.EnterRoomDebug(RoomType.Boss, model: ModelDb.Encounter<KaiserCrabBoss>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled,
            "Direct Kaiser Crab boss fixture ready");
        var state = (CombatState)player.Creature.CombatState!;
        var left = state.HittableEnemies.Single(c => c.HasPower<BackAttackLeftPower>());
        var right = state.HittableEnemies.Single(c => c.HasPower<BackAttackRightPower>());
        foreach (var enemy in new[] { left, right }) await CreatureCmd.SetMaxAndCurrentHp(enemy, 1000);
        var surrounded = player.Creature.GetPower<SurroundedPower>()!;
        var actor = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
        bool LooksRight() => actor.Body.GlobalTransform.Determinant() < 0;
        var initialPosition = actor.Body.GlobalPosition;
        float size = actor.Body.Scale.Length();
        Check(surrounded.Facing == SurroundedPower.Direction.Right && LooksRight(), "Crab encounter starts with matching logical and visible right facing");
        int step = 0;
        foreach (var target in new[] { left, right, left, left, right })
        {
            await Play<SmallWave>(player, target);
            bool expectRight = target == right;
            Check(surrounded.Facing == (expectRight ? SurroundedPower.Direction.Right : SurroundedPower.Direction.Left) && LooksRight() == expectRight,
                "Actual attack turns toward its selected crab claw: " + step);
            Check(Math.Abs(actor.Body.Scale.Length() - size) < 0.001f && actor.Body.GlobalPosition.DistanceTo(initialPosition) < 0.1f,
                "Turning keeps body size and position: " + step);
            decimal fromLeft = surrounded.ModifyDamageMultiplicative(player.Creature, 10, ValueProp.Unpowered, left, null, null);
            decimal fromRight = surrounded.ModifyDamageMultiplicative(player.Creature, 10, ValueProp.Unpowered, right, null, null);
            Check(fromLeft == (expectRight ? 1.5m : 1m) && fromRight == (expectRight ? 1m : 1.5m),
                "Native back-attack multipliers agree with the visible facing: " + step);
            await Capture("qa-crab-facing-" + step);
            step++;
        }
        await Play<SmallShockwave>(player);
        Check(surrounded.Facing == SurroundedPower.Direction.Right && LooksRight(), "An all-enemy skill does not arbitrarily reverse facing");
        await DelayedFacingChecks(run, player, left, right);
        await Play<SmallWave>(player, left);
        actor.SetAnimationTrigger("Dead");
        bool deathKeptLeft = true;
        for (int i = 0; i < 45; i++) { await Frames(1); deathKeptLeft &= !LooksRight(); }
        Check(deathKeptLeft, "Death animation preserves the left-facing crab pose instead of flipping");
        await NGame.Instance!.ReturnToMainMenu();
        SaveManager.Instance.DeleteCurrentRun();
    }

    private static async Task DelayedFacingChecks(RunState run, MegaCrit.Sts2.Core.Entities.Players.Player player, Creature left, Creature right)
    {
        var targets = player.Creature.CombatState!.GetOpponentsOf(player.Creature).Where(c => c.IsHittable).ToArray();
        await RelicCmd.Obtain<ConstructDart>(player);
        var saved = player.Deck.Cards.OfType<TonyWave>().Single().ToSerializable();
        Check(CardModel.FromSerializable(saved).Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Skill,
            "Tony Wave keeps its existing save ID and reconstructs as a skill");
        int scenario = 0;
        foreach (bool[] upgrades in new[] { new[] { false }, new[] { true }, new[] { false, true }, new[] { true, false } })
        {
            var desired = upgrades.Select((_, i) => (scenario + i) % 2 == 0 ? left : right).ToArray();
            Rng? forecast = null;
            for (ulong seed = 1; seed < 10000; seed++)
            {
                var candidate = new Rng(seed);
                if (!desired.All(t => candidate.NextItem(targets) == t)) continue;
                run.Rng.CombatTargets.LoadFromSerializable(new Rng(seed).ToSerializable());
                forecast = candidate;
                break;
            }
            if (forecast == null) throw new InvalidOperationException("Cannot prepare deterministic crab targets");
            var facingBefore = player.Creature.GetPower<SurroundedPower>()!.Facing;
            int dexBefore = player.Creature.GetPowerAmount<DexterityPower>();
            foreach (bool upgraded in upgrades)
            {
                var card = await Play<TonyWave>(player, upgraded: upgraded);
                Check(card.Type == MegaCrit.Sts2.Core.Entities.Cards.CardType.Skill, "Normal/upgraded Tony Wave is a skill");
            }
            Check(player.Creature.GetPowerAmount<DexterityPower>() == dexBefore, "Playing Tony Wave does not trigger attack-only dart Dexterity: " + scenario);
            Check(player.Creature.GetPower<SurroundedPower>()!.Facing == facingBefore,
                "Queued Tony Wave does not turn before its delayed target is chosen: " + scenario);
            DelayedFacingProbe.Hits.Clear();
            DelayedFacingProbe.Enabled = true;
            try { await NextTurn(player, left); }
            finally { DelayedFacingProbe.Enabled = false; }
            Check(DelayedFacingProbe.Hits.Count == upgrades.Length && DelayedFacingProbe.Hits.Select(h => h.Target).SequenceEqual(desired),
                "Normal/upgraded/stacked Tony waves hit the native seeded random targets exactly once: " + scenario);
            Check(DelayedFacingProbe.Hits.All(h => h.Facing == (h.Target == right ? SurroundedPower.Direction.Right : SurroundedPower.Direction.Left) &&
                h.LooksRight == (h.Target == right)), "Each delayed hit turns visibly and logically before native damage hooks: " + scenario);
            Check(player.Creature.Block == upgrades.Sum(up => up ? 9 : 7), "Delayed facing preserves each normal/upgraded block grant: " + scenario);
            Check(System.Text.Json.JsonSerializer.Serialize(run.Rng.CombatTargets.ToSerializable()) ==
                System.Text.Json.JsonSerializer.Serialize(forecast.ToSerializable()), "Turning consumes no additional random draws: " + scenario);
            await Capture("qa-crab-tony-" + scenario);
            scenario++;
        }
    }
}

// Observe the real native damage boundary, rather than the fix's helper.
[HarmonyPatch(typeof(Hook), nameof(Hook.BeforeDamageReceived))]
internal static class DelayedFacingProbe
{
    internal static bool Enabled;
    internal static readonly List<(Creature Target, SurroundedPower.Direction Facing, bool LooksRight)> Hits = [];
    private static void Prefix(Creature target, Creature? dealer, CardModel? cardSource)
    {
        if (!Enabled || cardSource is not TonyWave || dealer?.GetPower<SurroundedPower>() is not { } power) return;
        var actor = NCombatRoom.Instance!.GetCreatureNode(dealer)!;
        Hits.Add((target, power.Facing, actor.Body.GlobalTransform.Determinant() < 0));
    }
}
