using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task WorkshopRelicChecks()
    {
        var run = await ReliabilityRun("WORKSHOPRELICS");
        var player = run.Players.Single();
        RelicModel[] models = [ModelDb.Relic<OpenEndWrench>(), ModelDb.Relic<SpinningScrew>(), ModelDb.Relic<SpareKnuckles>(), ModelDb.Relic<EmergencyOilCan>()];
        Check(models.All(r => player.Character.RelicPool.AllRelics.Any(x => x.Id == r.Id)) && models.Select(r => r.Id).Distinct().Count() == 4,
            "All four workshop relics have distinct IDs in the construct's pool");
        Check(models.All(r => !ModelDb.Character<Ironclad>().RelicPool.AllRelics.Any(x => x.Id == r.Id)),
            "Native characters do not acquire workshop relics from their own pools");
        Check(models.Take(2).All(r => r.Rarity == RelicRarity.Common) && models.Skip(2).All(r => r.Rarity == RelicRarity.Uncommon),
            "Wrench and Screw are common; Knuckles and Oil Can are uncommon");
        Check(models.All(r => r.Icon != null && r.BigIcon != null && r.IconOutline != null), "All four icon, outline and big-icon variants load");
        Check(ModelDb.Relic<OpenEndWrench>().Id.Entry == "PUNCHCONSTRUCT-SPINNING_SCREW" &&
            RelicModel.FromSerializable(ModelDb.Relic<OpenEndWrench>().ToMutable().ToSerializable()) is OpenEndWrench,
            "An old healing/gold Screw save reconstructs as the renamed Wrench");
        Check(RelicModel.FromSerializable(ModelDb.Relic<SpinningScrew>().ToMutable().ToSerializable()) is SpinningScrew,
            "The new draw Screw keeps its separate save identity");

        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled,
            "Workshop relic fixture ready");
        var state = (CombatState)player.Creature.CombatState!;
        var enemies = state.HittableEnemies.ToArray();
        Check(enemies.Length >= 2, "Workshop fixture contains multiple live targets");
        foreach (var e in enemies) await CreatureCmd.SetMaxAndCurrentHp(e, 1000);
        var oil = await RelicCmd.Obtain<EmergencyOilCan>(player);
        string RngState() => JsonSerializer.Serialize(run.Rng.CombatTargets.ToSerializable());
        await PlayerCmd.SetEnergy(0, player);
        var randomBefore = RngState();
        await oil.BeforeSideTurnEnd(Context, CombatSide.Player, [player.Creature]);
        Check(RngState() == randomBefore && enemies.All(e => !e.HasPower<WeakPower>() && !e.HasPower<VulnerablePower>()),
            "Oil Can does nothing and consumes no RNG at zero energy");
        run.Rng.CombatTargets.LoadFromSerializable(new Rng(42).ToSerializable());
        var forecast = new Rng(42);
        var selected = forecast.NextItem(enemies)!;
        await PlayerCmd.SetEnergy(4, player);
        await oil.BeforeSideTurnEnd(Context, CombatSide.Player, [player.Creature]);
        Check(selected.GetPowerAmount<WeakPower>() == 1 && selected.GetPowerAmount<VulnerablePower>() == 1 &&
            enemies.Where(e => e != selected).All(e => !e.HasPower<WeakPower>() && !e.HasPower<VulnerablePower>()) && player.PlayerCombatState!.Energy == 4,
            "Any positive energy applies exactly one Weak and Vulnerable to the same single enemy without spending energy");
        Check(RngState() == JsonSerializer.Serialize(forecast.ToSerializable()), "Oil Can uses exactly one native combat-target RNG draw");
        randomBefore = RngState();
        await oil.BeforeSideTurnEnd(Context, CombatSide.Enemy, enemies);
        await oil.BeforeSideTurnEnd(Context, CombatSide.Player, []);
        Check(RngState() == randomBefore && selected.GetPowerAmount<WeakPower>() == 1,
            "Oil Can ignores enemy turns and turns without its owner");
        await PowerCmd.Remove<WeakPower>(selected); await PowerCmd.Remove<VulnerablePower>(selected);

        var wrench = await RelicCmd.Obtain<OpenEndWrench>(player);
        var knuckles = await RelicCmd.Obtain<SpareKnuckles>(player);
        await CreatureCmd.SetCurrentHp(player.Creature, 50);
        player.Creature.LoseBlockInternal(player.Creature.Block);
        await PowerCmd.Apply<DexterityPower>(Context, player.Creature, 5, player.Creature, null);
        int gold = player.Gold;
        await CreatureCmd.SetCurrentHp(enemies[0], 1);
        await CreatureCmd.Damage(Context, enemies[0], 1, ValueProp.Unpowered, player.Creature);
        Check(player.Creature.CurrentHp == 51 && player.Gold == gold + 5 && player.Creature.Block == 16,
            "A real enemy death grants Wrench healing/gold and exactly 16 Knuckles block, unaffected by Dexterity");
        var block = player.Creature.Block;
        await knuckles.AfterDeath(Context, player.Creature, false, 0);
        await knuckles.AfterDeath(Context, enemies[0], true, 0);
        Check(player.Creature.Block == block, "Player deaths and prevented enemy deaths cannot trigger Knuckles");
        await PowerCmd.Remove<DexterityPower>(player.Creature);

        var screw = await RelicCmd.Obtain<SpinningScrew>(player);
        var enemy = enemies.First(e => e.IsAlive);
        bool oilSeenInEnemyTurn = false;
        EnemyMoveForQa = _ => {
            oilSeenInEnemyTurn |= state.HittableEnemies.Any(e => e.GetPowerAmount<WeakPower>() == 1 && e.GetPowerAmount<VulnerablePower>() == 1);
            return Task.CompletedTask;
        };
        try
        {
            await PlayerCmd.SetEnergy(1, player);
            await NextTurn(player, enemy);
        }
        finally { EnemyMoveForQa = null; }
        Check(oilSeenInEnemyTurn && player.PlayerCombatState!.Hand.Cards.Count == 5,
            "The actual end-turn pipeline applies Oil Can before enemy actions; idle Screw grants no extra draw");
        await RelicCmd.Remove(oil);
        foreach (var e in state.HittableEnemies) { if (e.HasPower<WeakPower>()) await PowerCmd.Remove<WeakPower>(e); if (e.HasPower<VulnerablePower>()) await PowerCmd.Remove<VulnerablePower>(e); }

        async Task Round(bool deal, bool incoming, bool blocked)
        {
            player.Creature.LoseBlockInternal(player.Creature.Block);
            enemy.LoseBlockInternal(enemy.Block);
            if (deal) await CreatureCmd.Damage(Context, enemy, 1, ValueProp.Unpowered, player.Creature);
            if (blocked) await CreatureCmd.GainBlock(player.Creature, 20, ValueProp.Unpowered, null);
            EnemyMoveForQa = monster => incoming && monster == enemy.Monster
                ? CreatureCmd.Damage(Context, player.Creature, 1, ValueProp.Unpowered, enemy) : Task.CompletedTask;
            try { await NextTurn(player, enemy); }
            finally { EnemyMoveForQa = null; }
        }
        await Round(true, true, true);
        Check(player.PlayerCombatState!.Hand.Cards.Count == 6 && !CombatRelicState.ScrewDrawReady[screw],
            "Dealing damage then fully blocking an actual enemy-turn hit grants exactly one extra next-turn card");
        await Round(false, false, false);
        Check(player.PlayerCombatState.Hand.Cards.Count == 5, "Screw eligibility resets and cannot repeat on an idle round");
        int hp = player.Creature.CurrentHp;
        await Round(true, true, false);
        Check(player.Creature.CurrentHp == hp - 1 && player.PlayerCombatState.Hand.Cards.Count == 5,
            "Damage received during the actual enemy turn cancels the next-turn Screw draw");
        enemy.GainBlockInternal(20);
        await CreatureCmd.Damage(Context, enemy, 1, ValueProp.Unpowered, player.Creature);
        await NextTurn(player, enemy);
        Check(player.PlayerCombatState.Hand.Cards.Count == 5, "An entirely blocked outgoing hit does not count as damage dealt");
        await CreatureCmd.Damage(Context, enemy, 1, ValueProp.Unpowered, player.Creature);
        player.Creature.LoseBlockInternal(player.Creature.Block);
        await CreatureCmd.Damage(Context, player.Creature, 1, ValueProp.Unpowered, player.Creature);
        await NextTurn(player, enemy);
        Check(player.PlayerCombatState.Hand.Cards.Count == 5, "Actual self-inflicted damage also prevents Screw's no-damage bonus");
        await CreatureCmd.Damage(Context, enemy, 1, ValueProp.Unpowered, player.Creature);
        var restored = (SpinningScrew)RelicModel.FromSerializable(screw.ToSerializable());
        var cloned = (SpinningScrew)screw.MutableClone();
        Check(CombatRelicState.ScrewDamageGiven[restored] && CombatRelicState.ScrewDamageGiven[cloned] &&
            !CombatRelicState.ScrewDamageTaken[restored] && !CombatRelicState.ScrewDrawReady[restored],
            "Screw's mid-round flags survive native serialization and cloning");
        await screw.BeforeCombatStart();
        Check(!CombatRelicState.ScrewDamageGiven[screw] && !CombatRelicState.ScrewDamageTaken[screw] && !CombatRelicState.ScrewDrawReady[screw],
            "A new combat clears every pending Screw flag");
        gold = player.Gold;
        var remaining = state.HittableEnemies.ToArray();
        foreach (var foe in remaining)
        {
            await CreatureCmd.SetCurrentHp(foe, 1);
            foe.LoseBlockInternal(foe.Block);
        }
        oil = await RelicCmd.Obtain<EmergencyOilCan>(player);
        // Use a real queued card action so native action-end victory runs too.
        await Play<MegaCrit.Sts2.Core.Models.Cards.Thunderclap>(player);
        await Until(() => !CombatManager.Instance.IsInProgress, "Workshop fixture victory");
        randomBefore = RngState();
        await oil.BeforeSideTurnEnd(Context, CombatSide.Player, [player.Creature]);
        Check(player.Gold == gold + 5 * remaining.Length && RngState() == randomBefore, "Final enemy defeats each pay once and Oil Can safely ignores a finished combat");
        await NGame.Instance!.ReturnToMainMenu();
        SaveManager.Instance.DeleteCurrentRun();
    }
}
