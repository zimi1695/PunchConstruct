using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task ResolvePunchNab(EventModel punchOff)
    {
        int count = punchOff.Owner!.Relics.Count;
        int curses = punchOff.Owner.Deck.Cards.Count(c => c is Injury);
        var task = (Task)AccessTools.Method(typeof(PunchOff), "Nab").Invoke(punchOff, [])!;
        await Until(() => task.IsFaulted || Descendants(_tree.Root).OfType<NRewardsScreen>().Any(s => s.IsVisibleInTree()), "Punch Off relic reward");
        if (task.IsFaulted) await task;
        var screen = Descendants(_tree.Root).OfType<NRewardsScreen>().Single(s => s.IsVisibleInTree());
        var button = Descendants(screen).OfType<NRewardButton>().Single(b => b.Reward is RelicReward);
        AccessTools.Method(typeof(NRewardButton), "OnRelease").Invoke(button, []);
        await Until(() => task.IsCompleted, "Punch Off finishes after taking relic");
        await task;
        Check(punchOff.Owner.Relics.Count == count + 1 && punchOff.Owner.Deck.Cards.Count(c => c is Injury) == curses + 1,
            "Punch Off Nab resolves its actual native relic reward and Injury");
    }
    private static async Task<RunState> ReliabilityRun(string seed) => await NGame.Instance!.StartNewSingleplayerRun(
        ModelDb.Character<ConstructCharacter>(), true,
        [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], seed, GameMode.Standard);

    private static async Task PersistenceSeed()
    {
        var run = await ReliabilityRun("SOUPPERSIST");
        var player = run.Players.Single();
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<DollRoom>(), showTransition: false);
        var waves = player.Deck.Cards.OfType<SmallWave>().ToArray();
        CardCmd.Upgrade(waves[0]);
        CardCmd.Upgrade(waves[2]);
        var enchantments = new EnchantmentModel[] { ModelDb.Enchantment<Sharp>(), ModelDb.Enchantment<Nimble>(), ModelDb.Enchantment<Swift>() };
        for (int i = 0; i < enchantments.Length; i++) CardCmd.Enchant(enchantments[i].ToMutable(), waves[i], i + 1);
        var skipped = waves.Take(3).ToArray();
        var unchanged = player.Deck.Cards.Where(c => c is not SmallWave).ToArray();
        await RelicCmd.Obtain<NutritiousSoup>(player);
        var eternal = player.Deck.Cards.Where(c => PersistentCardKeywords.SoupEternal[c]).ToArray();
        Check(eternal.Length == 2 && player.Deck.Cards.Count == 8 && skipped.All(player.Deck.Cards.Contains),
            "Soup replaces the two unenchanted Small Waves and skips the three enchanted originals");
        Check(unchanged.All(c => player.Deck.Cards.Contains(c) && !c.Keywords.Contains(CardKeyword.Eternal)),
            "Soup leaves the original Iron Wave, Tony Wave and Shock unchanged");
        Check(SoupWavePatch.Transforming.Count == 0 && eternal.All(c => !c.IsRemovable && !c.IsTransformable && c.Enchantment == null),
            "Replacement restrictions remain native and no enchantment is overwritten");
        foreach (var card in eternal)
        {
            var clone = (CardModel)card.MutableClone();
            Check(card is IronWave && card.Keywords.Contains(CardKeyword.Eternal) && clone.Keywords.Contains(CardKeyword.Eternal) &&
                card.EnergyCost.GetWithModifiers(CostModifiers.None) == 0 && clone.EnergyCost.GetWithModifiers(CostModifiers.None) == 0,
                "Eternal and zero cost survive native clone of an unenchanted Soup replacement");
        }
        var legacy = run.CreateCard<IronWave>(player);
        PersistentCardKeywords.SoupEternal[legacy] = true;
        Check(legacy.Keywords.Contains(CardKeyword.Eternal) && legacy.EnergyCost.GetWithModifiers(CostModifiers.None) == 0,
            "Old Soup marker with a native one-cost card automatically restores zero base cost");
        var ordinaryIron = player.Deck.Cards.Single(c => c is IronWave && !PersistentCardKeywords.SoupEternal[c]);
        Check(ordinaryIron.EnergyCost.GetWithModifiers(CostModifiers.None) == 1,
            "Ordinary Iron Wave keeps its one-energy cost");
        var tome = (DustyTome)ModelDb.Relic<DustyTome>().ToMutable();
        tome.SetupForPlayer(player);
        await RelicCmd.Obtain(tome, player);
        Check(player.Deck.Cards.Count == 9 && player.Deck.Cards.Single(c => c is BoxingChampionForm).IsUpgraded,
            "Actually obtaining Dusty Tome grants upgraded Boxing Champion Form through native AfterObtained");
        foreach (var (type, change) in RegentAdaptations.Changes)
        {
            var original = ModelDb.AllCards.Single(c => c.GetType() == type);
            var card = run.CreateCard(original, player);
            await CardPileCmd.Add(card, PileType.Deck, skipVisuals: true);
            var clone = (CardModel)card.MutableClone();
            clone.UpgradeInternal(); clone.FinalizeUpgradeInternal(); clone.DowngradeInternal(); clone.FinalizeUpgradeInternal();
            Check(card.CurrentStarCost == 0 && clone.CurrentStarCost == 0 && card.EnergyCost.GetWithModifiers(CostModifiers.None) == change.Cost &&
                (change.Damage == 0 || clone.DynamicVars.Damage.BaseValue == card.DynamicVars.Damage.BaseValue),
                "Regent cost and damage do not drift through cloning, upgrade and downgrade: " + type.Name);
        }
        await SaveManager.Instance.SaveRun(run.CurrentRoom);
        Check(SaveManager.Instance.LoadRunSave().Success, "Soup and Tome fixture is written to native disk save");
    }

    private static async Task PersistenceResume(NMainMenu menu)
    {
        Check(!RunManager.Instance.IsInProgress && SaveManager.Instance.LoadRunSave().Success,
            "A fresh process starts with the saved fixture, with no in-memory run");
        await (Task)AccessTools.Method(typeof(NMainMenu), "OnContinueButtonPressedAsync").Invoke(menu, [])!;
        await Until(() => RunManager.Instance.IsInProgress && RunManager.Instance.DebugOnlyGetState()?.CurrentRoom != null, "Native menu Continue");
        await Frames(90);
        var run = RunManager.Instance.DebugOnlyGetState()!;
        var player = run.Players.Single();
        Check(player.Character is ConstructCharacter && player.Deck.Cards.Count == 16, "Real fresh-process Continue restores character and sixteen-card fixture");
        Check(player.Relics.Any(r => r is NutritiousSoup) && player.Relics.Any(r => r is DustyTome), "Soup and Dusty Tome survive restart");
        var eternal = player.Deck.Cards.Where(c => PersistentCardKeywords.SoupEternal[c]).ToArray();
        Check(eternal.Length == 2 && eternal.All(c => c is IronWave && c.Keywords.Contains(CardKeyword.Eternal) && c.EnergyCost.GetWithModifiers(CostModifiers.None) == 0),
            "Both Soup replacements retain Eternal and zero cost after process restart");
        var skippedLoaded = player.Deck.Cards.OfType<SmallWave>().ToArray();
        Check(eternal.All(c => c.Enchantment == null) && skippedLoaded.Length == 3 && skippedLoaded.Count(c => c.IsUpgraded) == 2 &&
            skippedLoaded.Select(c => c.Enchantment?.Id).ToHashSet().SetEquals(new[] { ModelDb.GetId<Sharp>(), ModelDb.GetId<Nimble>(), ModelDb.GetId<Swift>() }),
            "The three skipped Small Waves retain distinct enchantments and upgrades after real disk loading");
        Check(player.Deck.Cards.Single(c => c is BoxingChampionForm).IsUpgraded,
            "Tome-granted ancient card retains its upgrade after restart");
        foreach (var (type, change) in RegentAdaptations.Changes)
        {
            var card = player.Deck.Cards.Single(c => c.GetType() == type);
            var original = ModelDb.AllCards.Single(c => c.GetType() == type);
            Check(card.CurrentStarCost == 0 && card.EnergyCost.GetWithModifiers(CostModifiers.None) == change.Cost &&
                (change.Damage == 0 || card.DynamicVars.Damage.BaseValue == original.DynamicVars.Damage.BaseValue + change.Damage),
                "Fresh-process loading preserves Regent adaptation exactly once: " + type.Name);
        }
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<NibbitsWeak>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Restored deck enters combat");
        var state = player.PlayerCombatState!;
        var inCombat = state.Hand.Cards.Concat(state.DrawPile.Cards).Concat(state.DiscardPile.Cards).ToArray();
        Check(inCombat.Count(c => c is IronWave && c.Keywords.Contains(CardKeyword.Eternal) && c.EnergyCost.GetWithModifiers(CostModifiers.None) == 0) == 2,
            "The restored deck's combat copies retain two Eternal keywords and zero costs");
        var freeIron = inCombat.First(c => c is IronWave && PersistentCardKeywords.SoupEternal[c]);
        await CreatureCmd.SetMaxAndCurrentHp(player.Creature.CombatState!.HittableEnemies.First(), 1000);
        if (freeIron.Pile?.Type != PileType.Hand) await CardPileCmd.Add(freeIron, PileType.Hand);
        await PlayerCmd.SetEnergy(3, player);
        Check(freeIron.TryManualPlay(player.Creature.CombatState!.HittableEnemies.First()), "Eternal Iron Wave can be played");
        await Until(() => freeIron.Pile?.Type == PileType.Discard, "Zero-cost Iron Wave resolves");
        Check(player.PlayerCombatState!.Energy == 3, "Playing restored Eternal Iron Wave spends no energy");
        foreach (var (type, change) in RegentAdaptations.Changes)
        {
            var card = inCombat.Single(c => c.GetType() == type);
            var clone = card.CreateCloneForPlayer(player);
            Check(clone.CurrentStarCost == 0 && (change.Damage == 0 || clone.DynamicVars.Damage.BaseValue == card.DynamicVars.Damage.BaseValue) &&
                clone.EnergyCost.GetWithModifiers(CostModifiers.None) == change.Cost,
                "Actual combat cloning preserves adaptation after restart: " + type.Name);
        }
        Check(player.Deck.Cards.Where(c => PersistentCardKeywords.SoupEternal[c]).All(c => !c.IsRemovable && !c.IsTransformable),
            "Eternal's normal deck removal and transformation restrictions survive restart");
        await NGame.Instance!.ReturnToMainMenu();
        SaveManager.Instance.DeleteCurrentRun();
    }

    private static async Task EdgeChecks()
    {
        var run = await ReliabilityRun("EDGECASES");
        var player = run.Players.Single();
        var protectedWave = player.Deck.Cards.OfType<SmallWave>().First();
        CardCmd.Enchant<TezcatarasEmber>(protectedWave, 1);
        var leaf = (LeafyPoultice)ModelDb.Relic<LeafyPoultice>().ToMutable();
        // Use the real command out of combat and a protected target, then reset
        // the fixture so the system-gate cases still begin with the starter deck.
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<DollRoom>(), showTransition: false);
        await RelicCmd.Obtain(leaf, player);
        Check(player.Deck.Cards.Contains(protectedWave) && !protectedWave.IsRemovable && player.Deck.Cards.Count(c => c is SmallWave) == 3,
            "Leafy Poultice skips an Eternal Small Wave and transforms two eligible waves without throwing");
        foreach (var c in player.Deck.Cards.Where(c => c is not SmallWave && c is not IronWave && c is not TonyWave && c is not SmallShockwave).ToArray())
            await CardPileCmd.RemoveFromDeck(c, false);
        // Real factories, varied draws, no combat and no progression.
        for (int i = 0; i < 12; i++)
        {
            var shop = MerchantInventory.CreateForNormalMerchant(player);
            Check(shop.CharacterCardEntries.All(e => e.CreationResult?.Card is { } c && c.Pool is not ColorlessCardPool &&
                    (CardAcquisition.Eligible(player, c) || CardAcquisition.ShopAncients.Contains(c.GetType()) && c is not Protector)) &&
                shop.ColorlessCardEntries.Count == 2 && shop.ColorlessCardEntries.All(e => e.CreationResult?.Card is { } c && c.Pool is ColorlessCardPool && CardAcquisition.Eligible(player, c)),
                "Native merchant population keeps colored and colorless slots valid: " + i);
            foreach (var entry in shop.ColorlessCardEntries)
            {
                AccessTools.Method(typeof(MerchantCardEntry), "RestockAfterPurchase").Invoke(entry, [shop]);
                Check(entry.CreationResult?.Card is { } c && c.Pool is ColorlessCardPool && CardAcquisition.Eligible(player, c),
                    "Native colorless restock remains in selected pool: " + i);
            }
        }
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<NibbitsWeak>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Edge-case room ready");
        var enemy = player.Creature.CombatState!.HittableEnemies.First();
        await CreatureCmd.SetMaxHp(enemy, 1000); await CreatureCmd.SetCurrentHp(enemy, 1000);
        await NewDesignChecks(run, player, enemy);
        await NGame.Instance!.ReturnToMainMenu();
        SaveManager.Instance.DeleteCurrentRun();
    }

    private static async Task BoundaryChecks()
    {
        var run = await ReliabilityRun("BOUNDARYCASES");
        var player = run.Players.Single();
        var curse = run.CreateCard<Doubt>(player);
        var changedCurse = CardFactory.CreateRandomCardForTransform(curse, false, new Rng(100));
        Check(changedCurse.Type == CardType.Curse, "Actual curse transformation keeps the native curse pool");
        var shared = run.CreateCard<DoomResonance>(player);
        var duplicate = run.CreateCard<DoomResonance>(player);
        await CardPileCmd.Add(shared, PileType.Deck, skipVisuals: true);
        await CardPileCmd.Add(duplicate, PileType.Deck, skipVisuals: true);
        Check(CardAcquisition.Weight(player, shared) == 2 && CardAcquisition.Eligible(player, ModelDb.Card<FinalJudgment>()) &&
            !CardAcquisition.Eligible(player, ModelDb.Card<Unleash>()),
            "Two copies shared by summon and doom groups still give weight two and preserve the Bodyguard gate");
        await CardPileCmd.RemoveFromDeck(shared, false); await CardPileCmd.RemoveFromDeck(duplicate, false);
        Check(CardAcquisition.Weight(player, shared) == 1 && !CardAcquisition.Eligible(player, ModelDb.Card<FinalJudgment>()),
            "Removing the last group cards immediately restores weight and closes the gate");

        async Task Enter()
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<NibbitsWeak>().ToMutable(), showTransition: false);
            await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Boundary combat ready");
            foreach (var enemy in player.Creature.CombatState!.HittableEnemies)
                await CreatureCmd.SetMaxAndCurrentHp(enemy, 1000);
        }
        await Enter();
        foreach (var canonical in new RelicModel[] { ModelDb.Relic<ConstructHelmet>(), ModelDb.Relic<RuinedHelmet>() })
        {
            var helmet = await RelicCmd.Obtain(canonical.ToMutable(), player);
            await PowerCmd.Apply<DexterityPower>(Context, player.Creature, -1, player.Creature, null);
            Check(player.Creature.GetPowerAmount<StrengthPower>() == 0, "Helmet ignores negative Dexterity: " + canonical.Id.Entry);
            await PowerCmd.Apply<DexterityPower>(Context, player.Creature, 2, player.Creature, null);
            Check(player.Creature.GetPowerAmount<StrengthPower>() == 2 && player.Creature.GetPowerAmount<DexterityPower>() == 1,
                "First positive Dexterity gain mirrors the gained amount, not the resulting total: " + canonical.Id.Entry);
            await PowerCmd.Apply<StrengthPower>(Context, player.Creature, 3, player.Creature, null);
            Check(player.Creature.GetPowerAmount<StrengthPower>() == 5 && player.Creature.GetPowerAmount<DexterityPower>() == 1,
                "Strength cannot reuse the shared combat allowance after Dexterity: " + canonical.Id.Entry);
            foreach (var enemy in player.Creature.CombatState!.HittableEnemies.ToArray()) await CreatureCmd.SetCurrentHp(enemy, 1);
            await Play<MegaCrit.Sts2.Core.Models.Cards.Thunderclap>(player);
            await Until(() => !CombatManager.Instance.IsInProgress, "One debug-prepared attack ends combat");
            await Enter();
            await PowerCmd.Apply<StrengthPower>(Context, player.Creature, 4, player.Creature, null);
            Check(player.Creature.GetPowerAmount<StrengthPower>() == 4 && player.Creature.GetPowerAmount<DexterityPower>() == 4,
                "Helmet allowance resets in the next actual combat: " + canonical.Id.Entry);
            await RelicCmd.Remove(helmet);
            await PowerCmd.Remove<StrengthPower>(player.Creature); await PowerCmd.Remove<DexterityPower>(player.Creature);
        }
        var target = player.Creature.CombatState!.HittableEnemies.First();
        var dart = await RelicCmd.Obtain<ConstructDart>(player);
        await Play<FightMe>(player, target);
        Check(player.Creature.GetPowerAmount<DexterityPower>() == 3,
            "A two-hit attack triggers Dart once per card and adds to Fight Me's permanent Dexterity");
        await NextTurn(player, target);
        Check(player.Creature.GetPowerAmount<DexterityPower>() == 2,
            "Dart expiry removes only temporary Dexterity, preserving Fight Me's permanent two");
        await RelicCmd.Remove(dart);
        await PowerCmd.Remove<DexterityPower>(player.Creature);
        await Play<IronWaveForm>(player, upgraded: true);
        player.Creature.LoseBlockInternal(player.Creature.Block);
        target.GainBlockInternal(3);
        await CreatureCmd.Damage(Context, target, 5, ValueProp.Unpowered, player.Creature);
        await CreatureCmd.Damage(Context, target, 4, ValueProp.Unpowered, player.Creature);
        Check(player.Creature.Block == 6, "Form uses actual unblocked damage from two separate instances without Dexterity scaling");
        await Play<UnarmToArms>(player);
        var previousOsty = player.Osty?.CurrentHp ?? 0;
        await NextTurn(player, target);
        player.Creature.LoseBlockInternal(player.Creature.Block);
        // Test the replacement during its own turn, after Form has reset its allowance.
        await Play<UnarmToArms>(player);
        await CreatureCmd.Damage(Context, target, 6, ValueProp.Unpowered, player.Creature);
        Check(player.Creature.Block == 0 && player.Osty!.CurrentHp == previousOsty + 6,
            "Form's granted block is replaced once by Unarm to Arms with equal summon");
        await NGame.Instance!.ReturnToMainMenu();
        SaveManager.Instance.DeleteCurrentRun();
    }
}
