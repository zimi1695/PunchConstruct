using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task CoopBoundaryChecks(RunState run)
    {
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
        await CoopWait(() => run.Players.All(p => p.PlayerCombatState?.Phase == PlayerTurnPhase.Play) && !CombatManager.Instance.PlayerActionsDisabled,
            "Second directly prepared multiplayer combat");
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        await CoopBarrier("second-combat-ready");
        Check(run.Players.All(p => !p.Creature.HasPower<DelayedWavePower>() && !p.Creature.HasPower<NeverBackDownPower>() &&
            !p.Creature.HasPower<UnarmToArmsPower>()) &&
            run.Players.SelectMany(p => p.Relics).Where(r => r is GuardStance or AncientGuardStance).All(r => r.DisplayAmount == 1),
            "Consecutive combat clears previous temporary powers and resets every growth counter");
        Check(!CombatRelicState.HelmetUsed[run.Players[0].Relics.OfType<ConstructHelmet>().Single()],
            "Helmet shared allowance resets in the next real combat");
        var state = (CombatState)run.Players[0].Creature.CombatState!;
        foreach (var foe in state.HittableEnemies) await CreatureCmd.SetMaxAndCurrentHp(foe, 1000);
        var cards = new List<CardModel>();
        foreach (var p in run.Players)
        {
            await CardCmd.Discard(Context, p.PlayerCombatState!.Hand.Cards.ToArray());
            var card = state.CreateCard<SmallWave>(p);
            await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, p);
            await PlayerCmd.SetEnergy(10, p);
            cards.Add(card);
        }
        await CoopBarrier("concurrent-attacks-ready");
        // Every peer submits its own input without waiting for another actor.
        var mine = cards.Single(LocalContext.IsMine);
        Check(mine.TryManualPlay(state.HittableEnemies.First()), "Concurrent local attack accepted by native action queue");
        await CoopWait(() => cards.All(c => c.Pile?.Type is not (PileType.Hand or PileType.Play)), "All simultaneous player actions resolve");
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        await CoopBarrier("concurrent-attacks-finished");
        Check(run.Players.All(p => p.Creature.Block == 3), "Concurrent actions apply block once to each correct owner");
        for (int actor = 0; actor < run.Players.Count; actor++)
        {
            var owner = run.Players[actor];
            var target = state.HittableEnemies.First();
            decimal hp = target.CurrentHp, block = owner.Creature.Block;
            var scorching = await CoopPlay<ScorchingIronWave>(run, actor, "scorching-repeat-" + actor, upgradeCount: 3);
            Check(scorching.CurrentUpgradeLevel == 3 && target.CurrentHp == hp - 14 && owner.Creature.Block == block + 14,
                "Three-times-upgraded Scorching Iron Wave resolves exactly once for network owner " + actor);
        }
        await CoopSnapshot(run, "concurrent");

        // Workshop relics: real remote card input, native end-turn synchronization,
        // and independently saved flags for each owner.
        var hostScrew = await RelicCmd.Obtain<SpinningScrew>(run.Players[0]);
        var guestScrew = await RelicCmd.Obtain<SpinningScrew>(run.Players[1]);
        var oil = await RelicCmd.Obtain<EmergencyOilCan>(run.Players[0]);
        await CoopPlay<SmallWave>(run, 0, "workshop-host-damage");
        Check(CombatRelicState.ScrewDamageGiven[hostScrew] && !CombatRelicState.ScrewDamageGiven[guestScrew],
            "A network attack marks only the correct Screw owner as having dealt damage");
        var restoredScrew = (SpinningScrew)RelicModel.FromSerializable(hostScrew.ToSerializable());
        Check(CombatRelicState.ScrewDamageGiven[restoredScrew] && !CombatRelicState.ScrewDamageTaken[restoredScrew],
            "Network-owner Screw flags survive serialized reconstruction");
        await PlayerCmd.SetEnergy(1, run.Players[0]);
        foreach (var player in run.Players.Skip(1)) await PlayerCmd.SetEnergy(0, player);
        int workshopTurn = run.Players[0].PlayerCombatState!.TurnNumber;
        HoldEnemyActions = true;
        try
        {
            await CoopBarrier("workshop-end-turn-ready");
            var local = LocalContext.GetMe(run)!;
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(local, local.PlayerCombatState!.TurnNumber));
            await CoopWait(() => run.Players.All(p => p.PlayerCombatState!.TurnNumber > workshopTurn && p.PlayerCombatState.Phase == PlayerTurnPhase.Play)
                && !CombatManager.Instance.PlayerActionsDisabled, "Workshop relics resolve through real network turn boundaries");
            await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
            await CoopBarrier("workshop-next-turn");
        }
        finally { HoldEnemyActions = false; }
        Check(run.Players[0].PlayerCombatState!.Hand.Cards.Count == 6 && run.Players[1].PlayerCombatState!.Hand.Cards.Count == 5,
            "Screw adds one next-turn card only to the qualifying owner on all peers");
        Check(!CombatRelicState.ScrewDrawReady[hostScrew] && !CombatRelicState.ScrewDamageGiven[hostScrew] &&
            !CombatRelicState.ScrewDamageGiven[guestScrew], "Network turn start consumes the Screw bonus and resets both owners' flags");
        await RelicCmd.Remove(oil);
        var knuckles = await RelicCmd.Obtain<SpareKnuckles>(run.Players[0]);
        var partial = state.HittableEnemies.First();
        await CreatureCmd.SetCurrentHp(partial, 1);
        var oldBlock = run.Players[0].Creature.Block;
        await CoopPlay<SmallWave>(run, 0, "workshop-knuckles-kill");
        Check(CombatManager.Instance.IsInProgress && run.Players[0].Creature.Block == oldBlock + 19,
            "An actual network enemy defeat grants 16 Knuckles block plus the card's three block");

        // A dead teammate must not leave construct triggers or choices stuck.
        // Native exhaust VFX defer returning a card to the pool; the hand container
        // returns the same controls again when the run scene unloads, and Pandora's
        // later fixture keeps the run alive past that deferred return. Clear every
        // hand through the normal discard path and let those presentations finish,
        // so the teammate dies with no half-owned NCard behind.
        foreach (var player in run.Players) await CardCmd.Discard(Context, player.PlayerCombatState!.Hand.Cards.ToArray());
        await CoopWait(() => run.Players.All(p => p.PlayerCombatState!.Hand.Cards.Count == 0) &&
            !Descendants(_tree.Root).Any(n => n is NCardExhaustVfx or NCardExhaustQuickVfx or NCardFlyVfx),
            "Discarded hands finish their native presentation before the teammate dies");
        await CoopBarrier("hands-cleared");
        Check(run.Players.All(p => p.PlayerCombatState!.Hand.Cards.Count == 0), "Every network hand is empty before the teammate dies");
        foreach (var player in run.Players.Take(2)) await RelicCmd.Obtain<OpenEndWrench>(player);
        await CreatureCmd.SetCurrentHp(run.Players[0].Creature, 30);
        int hostGold = run.Players[0].Gold, guestGold = run.Players[1].Gold;
        await CreatureCmd.Kill(run.Players[1].Creature, true);
        await CoopWait(() => run.Players[1].Creature.IsDead, "Native teammate death");
        Check(!run.IsGameOver && run.Players[0].Creature.IsAlive, "One teammate death does not end the surviving host's combat");
        await CoopBarrier("teammate-dead");
        int foes = state.HittableEnemies.Count();
        foreach (var foe in state.HittableEnemies) await CreatureCmd.SetCurrentHp(foe, 1);
        await CoopPlay<MegaCrit.Sts2.Core.Models.Cards.Thunderclap>(run, 0, "survivor-victory", targetsEnemy: false);
        await CoopWait(() => !CombatManager.Instance.IsInProgress, "Survivor wins multiplayer combat without waiting for dead teammate input");
        Check(!RunManager.Instance.ActionExecutor.IsRunning, "No action remains stuck after teammate death and victory");
        Check(run.Players[0].Gold == hostGold + foes * 5 && run.Players[0].Creature.CurrentHp == 30 + foes && run.Players[1].Gold == guestGold,
            "Each enemy death grants Wrench gold/healing once to living owner; dead teammate cannot receive it");
        await CoopSnapshot(run, "survivor");
    }
}
