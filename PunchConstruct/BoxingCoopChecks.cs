using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task BoxingCoopChecks(RunState run)
    {
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
        await CoopWait(() => run.Players.All(p => p.PlayerCombatState?.Phase == PlayerTurnPhase.Play) && !CombatManager.Instance.PlayerActionsDisabled, "Dedicated network boxing fixture");
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        await CoopBarrier("boxing-ready");
        var state = (CombatState)run.Players[0].Creature.CombatState!;
        foreach (var foe in state.HittableEnemies) await CreatureCmd.SetMaxAndCurrentHp(foe, 2000);
        foreach (var player in run.Players) await CardCmd.Discard(Context, player.PlayerCombatState!.Hand.Cards.ToArray());
        for (int actor = 0; actor < run.Players.Count; actor++)
        {
            var owner = run.Players[actor];
            if (owner.Character is not ConstructCharacter) continue;
            await OstyCmd.Summon(Context, owner, 20, ModelDb.Card<BrambleField>());
            await CoopPlay<LeftHook>(run, actor, "boxing-left-" + actor, upgraded: actor % 2 == 1);
            var right = owner.PlayerCombatState!.Hand.Cards.OfType<RightHook>().Single();
            Check(right.Owner == owner && right.IsUpgraded == (actor % 2 == 1), "Generated Right Hook belongs to the correct network owner and upgrade");
            await CoopBarrier("boxing-right-" + actor + "-ready");
            await PlayerCmd.SetEnergy(20, owner);
            if (LocalContext.IsMine(right)) Check(right.TryManualPlay(state.HittableEnemies.First()), "Submit generated Right Hook as actual local network input");
            await CoopWait(() => right.Pile?.Type == PileType.Exhaust, "Generated Right Hook network resolution");
            await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
            await CoopBarrier("boxing-right-" + actor + "-done");
            await CoopPlay<CombatRoutine>(run, actor, "boxing-routine-" + actor, upgraded: actor % 2 == 1, targetsEnemy: false);
        }
        await CoopPlay<BrambleField>(run, 0, "boxing-all-thorns", targetsEnemy: false);
        Check(state.GetCreaturesOnSide(CombatSide.Player).Concat(state.GetCreaturesOnSide(CombatSide.Enemy)).Where(c => c.IsAlive).All(c => c.GetPowerAmount<ThornsPower>() == 2),
            "Network Bramble grants exactly two Thorns to every living player, teammate, summon and enemy");
        await CoopSnapshot(run, "boxing-generated");
        for (int actor = 0; actor < run.Players.Count; actor++)
        {
            var owner = run.Players[actor];
            if (owner.Character is not ConstructCharacter) continue;
            Check(new[] { typeof(QuickPunch), typeof(CombinationPunch), typeof(BoxingGuard) }
                .All(t => owner.PlayerCombatState!.AllCards.Count(c => c.GetType() == t && c.Owner == owner && !c.IsUpgraded) == 1),
                "Network Routine immediately grants exactly its owner's three ordinary cards");
            await CardCmd.Discard(Context, owner.PlayerCombatState!.Hand.Cards.ToArray());
            int burns = owner.PlayerCombatState.AllCards.OfType<Burn>().Count();
            await CoopPlay<FurnaceOverload>(run, actor, "boxing-overload-" + actor, upgraded: actor % 2 == 1, targetsEnemy: false);
            Check(owner.PlayerCombatState.AllCards.OfType<Burn>().Count() == burns + 2, "Network Overload adds precisely two Burns to its owner");
        }
        await CoopSnapshot(run, "boxing-resolved");
        // Native draw-to-hand tweens outlive the action task. Normal play gives
        // them time on the reward screen; this direct-room fixture must do so too.
        await Frames(120);
        await CoopWait(() => Convert.ToInt32(HarmonyLib.AccessTools.Method(typeof(CardCmd), "GetTotalCardsBeingPreviewed").Invoke(null, [])) == 0,
            "Native card previews settle before fixture victory");
        await CoopBarrier("boxing-card-animations-settled");
        // Leave through the native victory cleanup, rather than replacing a
        // live combat while native draw/generation animations still own NCards.
        foreach (var foe in state.HittableEnemies) await CreatureCmd.SetCurrentHp(foe, 1);
        await CoopPlay<MegaCrit.Sts2.Core.Models.Cards.Thunderclap>(run, 0, "boxing-fixture-victory", targetsEnemy: false);
        await CoopWait(() => !CombatManager.Instance.IsInProgress, "Native boxing fixture victory and cleanup");
        await Frames(120);
        await CoopBarrier("boxing-fixture-finished");
    }
}
