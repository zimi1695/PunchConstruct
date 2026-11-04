using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task BoxingChecks()
    {
        var run = await ReliabilityRun("BOXING-SECOND-PARAGRAPH");
        var player = run.Players.Single();
        CardModel[] designs = [ModelDb.Card<BrambleField>(), ModelDb.Card<LeftHook>(), ModelDb.Card<RightHook>(), ModelDb.Card<CombatRoutine>(),
            ModelDb.Card<QuickPunch>(), ModelDb.Card<CombinationPunch>(), ModelDb.Card<BoxingGuard>(), ModelDb.Card<FurnaceOverload>()];
        foreach (var card in designs)
        {
            Check(card.Pool is ColorlessCardPool && player.Character.CardPool.AllCardIds.Contains(card.Id) && !ModelDb.CardPool<ColorlessCardPool>().AllCardIds.Contains(card.Id), "Construct-only registration and native colorless ownership: " + card.Title);
            Check(CardAcquisition.Eligible(player, card) == !BoxingGeneration.Restricted(card), "Acquisition restriction: " + card.Title);
            Check(ResourceLoader.Load<Texture2D>(card.PortraitPath)?.GetSize() == new Vector2(1000, 760), "Adopted portrait loads: " + card.Title);
            var mutable = run.CreateCard(card, player); CardCmd.Upgrade(mutable);
            var loaded = run.LoadCard(mutable.ToSerializable(), player);
            Check(loaded.Id == card.Id && loaded.IsUpgraded && loaded.Pool is ColorlessCardPool, "Native upgraded save roundtrip: " + card.Title);
        }
        for (int index = 0; index < 3; index++)
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<Bugslayer>(), showTransition: false);
            var room = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
            Check(room.CurrentOptions.Count == 3 && room.CurrentOptions.All(o => !o.IsLocked && o.HoverTips.Any()), "Actual Bugslayer presents three options with previews");
            int count = player.Deck.Cards.Count;
            await room.CurrentOptions[index].Chosen();
            Check(room.IsFinished && player.Deck.Cards.Count == count + 1 && player.Deck.Cards.Last().Id == designs[4 + index].Id, "Actual event adds precisely the selected card: " + index);
        }
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Boxing fixture ready");
        var state = (CombatState)player.Creature.CombatState!;
        var enemy = state.HittableEnemies.First();
        foreach (var target in state.HittableEnemies) await CreatureCmd.SetMaxAndCurrentHp(target, 2000);
        await OstyCmd.Summon(Context, player, 30, ModelDb.Card<BrambleField>());
        var osty = player.Osty!;
        await Play<BrambleField>(player, upgraded: true);
        var units = state.GetCreaturesOnSide(CombatSide.Player).Concat(state.GetCreaturesOnSide(CombatSide.Enemy)).Where(c => c.IsAlive).ToArray();
        Check(units.All(c => c.GetPowerAmount<ThornsPower>() == 2), "Two native Thorns on every living unit, including Osty");
        int reflectedHp = enemy.CurrentHp, ostyHp = osty.CurrentHp;
        await CreatureCmd.Damage(Context, osty, 1, ValueProp.Move, enemy, null, null);
        Check(enemy.CurrentHp == reflectedHp - 2 && osty.CurrentHp == ostyHp - 1, "Osty reflects two damage exactly once without a thorns loop");
        foreach (var unit in units) if (unit.HasPower<ThornsPower>()) await PowerCmd.Remove<ThornsPower>(unit);
        foreach (bool upgraded in new[] { false, true })
        {
            await CardCmd.Discard(Context, player.PlayerCombatState!.Hand.Cards.ToArray());
            player.Creature.LoseBlockInternal(player.Creature.Block);
            if (player.Creature.HasPower<VigorPower>()) await PowerCmd.Remove<VigorPower>(player.Creature);
            int hp = enemy.CurrentHp;
            var left = await Play<LeftHook>(player, enemy, upgraded);
            var right = player.PlayerCombatState.Hand.Cards.OfType<RightHook>().Single();
            Check(hp - enemy.CurrentHp == (upgraded ? 8 : 6) && player.Creature.Block == (upgraded ? 6 : 4), "Left Hook damage and block: upgraded=" + upgraded);
            Check(right.IsUpgraded == upgraded && right.Keywords.Contains(CardKeyword.Retain) && left.Pile?.Type == PileType.Exhaust, "Left Hook exhausts and generates matching retained Right Hook");
            if (upgraded) Check(left.GetDescriptionForPile(PileType.Hand).Contains("右勾拳+"), "Upgraded Left Hook text shows Right Hook+");
            await NextTurn(player, enemy);
            Check(right.Pile?.Type == PileType.Hand, "Generated Right Hook retains across the real turn boundary");
            player.Creature.LoseBlockInternal(player.Creature.Block);
            if (player.Creature.HasPower<VigorPower>()) await PowerCmd.Remove<VigorPower>(player.Creature);
            await PowerCmd.Apply<ThornsPower>(Context, enemy, 2, enemy, null);
            int ownerHp = player.Creature.CurrentHp; hp = enemy.CurrentHp;
            await PlayerCmd.SetEnergy(20, player);
            Check(right.TryManualPlay(enemy), "Play generated Right Hook");
            await Until(() => right.Pile?.Type == PileType.Exhaust, "Right Hook finishes");
            Check(player.Creature.CurrentHp == ownerHp && player.Creature.Block == (upgraded ? 14 : 12) - 2 && hp - enemy.CurrentHp == (upgraded ? 12 : 10), "Right Hook blocks thorns BEFORE attacking: upgraded=" + upgraded);
            await PowerCmd.Remove<ThornsPower>(enemy);
        }
        if (player.Creature.HasPower<VigorPower>()) await PowerCmd.Remove<VigorPower>(player.Creature);
        int enemyHp = enemy.CurrentHp;
        await Play<QuickPunch>(player, enemy, true);
        Check(enemyHp - enemy.CurrentHp == 16, "Quick Punch upgrade keeps sixteen damage");
        enemyHp = enemy.CurrentHp;
        await Play<CombinationPunch>(player, enemy, true);
        Check(enemyHp - enemy.CurrentHp == 12 && enemy.GetPowerAmount<FrailPower>() == 2 && !enemy.HasPower<VulnerablePower>(), "Combination Punch deals two hits and applies Frail, not Vulnerable");
        player.Creature.LoseBlockInternal(player.Creature.Block);
        await Play<BoxingGuard>(player, upgraded: true);
        Check(player.Creature.Block == 10, "Upgraded Boxing Guard still grants ten block");
        await CardCmd.Discard(Context, player.PlayerCombatState!.Hand.Cards.ToArray());
        var previousTrio = player.PlayerCombatState.AllCards.Where(c => c is QuickPunch or CombinationPunch or BoxingGuard).ToHashSet();
        var routinePlayed = await Play<CombatRoutine>(player, upgraded: true);
        var generatedTrio = player.PlayerCombatState.AllCards.Where(c => c is QuickPunch or CombinationPunch or BoxingGuard).Except(previousTrio).ToArray();
        Check(routinePlayed.Pile?.Type == PileType.Exhaust && new[] { typeof(QuickPunch), typeof(CombinationPunch), typeof(BoxingGuard) }
            .All(t => generatedTrio.Count(c => c.GetType() == t && c.Pile?.Type == PileType.Hand && !c.IsUpgraded) == 1), "Routine immediately generates each ordinary card and exhausts");
        var routine = run.CreateCard<CombatRoutine>(player); CardCmd.Upgrade(routine);
        Check(routine.Keywords.Contains(CardKeyword.Innate) && routine.Keywords.Contains(CardKeyword.Exhaust), "Routine upgrade is Innate, still Exhaust");
        await CardCmd.Discard(Context, player.PlayerCombatState.Hand.Cards.ToArray());
        for (int i = 0; i < 9; i++) await BoxingGeneration.Add(state.CreateCard<SmallWave>(player));
        int discarded = player.PlayerCombatState.DiscardPile.Cards.Count;
        await Play<CombatRoutine>(player);
        Check(player.PlayerCombatState.Hand.Cards.Count == 10 && player.PlayerCombatState.DiscardPile.Cards.Count == discarded + 2,
            "Routine fills its owner's last hand slot and overflows the other two cards to discard immediately");
        await CheckDiscardCounter(player, "Routine overflow");
        foreach (bool upgraded in new[] { false, true })
        {
            await CardCmd.Discard(Context, player.PlayerCombatState.Hand.Cards.ToArray());
            player.Creature.LoseBlockInternal(player.Creature.Block);
            if (player.Creature.HasPower<StrengthPower>()) await PowerCmd.Remove<StrengthPower>(player.Creature);
            for (int i = 0; i < 3; i++) await CardPileCmd.AddGeneratedCardToCombat(state.CreateCard<SmallWave>(player), PileType.Draw, player);
            int burns = player.PlayerCombatState.AllCards.OfType<Burn>().Count();
            var overload = await Play<FurnaceOverload>(player, upgraded: upgraded);
            Check(player.Creature.Block == (upgraded ? 8 : 6) && player.Creature.GetPowerAmount<StrengthPower>() == (upgraded ? 2 : 1), "Overload block and Strength: upgraded=" + upgraded);
            Check(player.PlayerCombatState.AllCards.OfType<Burn>().Count() == burns + 2 && player.PlayerCombatState.Hand.Cards.Count == (upgraded ? 4 : 3) && overload.Pile?.Type == PileType.Exhaust, "Overload draws, adds two Burns and exhausts: upgraded=" + upgraded);
        }
        Check(!CombatManager.Instance.PlayerActionsDisabled, "New cards leave the action queue responsive");
        await CardCmd.Discard(Context, player.PlayerCombatState.Hand.Cards.ToArray());
        for (int i = 0; i < 9; i++) await BoxingGeneration.Add(state.CreateCard<SmallWave>(player));
        for (int i = 0; i < 2; i++) await CardPileCmd.AddGeneratedCardToCombat(state.CreateCard<SmallWave>(player), PileType.Draw, player);
        discarded = player.PlayerCombatState.DiscardPile.Cards.Count;
        await Play<FurnaceOverload>(player, upgraded: true);
        Check(player.PlayerCombatState.Hand.Cards.Count == 10 && player.PlayerCombatState.DiscardPile.Cards.Count == discarded + 2,
            "Full-hand Overload stops native drawing at ten cards and overflows both generated Burns to discard");
        await CheckDiscardCounter(player, "Overload overflow");
        await Capture("boxing-cards");
        foreach (var other in state.HittableEnemies.Where(c => c != enemy).ToArray()) await CreatureCmd.Kill(other, true);
        await CreatureCmd.SetCurrentHp(enemy, 1);
        await CardCmd.Discard(Context, player.PlayerCombatState.Hand.Cards.ToArray());
        await Play<LeftHook>(player, enemy, true);
        await Until(() => !CombatManager.Instance.IsInProgress, "Lethal Left Hook completes native victory");
        Check(!RunManager.Instance.ActionExecutor.IsRunning, "A lethal Left Hook can generate its Right Hook and finish without stranding an action");
        await Frames(120);
    }
}
