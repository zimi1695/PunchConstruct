using System.Diagnostics;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task GuardTempoChecks()
    {
        await NGame.Instance!.ReturnToMainMenu();
        var run = await ReliabilityRun("GUARD-REAL-PECK");
        var player = run.Players.Single();
        await RunManager.Instance.EnterRoomDebug(RoomType.Elite, model: ModelDb.Encounter<ByrdonisElite>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Actual Byrdonis tempo fixture ready");
        var enemy = player.Creature.CombatState!.HittableEnemies.Single();
        var starter = player.Relics.OfType<GuardStance>().Single();
        var move = AccessTools.Method(typeof(Byrdonis), "PeckMove");
        async Task<double> Measure(bool native, bool block)
        {
            GuardDamagePresentation.NativeTimingForQa = native;
            try
            {
                await starter.BeforeCombatStart();
                await PowerCmd.Remove<VigorPower>(player.Creature);
                player.Creature.LoseBlockInternal(player.Creature.Block);
                if (block) player.Creature.GainBlockInternal(1000);
                var timer = Stopwatch.StartNew();
                await (Task)move.Invoke(enemy.Monster, [new[] { player.Creature }])!;
                return timer.Elapsed.TotalMilliseconds;
            }
            finally { GuardDamagePresentation.NativeTimingForQa = false; }
        }
        await RelicCmd.Remove(starter);
        double baseline = await Measure(false, true);
        starter = (GuardStance)await RelicCmd.Obtain<GuardStance>(player);
        double before = await Measure(true, true);
        int beforeVigor = player.Creature.GetPowerAmount<VigorPower>();
        int beforeNext = starter.DisplayAmount;
        double after = await Measure(false, true);
        Check(player.Creature.GetPowerAmount<VigorPower>() == beforeVigor && starter.DisplayAmount == beforeNext && beforeVigor == 6,
            "Full native three-hit Peck preserves three separate grants and identical one-plus-two-plus-three Vigor");
        Check(after < before - 250 && after < baseline - 250,
            "Actual fully-blocked multi-hit animation finishes faster than both native timing and the prior power-only optimization");
        double unblocked = await Measure(false, false);
        Check(!player.Creature.HasPower<VigorPower>() && starter.DisplayAmount == 1 && unblocked > after + 250,
            "Unblocked native Peck retains original timing and does not advance relic growth");
        Godot.GD.Print($"[Construct GUARD REAL TEMPO] native-no-relic={baseline:F1}ms; prior-fix={before:F1}ms; compact-blocked={after:F1}ms; unblocked={unblocked:F1}ms");
    }
}
