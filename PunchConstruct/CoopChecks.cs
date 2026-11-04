using System.Diagnostics;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

// Explicit QA only: two independent game processes, native ENet transport,
// lobby and synchronized game actions. Files coordinate fixtures and compare
// results; they never transfer player choices or substitute for network input.
internal static partial class PlaytestRunner
{
    private static INetGameService? _coopNet;
    private static string _coopDir = "", _coopRole = "";
    private static int _coopPlayers;
    private static string CoopRole(int index) => index == 0 ? "host" : index == 1 ? "client" : "client" + index;
    private sealed class CoopLobbyListener : IStartRunLobbyListener
    {
        internal StartRunLobby Lobby = null!;
        internal Task<RunState>? Started;
        public void BeginRun(string seed, List<ActModel> acts, IReadOnlyList<ModifierModel> modifiers)
            => Started = NGame.Instance!.StartNewMultiplayerRun(Lobby, false, acts, modifiers, seed, Lobby.Ascension);
        public void PlayerConnected(StartRunLobbyPlayer player) { }
        public void PlayerChanged(StartRunLobbyPlayer player, bool isRandomCharacterResolution) { }
        public void AscensionChanged() { }
        public void SeedChanged() { }
        public void ModifiersChanged() { }
        public void MaxAscensionChanged() { }
        public void RemotePlayerDisconnected(StartRunLobbyPlayer player) => GD.Print("[Construct COOP DISCONNECT] remote lobby " + player.id);
        public void LocalPlayerDisconnected(NetErrorInfo info) => GD.Print("[Construct COOP DISCONNECT] local lobby " + info);
    }

    private static async Task CoopWait(Func<bool> predicate, string operation)
    {
        var watch = Stopwatch.StartNew();
        while (!predicate())
        {
            if (watch.Elapsed.TotalSeconds > 35) throw new TimeoutException(operation);
            if (NRun.Instance == null) _coopNet?.Update();
            await Frames(1);
        }
    }
    private static async Task CoopBarrier(string name)
    {
        System.IO.File.WriteAllText(System.IO.Path.Combine(_coopDir, name + "-" + _coopRole), "ready");
        await CoopWait(() => Enumerable.Range(0, _coopPlayers).All(i => System.IO.File.Exists(System.IO.Path.Combine(_coopDir, name + "-" + CoopRole(i)))), "Coop barrier " + name);
    }
    private static string CoopArg(string name) => OS.GetCmdlineArgs().Single(a => a.StartsWith(name + "=")).Substring(name.Length + 1);

    // Peers 2 and 3 send their character change after joining, so that message
    // can still be in flight when this process reaches the lobby barrier. Every
    // difficulty assertion below depends on the whole party, so wait for the
    // complete selection on every peer instead of the first two players only;
    // otherwise some peers would still see a native teammate and take the
    // non-construct branch while the last peer takes the construct branch.
    private static bool CoopSelectionsReady(StartRunLobby lobby, CharacterModel guest) =>
        lobby.Players.Count == _coopPlayers && lobby.Players[1].character.Id == guest.Id &&
        lobby.Players.Where((_, index) => index != 1).All(p => p.character is ConstructCharacter);

    private static async Task<CardModel> CoopPlay<T>(RunState run, int actor, string phase, bool upgraded = false, bool targetsEnemy = true, CardModel? selection = null, int upgradeCount = 0)
        where T : CardModel, new()
    {
        var player = run.Players[actor];
        var card = ((CombatState)player.Creature.CombatState!).CreateCard<T>(player);
        for (int i = 0; i < Math.Max(upgraded ? 1 : 0, upgradeCount); i++)
        { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
        await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
        await PlayerCmd.SetEnergy(20, player);
        await CoopBarrier(phase + "-prepared");
        if (LocalContext.IsMine(card))
        {
            Check(card.TryManualPlay(targetsEnemy ? player.Creature.CombatState!.HittableEnemies.First() : null), "Queue native network input " + phase);
            if (selection != null)
            {
                await CoopWait(() => NPlayerHand.Instance?.CurrentMode == NPlayerHand.Mode.SimpleSelect, "Actual hand-selection UI opens " + phase);
                await Frames(2);
                var hand = NPlayerHand.Instance!;
                AccessTools.Method(typeof(NPlayerHand), "SelectCardInSimpleMode").Invoke(hand, [hand.GetCardHolder(selection)]);
                var completion = AccessTools.Field(typeof(NPlayerHand), "_selectionCompletionSource").GetValue(hand) as TaskCompletionSource<IEnumerable<CardModel>>;
                if (completion is { Task.IsCompleted: false })
                    AccessTools.Method(typeof(NPlayerHand), "OnSelectModeConfirmButtonPressed").Invoke(hand, [null]);
            }
        }
        await CoopWait(() => card.Pile?.Type is not (PileType.Hand or PileType.Play), "Network card resolution " + phase);
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        await CoopWait(() => !RunManager.Instance.ActionExecutor.IsRunning, "Network action and post-action checksum complete " + phase);
        await CoopBarrier(phase + "-resolved");
        return card;
    }

    private static async Task CoopSnapshot(RunState run, string phase)
    {
        object Power(PowerModel p) => new { Id = p.Id.ToString(), p.Amount, Applier = p.Applier?.Player?.NetId ?? p.Applier?.PetOwner?.NetId,
            Vars = p.DynamicVars.Select(v => new { v.Key, v.Value.BaseValue }).ToArray() };
        object Card(CardModel c) => new { Id = c.Id.ToString(), c.IsUpgraded, c.CurrentUpgradeLevel,
            Vars = c.DynamicVars.Select(v => new { v.Key, v.Value.BaseValue }).ToArray(),
            Enchantment = c.Enchantment?.Id.ToString(), Level = c.Enchantment?.Amount };
        var value = new {
            Players = run.Players.Select(p => new { p.NetId, Character = p.Character.Id.ToString(), p.Creature.CurrentHp, p.Creature.MaxHp, p.Creature.Block, p.Gold,
                Powers = p.Creature.Powers.Select(Power).ToArray(), Energy = p.PlayerCombatState?.Energy,
                Relics = p.Relics.Select(r => new { Id = r.Id.ToString(), r.DisplayAmount,
                    ScrewGiven = r is SpinningScrew s1 ? (bool?)CombatRelicState.ScrewDamageGiven[s1] : null,
                    ScrewTaken = r is SpinningScrew s2 ? (bool?)CombatRelicState.ScrewDamageTaken[s2] : null,
                    ScrewReady = r is SpinningScrew s3 ? (bool?)CombatRelicState.ScrewDrawReady[s3] : null }).ToArray(),
                Deck = p.Deck.Cards.Select(Card).ToArray(), Hand = p.PlayerCombatState?.Hand.Cards.Select(Card).ToArray(),
                Discard = p.PlayerCombatState?.DiscardPile.Cards.Select(Card).ToArray(),
                PetHp = p.Osty?.CurrentHp, PetPowers = p.Osty?.Powers.Select(Power).ToArray() }).ToArray(),
            Enemies = run.Players[0].Creature.CombatState?.Enemies.Select(e => new { Id = e.Monster?.Id.ToString(), e.CurrentHp, e.Block, Powers = e.Powers.Select(Power).ToArray() }).ToArray(),
            CombatTargetRng = run.Rng.CombatTargets.ToSerializable()
        };
        System.IO.File.WriteAllText(System.IO.Path.Combine(_coopDir, phase + "-" + _coopRole + ".json"), JsonSerializer.Serialize(value));
        await CoopBarrier(phase + "-snapshot");
    }

    private static async Task CoopCombatChecks(RunState run)
    {
        var p0 = run.Players[0]; var p1 = run.Players[1];
        var state = (CombatState)p0.Creature.CombatState!;
        var enemy = state.HittableEnemies.First();
        foreach (var foe in state.HittableEnemies)
        {
            await CreatureCmd.SetMaxAndCurrentHp(foe, 1000);
            foreach (var power in foe.Powers.ToArray()) await PowerCmd.Remove(power);
        }
        foreach (var player in run.Players)
        {
            await CardCmd.Discard(Context, player.PlayerCombatState!.Hand.Cards.ToArray());
            await CreatureCmd.GainBlock(player.Creature, 100, ValueProp.Unpowered, null);
        }
        await PowerCmd.Apply<ThornsPower>(Context, enemy, 1, enemy, null);
        for (int hit = 1; hit <= 3; hit++)
        {
            await CoopPlay<SmallWave>(run, 0, "vigor-thorns-" + hit);
            Check(p0.Creature.GetPowerAmount<VigorPower>() == hit && p1.Creature.GetPowerAmount<VigorPower>() == 0,
                "Network attack consumes starting host Vigor and keeps only its new blocked-reflection grant: " + hit);
        }
        await PowerCmd.Remove<ThornsPower>(enemy);
        await CoopPlay<SmallWave>(run, 0, "vigor-consume");
        Check(!p0.Creature.HasPower<VigorPower>(), "Host Vigor is consumed after the last reflected hit on every peer");
        await p0.Relics.OfType<GuardStance>().Single().BeforeCombatStart();
        await CoopBarrier("vigor-cleared");
        await CreatureCmd.Damage(Context, p0.Creature, 1, ValueProp.Unpowered, enemy);
        Check(p0.Creature.GetPowerAmount<VigorPower>() == 1 && p1.Creature.GetPowerAmount<VigorPower>() == 0,
            "Blocked host hit grants Vigor only to the host");
        await CreatureCmd.Damage(Context, p1.Creature, 1, ValueProp.Unpowered, enemy);
        Check(p0.Relics.OfType<GuardStance>().Single().DisplayAmount == 2 &&
            (p1.Character is not ConstructCharacter || p1.Relics.OfType<GuardStance>().Single().DisplayAmount == 2),
            "Players maintain separate starter growth counters");
        foreach (var player in run.Players) await PowerCmd.Remove<VigorPower>(player.Creature);
        await RelicCmd.Replace(p0.Relics.OfType<GuardStance>().Single(), ModelDb.Relic<AncientGuardStance>().ToMutable());
        await CreatureCmd.Damage(Context, p0.Creature, 1, ValueProp.Unpowered, enemy);
        Check(p0.Creature.GetPowerAmount<StrengthPower>() == 0 && p0.Creature.GetPowerAmount<GuardStrengthNextTurnPower>() == 1 &&
            p1.Creature.GetPowerAmount<GuardStrengthNextTurnPower>() == 0 &&
            p0.Relics.OfType<AncientGuardStance>().Single().Title.GetFormattedText() == "古铜之拳", "Ancient replacement queues temporary Strength only for its owner");
        await PowerCmd.Remove<GuardStrengthNextTurnPower>(p0.Creature);
        await CoopBarrier("fixture-ready");
        await CoopPlay<SicEm>(run, 0, "host-mark");
        if (p1.Character is not Ironclad) await CoopPlay<SicEm>(run, 1, "guest-mark", upgraded: true);
        var marks = enemy.Powers.OfType<ConstructSicEmPower>().ToArray();
        Check(marks.Single(p => p.Applier == p0.Creature).Amount == 3 &&
            (p1.Character is not ConstructCharacter || marks.Length == 2 && marks.Single(p => p.Applier == p1.Creature).Amount == 4),
            "Two constructs keep independent summon marks on the same enemy");
        if (p1.Character is Necrobinder)
            Check(enemy.GetPower<SicEmPower>()?.Applier == p1.Creature && enemy.GetPowerAmount<SicEmPower>() == 4,
                "Native Necrobinder mark stays separate from the construct mark");
        int guestPet = p1.Osty?.CurrentHp ?? 0;
        await CoopPlay<SmallWave>(run, 0, "host-followup");
        Check(p0.Osty?.CurrentHp == 3 && (p1.Osty?.CurrentHp ?? 0) == guestPet,
            "Host attack summons only the host's Osty");
        int hostPet = p0.Osty!.CurrentHp;
        if (p1.Character is Necrobinder) await CoopPlay<SicEm>(run, 1, "guest-followup");
        else await CoopPlay<SmallWave>(run, 1, "guest-followup");
        Check(p0.Osty.CurrentHp == hostPet && (p1.Osty?.CurrentHp ?? 0) == guestPet + (p1.Character is Ironclad ? 0 : 4),
            "Guest attacks cannot trigger or redirect the host's summon mark");
        await CoopPlay<SicEm>(run, 0, "host-stack", upgraded: true);
        Check(enemy.Powers.OfType<ConstructSicEmPower>().Single(p => p.Applier == p0.Creature).Amount == 7 &&
            (p1.Character is not ConstructCharacter || enemy.Powers.OfType<ConstructSicEmPower>().Single(p => p.Applier == p1.Creature).Amount == 4),
            "Further upgrades stack only on the same player's mark");
        await CoopPlay<UnarmToArms>(run, 0, "host-unarm", targetsEnemy: false);
        decimal oldBlock = p0.Creature.Block;
        hostPet = p0.Osty!.CurrentHp;
        guestPet = p1.Osty?.CurrentHp ?? 0;
        await CoopPlay<SmallWave>(run, 0, "host-unarm-wave");
        Check(p0.Creature.Block == oldBlock && p0.Osty.CurrentHp == hostPet + 10 && (p1.Osty?.CurrentHp ?? 0) == guestPet,
            "Host block replacement and mark summon stay local to the host during a real network card action");
        for (int actor = 2; actor < run.Players.Count; actor++)
        {
            await CoopPlay<SicEm>(run, actor, "extra-mark-" + actor);
            var hp = run.Players.Select(p => p.Osty?.CurrentHp ?? 0).ToArray();
            await CoopPlay<SmallWave>(run, actor, "extra-followup-" + actor);
            Check(run.Players.Select((p, i) => (p.Osty?.CurrentHp ?? 0) == hp[i] + (i == actor ? 3 : 0)).All(v => v),
                "Third/fourth player mark summons only its own pet: " + actor);
            await CoopPlay<NeverBackDown>(run, actor, "extra-never-" + actor, targetsEnemy: false);
            await CoopPlay<TonyWave>(run, actor, "extra-delay-" + actor, targetsEnemy: false);
        }
        await PowerCmd.Apply<StrengthPower>(Context, p0.Creature, 2, p0.Creature, null);
        await PowerCmd.Apply<StrengthPower>(Context, p0.Osty!, 30, p0.Creature, null);
        await PowerCmd.Apply<VigorPower>(Context, p0.Creature, 4, p0.Creature, null);
        int beforeHp = enemy.CurrentHp;
        int petHp = p0.Osty!.CurrentHp;
        await CoopPlay<Unleash>(run, 0, "host-hp-attack");
        Check(beforeHp - enemy.CurrentHp == 6 + petHp + 2 + 4 && !p0.Creature.HasPower<VigorPower>(),
            "Network Unleash attacks as its owner, scales by current pet HP, and consumes owner Vigor");
        beforeHp = enemy.CurrentHp;
        int petMax = p0.Osty.MaxHp;
        await CoopPlay<Protector>(run, 0, "host-max-hp-attack", upgraded: true);
        Check(beforeHp - enemy.CurrentHp == 15 + petMax + 2,
            "Network Protector+ scales by maximum pet HP and owner Strength rather than pet Strength");
        await PowerCmd.Remove<StrengthPower>(p0.Creature);
        await PowerCmd.Remove<StrengthPower>(p0.Osty);
        await CreatureCmd.GainBlock(p0.Creature, 10, ValueProp.Unpowered, null);
        await CreatureCmd.Damage(Context, p0.Creature, 1, ValueProp.Unpowered, enemy);
        await CreatureCmd.Damage(Context, p0.Creature, 1, ValueProp.Unpowered, enemy);
        Check(p0.Creature.GetPowerAmount<GuardStrengthNextTurnPower>() == 5 && !p0.Creature.HasPower<StrengthPower>(),
            "Two blocked hits stack the next-turn queue at two plus three, without immediate Strength");
        await CoopSnapshot(run, "marks");

        await CoopPlay<NeverBackDown>(run, 0, "host-never", targetsEnemy: false);
        if (p1.Character is ConstructCharacter) await CoopPlay<NeverBackDown>(run, 1, "guest-never", upgraded: true, targetsEnemy: false);
        await CoopPlay<TonyWave>(run, 0, "host-delay", targetsEnemy: false);
        if (p1.Character is ConstructCharacter) await CoopPlay<TonyWave>(run, 1, "guest-delay", upgraded: true, targetsEnemy: false);
        await PowerCmd.Apply<ObserverMantra>(Context, p0.Creature, 9, p0.Creature, null);
        await CoopPlay<ObserverProstrate>(run, 0, "host-divinity", targetsEnemy: false);
        Check(p0.Creature.HasPower<ObserverDivinity>() && p0.Creature.GetPowerAmount<ObserverMantra>() == 1 &&
            p0.PlayerCombatState!.Energy == 23 && !p1.Creature.HasPower<ObserverDivinity>(),
            "Native network Prostrate enters only the owner's Divinity and grants exactly three energy");
        Check(ObserverDivinityVfx.Find(p0.Creature) is { } divineVfx && !Descendants(divineVfx).OfType<CanvasLayer>().Any(),
            "Every peer shows owner's Divinity particles without a multiplayer full-screen border");
        await CoopSnapshot(run, "divinity");
        var previous = run.Players.Select(p => p.PlayerCombatState!.AllCards.ToHashSet()).ToArray();
        int turn = p0.PlayerCombatState!.TurnNumber;
        HoldEnemyActions = true;
        try
        {
            await CoopBarrier("end-turn-prepared");
            var me = LocalContext.GetMe(run)!;
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(me, me.PlayerCombatState!.TurnNumber));
            await CoopWait(() => run.Players.All(p => p.PlayerCombatState!.TurnNumber > turn && p.PlayerCombatState.Phase == PlayerTurnPhase.Play) &&
                !CombatManager.Instance.PlayerActionsDisabled, "Actual two-player end-turn and turn-start triggers");
            await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
            await CoopBarrier("next-turn-ready");
        }
        finally { HoldEnemyActions = false; }
        var hostGenerated = p0.PlayerCombatState!.AllCards.Except(previous[0]).ToArray();
        Check(hostGenerated.OfType<Fisticuffs>().Count() == 1 && hostGenerated.OfType<Fisticuffs>().All(c => !c.IsUpgraded) && hostGenerated.OfType<Burn>().Count() == 1,
            "Host normal Never Back Down creates only its normal Fisticuffs and Burn");
        if (p1.Character is ConstructCharacter)
        {
            var generated = p1.PlayerCombatState!.AllCards.Except(previous[1]).ToArray();
            Check(generated.OfType<Fisticuffs>().Count() == 1 && generated.OfType<Fisticuffs>().All(c => !c.IsUpgraded) && generated.OfType<Burn>().Count() == 1,
                "Guest upgraded Never Back Down keeps its upgraded generation separate");
        }
        Check(!p0.Creature.HasPower<DelayedWavePower>() && !p1.Creature.HasPower<DelayedWavePower>() &&
            !enemy.HasPower<ConstructSicEmPower>(), "Both delayed random attacks resolve and temporary enemy marks expire");
        Check(p0.Creature.GetPowerAmount<StrengthPower>() == 5 && p0.Creature.GetPowerAmount<GuardTemporaryStrengthPower>() == 5 &&
            p0.Creature.GetPower<GuardStrengthNextTurnPower>() is { ActiveAmount: 5, PendingAmount: 0 } && !p1.Creature.HasPower<GuardTemporaryStrengthPower>(),
            "Native multiplayer turn start activates five temporary Strength only on its owner");
        Check(!p0.Creature.HasPower<ObserverDivinity>() && ObserverDivinityVfx.Find(p0.Creature) == null,
            "Owner turn end exits Divinity and clears all peer effects before the next turn");
        await CoopSnapshot(run, "turn");

        turn = p0.PlayerCombatState!.TurnNumber;
        HoldEnemyActions = true;
        try
        {
            await CoopBarrier("temporary-strength-expiry-prepared");
            var me = LocalContext.GetMe(run)!;
            RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new EndPlayerTurnAction(me, me.PlayerCombatState!.TurnNumber));
            await CoopWait(() => run.Players.All(p => p.PlayerCombatState!.TurnNumber > turn && p.PlayerCombatState.Phase == PlayerTurnPhase.Play) &&
                !CombatManager.Instance.PlayerActionsDisabled, "Native multiplayer temporary-Strength expiry turn");
            await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
            await CoopBarrier("temporary-strength-expired");
        }
        finally { HoldEnemyActions = false; }
        Check(!p0.Creature.HasPower<GuardTemporaryStrengthPower>() && !p0.Creature.HasPower<StrengthPower>(),
            "Temporary Strength expires on its owner's actual multiplayer turn end");
        await CoopSnapshot(run, "temporary-strength-expired");

        int remoteChoices = 0;
        RunManager.Instance.PlayerChoiceSynchronizer.PlayerChoiceReceived += (player, _, _) =>
        {
            if (!LocalContext.IsMe(player)) remoteChoices++;
        };
        for (int actor = 0; actor < run.Players.Count; actor++)
        {
            var player = run.Players[actor];
            await CardCmd.Discard(Context, player.PlayerCombatState!.Hand.Cards.ToArray());
            var target = state.CreateCard<SmallWave>(player);
            await CardPileCmd.AddGeneratedCardToCombat(target, PileType.Hand, player);
            await CoopPlay<BattleFervor>(run, actor, "fervor-" + actor, upgraded: actor == 1, targetsEnemy: false, selection: target);
            Check(player.PlayerCombatState.Hand.Cards.OfType<Fisticuffs>().Single() is { Enchantment: Momentum { Amount: 5 } } fist &&
                !fist.IsUpgraded && player.PlayerCombatState.Hand.Cards.OfType<Burn>().Count() == 1 &&
                (actor == 1 ? player.PlayerCombatState.DiscardPile : player.PlayerCombatState.ExhaustPile).Cards.Any(c => c is BattleFervor),
                "Network-synchronized hand choice transforms only that player's card with Momentum 5: " + actor);
        }
        Check(remoteChoices > 0, "Each peer received a real remote player-choice message");
        await CoopSnapshot(run, "choices");

        foreach (var relic in run.Players.SelectMany(p => p.Relics).Where(r => r is GuardStance or AncientGuardStance))
        {
            var clone = (RelicModel)relic.MutableClone();
            var restored = RelicModel.FromSerializable(relic.ToSerializable());
            Check(clone.DisplayAmount == relic.DisplayAmount && restored.DisplayAmount == relic.DisplayAmount,
                "Growing relic preserves its next grant through native cloning and serialized reconstruction: " + relic.Id);
        }
        var helmet = (ConstructHelmet)await RelicCmd.Obtain<ConstructHelmet>(p0);
        await PowerCmd.Apply<StrengthPower>(Context, p0.Creature, 1, p0.Creature, null);
        var restoredHelmet = (ConstructHelmet)RelicModel.FromSerializable(helmet.ToSerializable());
        Check(CombatRelicState.HelmetUsed[restoredHelmet] && CombatRelicState.HelmetUsed[(ConstructHelmet)helmet.MutableClone()],
            "Helmet's shared once-per-combat allowance survives native cloning and reconstruction");
        await PowerCmd.Remove<StrengthPower>(p0.Creature);
        await PowerCmd.Remove<DexterityPower>(p0.Creature);
        foreach (var foe in state.HittableEnemies) await CreatureCmd.SetCurrentHp(foe, 1);
        await CoopPlay<MegaCrit.Sts2.Core.Models.Cards.Thunderclap>(run, 0, "finish-prepared-combat", targetsEnemy: false);
        await CoopWait(() => !CombatManager.Instance.IsInProgress, "Targeted native multiplayer victory");
        Check(run.Players.SelectMany(p => p.Relics).Where(r => r is GuardStance or AncientGuardStance).All(r => r.DisplayAmount == 1),
            "Native victory resets both players' growing relics before entering the next room");

        // Debug room changes bypass the normal reward/map interaction delay.
        // Native exhaust VFX owns a two-second delayed card return; unloading
        // its parent early returns that same pooled card from _ExitTree too.
        await CoopWait(() => !Descendants(_tree.Root).Any(n => n is NCardExhaustVfx or NCardExhaustQuickVfx or NCardFlyVfx),
            "Native card presentations finish before the debug room transition");
        await Frames(2);
        await CoopBarrier("victory-card-presentations-drained");
        Check(!Descendants(_tree.Root).Any(n => n is NCardExhaustVfx or NCardExhaustQuickVfx or NCardFlyVfx),
            "QA debug jump waits for native exhaust/flight ownership cleanup on every peer");

        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<ColorfulPhilosophers>(), showTransition: false);
        await CoopBarrier("event-ready");
        var previousDecks = run.Players.Select(p => p.Deck.Cards.ToHashSet()).ToArray();
        int choicesBeforeRelics = remoteChoices;
        var offers = run.Players.Where(p => p.Character is ConstructCharacter)
            .Select(p => RelicCmd.Obtain<SeaGlass>(p)).ToArray();
        if (LocalContext.GetMe(run)?.Character is ConstructCharacter)
        {
            await CoopWait(() => Descendants(_tree.Root).OfType<NSimpleCardSelectScreen>().Any(s => s.IsVisibleInTree()), "Actual Sea Glass selection UI opens");
            var screen = Descendants(_tree.Root).OfType<NSimpleCardSelectScreen>().Single(s => s.IsVisibleInTree());
            var offered = (IReadOnlyList<CardModel>)AccessTools.Field(typeof(NCardGridSelectionScreen), "_cards").GetValue(screen)!;
            Check(offered.Count == 15, "Actual local Sea Glass UI displays all fifteen selected imports");
            foreach (var card in offered.Take(2)) AccessTools.Method(typeof(NSimpleCardSelectScreen), "OnCardClicked").Invoke(screen, [card]);
            AccessTools.Method(typeof(NSimpleCardSelectScreen), "CompleteSelection").Invoke(screen, []);
        }
        await Task.WhenAll(offers);
        await CoopBarrier("sea-glass-finished");
        foreach (var player in run.Players.Where(p => p.Character is ConstructCharacter))
        {
            int index = run.Players.ToList().IndexOf(player);
            var added = player.Deck.Cards.Except(previousDecks[index]).ToArray();
            Check(added.Length == 2 && added.All(CardAcquisition.IsSelectedImport), "Sea Glass synchronizes the selected two cards only into its owner's permanent deck: " + player.NetId);
        }
        Check(CardSelectCmd.Selector == null && !CardAcquisition.SeaGlassSelection && CardFactoryScope.Player == null,
            "Parallel owners' relic selections restore all acquisition scopes");
        if (p1.Character is ConstructCharacter || _coopRole == "client")
            Check(remoteChoices > choicesBeforeRelics, "Sea Glass used real remote reward-choice messages");
        await CoopSnapshot(run, "rewards");
        await BoxingCoopChecks(run);
        await CoopBoundaryChecks(run);
        await CoopPandoraCheck(run);
    }

    private static async Task CoopPandoraCheck(RunState run)
    {
        var owner = run.Players[0];
        var before = owner.Deck.Cards.ToArray();
        var waves = before.OfType<SmallWave>().Where(c => c.IsRemovable && c.IsTransformable).ToArray();
        Check(waves.Length > 0, "Network Pandora fixture retains eligible Small Waves");
        await RelicCmd.Obtain<PandorasBox>(owner);
        var added = owner.Deck.Cards.Except(before).ToArray();
        Check(added.Length == waves.Length && waves.All(c => !owner.Deck.Cards.Contains(c)) &&
            before.Except(waves).All(c => owner.Deck.Cards.Contains(c)) && added.All(c => CardAcquisition.Eligible(owner, c)),
            "Actual network Pandora transforms only eligible owner waves into this character's allowed pool on every peer");
        var preview = Descendants(_tree.Root).OfType<MegaCrit.Sts2.Core.Nodes.Screens.NCardsViewScreen>().FirstOrDefault(s => s.IsVisibleInTree());
        if (LocalContext.IsMe(owner))
        {
            Check(preview != null, "Only Pandora's local owner receives the native result preview");
            AccessTools.Method(typeof(MegaCrit.Sts2.Core.Nodes.Screens.NCardsViewScreen), "OnReturnButtonPressed").Invoke(preview, [null]);
        }
        else Check(preview == null, "Remote Pandora transformation does not open a teammate's result screen");
        await Frames(15);
        await CoopSnapshot(run, "pandora");
    }

    private static async Task CoopChecks()
    {
        _coopRole = CoopArg("--punchconstruct-coop-role");
        _coopPlayers = int.Parse(CoopArg("--punchconstruct-coop-players"));
        _coopDir = CoopArg("--punchconstruct-coop-dir");
        ushort port = ushort.Parse(CoopArg("--punchconstruct-coop-port"));
        string guest = CoopArg("--punchconstruct-coop-guest");
        CharacterModel guestCharacter = guest switch { "ironclad" => ModelDb.Character<Ironclad>(), "necrobinder" => ModelDb.Character<Necrobinder>(), _ => ModelDb.Character<ConstructCharacter>() };
        var listener = new CoopLobbyListener();
        StartRunLobby lobby;
        if (_coopRole == "host")
        {
            var host = new NetHostGameService(PeerVersionInfo.LocalDefault());
            var error = host.StartENetHost(port, _coopPlayers - 1);
            if (error != null) throw new InvalidOperationException("ENet host failed: " + error);
            _coopNet = host;
            lobby = new StartRunLobby(GameMode.Standard, host, listener, _coopPlayers);
            listener.Lobby = lobby;
            lobby.AddLocalHostPlayer(new UnlockState(SaveManager.Instance.Progress), 0);
            lobby.SetLocalCharacter(ModelDb.Character<ConstructCharacter>());
            System.IO.File.WriteAllText(System.IO.Path.Combine(_coopDir, "host-listening"), port.ToString());
        }
        else
        {
            await CoopWait(() => System.IO.File.Exists(System.IO.Path.Combine(_coopDir, "host-listening")), "Host listen");
            int index = _coopRole == "client" ? 1 : int.Parse(_coopRole[6..]);
            if (index > 1) await CoopWait(() => System.IO.File.Exists(System.IO.Path.Combine(_coopDir, "joined-" + (index - 1))), "Previous peer joined");
            var client = new NetClientGameService(PeerVersionInfo.LocalDefault());
            var joined = await new JoinFlow(client).Begin(new ENetClientConnectionInitializer((ulong)(index + 1), "127.0.0.1", port), _tree);
            if (joined.joinResponse == null) throw new InvalidOperationException("No lobby join response: " + joined.sessionState);
            // A response may complete inline inside native message dispatch.
            // Replace fixture listeners only after that enumeration returns.
            await Frames(2);
            _coopNet = client;
            lobby = new StartRunLobby(GameMode.Standard, client, listener, _coopPlayers);
            listener.Lobby = lobby;
            lobby.InitializeFromMessage(joined.joinResponse.Value);
            lobby.SetLocalCharacter(index == 1 ? guestCharacter : ModelDb.Character<ConstructCharacter>());
            await Frames(2);
            System.IO.File.WriteAllText(System.IO.Path.Combine(_coopDir, "joined-" + index), "ready");
        }
        await CoopWait(() => CoopSelectionsReady(lobby, guestCharacter),
            "All native lobby character selections synchronize");
        Check(_coopNet!.IsConnected && lobby.Players.Count == _coopPlayers, "All actual ENet peers connected; role=" + _coopRole);
        Check(lobby.Players[0].character is ConstructCharacter && lobby.Players[1].character.Id == guestCharacter.Id,
            "Lobby carries custom character model IDs; guest=" + guest);
        await CoopBarrier("lobby-ready");
        // One partition for the whole party: the availability cap and the branch
        // that exercises it must never be derived from different player sets.
        bool constructParty = lobby.Players.All(p => p.character is ConstructCharacter);
        int expectedMaximum = constructParty ? ConstructDifficulty.Maximum :
            lobby.Players.Where(p => p.character is not ConstructCharacter).Min(p => p.maxMultiplayerAscensionUnlocked);
        await CoopWait(() => lobby.MaxAscension == expectedMaximum, "Character-specific multiplayer ascension availability");
        Check(lobby.MaxAscension == expectedMaximum, "Fresh Construct party unlocks all difficulties; native teammate retains its limit");
        int globalAscension = SaveManager.Instance.Progress.MaxMultiplayerAscension;
        if (constructParty)
        {
            if (_coopRole == "host") lobby.SyncAscensionChange(ConstructDifficulty.Maximum);
            await CoopWait(() => lobby.Ascension == ConstructDifficulty.Maximum, "Native ENet synchronizes highest difficulty");
            Check(lobby.Ascension == 10, "All actual ENet peers can select the highest difficulty on fresh profiles");
            await CoopBarrier("ascension-ten");
            if (_coopRole == "host") lobby.SetLocalCharacter(ModelDb.Character<Ironclad>());
            await CoopWait(() => lobby.Players[0].character is Ironclad && lobby.MaxAscension == 0 && lobby.Ascension == 0,
                "Switching host to native character restores original cap and clamps selected difficulty");
            Check(SaveManager.Instance.Progress.MaxMultiplayerAscension == globalAscension, "Difficulty unlock does not raise account-wide multiplayer progression");
            await CoopBarrier("ascension-native-cap");
            if (_coopRole == "host") lobby.SetLocalCharacter(ModelDb.Character<ConstructCharacter>());
            await CoopWait(() => lobby.Players[0].character is ConstructCharacter && lobby.MaxAscension == 10,
                "Switching back restores Construct's full difficulty range on all peers");
        }
        if (_coopRole == "host") lobby.SyncAscensionChange(0);
        await CoopWait(() => lobby.Ascension == 0, "Reset fixture difficulty before existing combat assertions");
        await CoopBarrier("ascension-checked");
        lobby.SetReady(true);
        await CoopWait(() => listener.Started != null, "Both players ready start native run");
        await CoopWait(() => listener.Started!.IsCompleted, "Native multiplayer run startup");
        var run = await listener.Started!;
        lobby.CleanUp(false);
        Check(run.Players.Count == _coopPlayers && LocalContext.GetMe(run)?.NetId == _coopNet.NetId,
            "Native multiplayer run has all players and correct local identity");
        Check(run.Players[0].Relics.Any(r => r is GuardStance) && run.Players[0].Deck.Cards.Count == 8,
            "Host construct starts with its eight-card deck and starter relic");
        await CoopBarrier("run-ready");
        if (OS.GetCmdlineArgs().Contains("--punchconstruct-coop-treasure"))
        {
            await TreasureCursorChecks(run);
            await CoopBarrier("checked");
            GD.Print($"[Construct COOP PASS] {_checks} checks; native treasure cursor {_coopRole}; guest={guest}");
            await NGame.Instance!.ReturnToMainMenu();
            await Frames(5);
            await QaStartupPreload.Wait(_tree);
            _coopNet = null;
            return;
        }
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
        await CoopWait(() => run.Players.All(p => p.PlayerCombatState?.Phase == PlayerTurnPhase.Play) && !CombatManager.Instance.PlayerActionsDisabled,
            "Direct multiplayer combat ready");
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        await CoopBarrier("combat-ready");
        Check(run.Players.All(p => p.Creature.CombatState == run.Players[0].Creature.CombatState), "Both players enter the same actual combat");
        await CoopSnapshot(run, "combat");
        await CoopCombatChecks(run);
        await CoopBarrier("checked");
        GD.Print($"[Construct COOP PASS] {_checks} checks; native ENet {_coopRole}; guest={guest}");
        await NGame.Instance!.ReturnToMainMenu();
        await Frames(5);
        await QaStartupPreload.Wait(_tree);
        _coopNet = null;
    }
}
