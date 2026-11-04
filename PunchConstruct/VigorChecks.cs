using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task VigorChecks()
    {
        var run = await ReliabilityRun("VIGOR-THORNS");
        var player = run.Players.Single();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Thorns fixture ready");
        var enemy = player.Creature.CombatState!.HittableEnemies.First();
        await CreatureCmd.SetMaxAndCurrentHp(enemy, 2000);
        await PowerCmd.Apply<ThornsPower>(Context, enemy, 1, enemy, null);
        await CreatureCmd.GainBlock(player.Creature, 100, ValueProp.Unpowered, null);
        for (int hit = 1; hit <= 4; hit++)
        {
            int before = player.Creature.GetPowerAmount<VigorPower>();
            int hp = enemy.CurrentHp;
            await Play<SmallWave>(player, enemy);
            int after = player.Creature.GetPowerAmount<VigorPower>();
            Godot.GD.Print($"[Construct VIGOR THORNS] attack={hit} before={before} after={after} next={player.Relics.OfType<GuardStance>().Single().DisplayAmount}");
            Check(after == hit, "Attack consumes its starting Vigor and preserves only Vigor earned from this blocked thorns hit: " + hit);
            Check(hp - enemy.CurrentHp == 3 + before, "The next distinct attack actually receives its starting Vigor damage: " + hit);
        }
        await Play<FightMe>(player, enemy);
        Check(player.Creature.GetPowerAmount<VigorPower>() == 5 + 6,
            "Fight Me's two hits consume starting Vigor once and keep both blocked-reflection grants");
        await PowerCmd.Remove<ThornsPower>(enemy);
        int remaining = player.Creature.GetPowerAmount<VigorPower>();
        int finalHp = enemy.CurrentHp;
        await Play<SmallWave>(player, enemy);
        Check(!player.Creature.HasPower<VigorPower>(), "The next attack without thorns consumes all remaining Vigor");
        Check(finalHp - enemy.CurrentHp == remaining + 3, "The final attack receives all retained Vigor as damage");
        Check(!CombatManager.Instance.PlayerActionsDisabled, "Native card queue remains usable after repeated nested block gains");
        await GuardTempoChecks();
    }
}
