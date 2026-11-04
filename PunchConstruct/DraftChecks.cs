using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task DraftChecks()
    {
        double speed = Engine.TimeScale;
        Engine.TimeScale = 8;
        try
        {
            var run = await ReliabilityRun("DRAFTFIRSTPARAGRAPH");
            var player = run.Players.Single();
            var native = Player.CreateForNewRun<Ironclad>(new UnlockState([], [], 0), 9988);
            foreach (var source in new CardModel[] { ModelDb.Card<Prowess>() })
                Check(run.CreateCard(source, player).Rarity == CardRarity.Common && run.CreateCard(source, native).Rarity == source.Rarity,
                    "Construct common rarity leaves other owners and canonical card unchanged: " + source.Id);
            Check(run.CreateCard<Fisticuffs>(player).Rarity == CardRarity.Uncommon && ModelDb.Card<Fisticuffs>().Rarity == CardRarity.Uncommon,
                "Fisticuffs remains blue for both Construct and the canonical colorless card");
            var never = run.CreateCard<NeverBackDown>(player);
            Check(never.Rarity == CardRarity.Rare && never.EnergyCost.Canonical == 2 && !never.Description.GetFormattedText().Contains("拳斗+"),
                "Never Back Down is gold, costs two and generates the ordinary fist");
            CardCmd.Upgrade(never);
            Check(never.EnergyCost.GetWithModifiers(CostModifiers.None) == 1 && !never.Description.GetFormattedText().Contains("拳斗+"),
                "Never Back Down upgrade costs one without upgrading the generated fist");
            never.DowngradeInternal(); never.FinalizeUpgradeInternal();
            Check(never.EnergyCost.GetWithModifiers(CostModifiers.None) == 2 && ModelDb.Card<IronWaveForm>().Rarity == CardRarity.Uncommon,
                "Downgrade restores two energy; Iron Wave Form is blue");
            var upgraded = run.CreateCard<ScorchingIronWave>(player);
            foreach (int expected in new[] { 7, 10, 14, 19, 25, 32, 40, 49 })
            {
                CardCmd.Upgrade(upgraded);
                Check(upgraded.DynamicVars.Damage.BaseValue == expected && upgraded.DynamicVars.Block.BaseValue == expected,
                    "Successive real upgrades reach literal damage/block value " + expected);
            }
            foreach (var restored in new[] { run.CloneCard(upgraded), run.LoadCard(upgraded.ToSerializable(), player) })
            {
                Check(restored.CurrentUpgradeLevel == 8 && restored.DynamicVars.Block.BaseValue == 49, "Clone/load preserve eight cumulative upgrades");
                CardCmd.Upgrade(restored);
                Check(restored.DynamicVars.Block.BaseValue == 59, "Ninth upgrade after clone/load is +10");
            }
            var selector = new UpdateSelector();
            using (CardSelectCmd.UseSelector(selector))
            {
                var soupOffer = ConstructSoupOffer.Predicate;
                var closure = soupOffer.DeclaringType!.GetField("<>9", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)!.GetValue(null);
                Check((bool)soupOffer.Invoke(closure, [player.Deck.Cards.First(c => c is SmallWave)])! &&
                    !(bool)soupOffer.Invoke(closure, [player.Deck.Cards.First(c => c is IronWave)])!,
                    "Native ancient Soup offer recognizes Small Waves and stops checking ordinary strikes for Construct");
                Check((bool)soupOffer.Invoke(closure, [run.CreateCard<StrikeIronclad>(native)])!, "Native teammates retain the original Soup offer predicate");
                await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<DollRoom>(), showTransition: false);
                var talisman = await RelicCmd.Obtain<NeowsTalisman>(player);
                Check(player.Deck.Cards.OfType<SmallWave>().Count(c => c.IsUpgraded) == 2 && selector.Options.Last().All(c => c is SmallWave),
                    "Actually obtaining Neow's Talisman selects and upgrades exactly two Small Waves");
                Check(talisman.DynamicDescription.GetFormattedText().Contains("两张") && talisman.DynamicEventDescription.GetFormattedText().Contains("小斩波"),
                    "Talisman inventory and ancient-event descriptions match the confirmed adaptation");
                await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<SpiralingWhirlpool>(), showTransition: false);
                var whirl = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
                Check(whirl.CurrentOptions[0].Description.GetFormattedText().Contains("小斩波") && whirl.CurrentOptions[0].HoverTips.Any(),
                    "Actual Whirlpool option shows Small Wave and its enchantment tooltip");
                await whirl.CurrentOptions[0].Chosen();
                Check(player.Deck.Cards.Count(c => c is SmallWave && c.Enchantment is Spiral) == 1 && whirl.IsFinished,
                    "Actual Whirlpool callback enchants exactly one Small Wave and finishes");
                Check(!(bool)soupOffer.Invoke(closure, [player.Deck.Cards.Single(c => c is SmallWave && c.Enchantment is Spiral)])!,
                    "The ancient Soup offer ignores already-enchanted Small Waves, including Spiral");
                Check(selector.Options.Last().All(c => c is SmallWave) && !ModelDb.Enchantment<Spiral>().CanEnchant(player.Deck.Cards.OfType<IronWave>().Single()),
                    "Native selection offers only eligible Small Waves, not the ordinary Iron Wave");
                await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<Amalgamator>(), showTransition: false);
                var fusion = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
                Check(fusion.CurrentOptions.Count == 2 && fusion.CurrentOptions.All(o => !o.IsLocked), "Construct enters actual fusion event with two usable options");
                int count = player.Deck.Cards.Count, smalls = player.Deck.Cards.Count(c => c is SmallWave), irons = player.Deck.Cards.Count(c => c is IronWave);
                await fusion.CurrentOptions[0].Chosen();
                Check(fusion.IsFinished && player.Deck.Cards.Count == count - 1 && player.Deck.Cards.Count(c => c is SmallWave) == smalls - 2 &&
                    player.Deck.Cards.Count(c => c is IronWave) == irons + 1 && player.Deck.Cards.All(c => c is not UltimateStrike), "First native fusion callback removes exactly two Small Waves and grants ordinary Iron Wave");
                await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<Amalgamator>(), showTransition: false);
                fusion = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
                var iron = player.Deck.Cards.OfType<IronWave>().First();
                CardCmd.Upgrade(iron);
                CardCmd.Enchant<Momentum>(iron, 5);
                await fusion.CurrentOptions[1].Chosen();
                var reforged = player.Deck.Cards.OfType<ScorchingIronWave>().Single();
                Check(fusion.IsFinished && !player.Deck.Cards.Contains(iron) && reforged.CurrentUpgradeLevel == 1 &&
                    reforged.DynamicVars.Block.BaseValue == 7 && reforged.Enchantment is Momentum { Amount: 5 },
                    "Second fusion option transforms upgraded Iron Wave into exactly Scorching+ and preserves enchantment");
                foreach (var extra in player.Deck.Cards.OfType<IronWave>().ToArray()) await CardPileCmd.RemoveFromDeck(extra, false);
                await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<Amalgamator>(), showTransition: false);
                fusion = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
                Check(fusion.CurrentOptions[1].IsLocked && !fusion.CurrentOptions[0].IsLocked,
                    "Missing ordinary Iron Wave locks only the second option");
                foreach (var c in player.Deck.Cards.OfType<SmallWave>()) c.AddKeyword(CardKeyword.Eternal);
                Check(!ModelDb.Event<Amalgamator>().IsAllowed(run), "Fusion is excluded when neither ingredient set is available");
                var toothSource = run.CreateCard<IronWave>(player);
                CardCmd.Upgrade(toothSource);
                CardCmd.Enchant<Momentum>(toothSource, 5);
                await CardPileCmd.Add(toothSource, PileType.Deck, skipVisuals: true);
                var tooth = (ArchaicTooth)ModelDb.Relic<ArchaicTooth>().ToMutable();
                Check(tooth.SetupForPlayer(player) && tooth.AncientCard!.Id == ModelDb.GetId<ScorchingIronWave>(),
                    "Native Tooth setup previews Scorching Iron Wave");
                var preview = run.LoadCard(tooth.AncientCard!, player);
                Check(preview.CurrentUpgradeLevel == 3 && preview.DynamicVars.Block.BaseValue == 14 && preview.Enchantment is Momentum { Amount: 5 },
                    "Tooth preview is exactly three upgrades, not three plus the removed starter's upgrade");
                await RelicCmd.Obtain(tooth, player);
                Check(!player.Deck.Cards.Contains(toothSource) && player.Deck.Cards.OfType<ScorchingIronWave>()
                    .Any(c => c.CurrentUpgradeLevel == 3 && c.DynamicVars.Block.BaseValue == 14 && c.Enchantment is Momentum { Amount: 5 }),
                    "Actually obtaining Tooth applies the same three-upgrade replacement as its preview");
                Check(tooth.DynamicDescription.GetFormattedText().Contains("强化") && tooth.DynamicDescription.GetFormattedText().Contains("3次") &&
                    !tooth.DynamicDescription.GetFormattedText().Contains("巨大"), "Tooth's acquired tooltip agrees with its actual replacement");
            }
            var nativeStrike = run.CreateCard<StrikeIronclad>(native);
            var deletedId = new ModelId("CARD", "PUNCHCONSTRUCT-GIANT_IRON_WAVE");
            Check(ModelDb.GetByIdOrNull<CardModel>(deletedId) == null && !player.Character.CardPool.AllCardIds.Contains(deletedId),
                "Removed ancient wave is absent from both the model database and acquisition pool");
            var oldSaved = run.CreateCard<ScorchingIronWave>(player).ToSerializable();
            oldSaved.Id = deletedId;
            Check(run.LoadCard(oldSaved, player) is ScorchingIronWave,
                "Old saved copies migrate to Scorching Iron Wave without registering the deleted model");
            AddedRelicWeightChecks();
            MassiveScrollExclusionChecks();
            Check(ModelDb.Enchantment<Spiral>().CanEnchant(nativeStrike), "Native Ironclad starter retains Spiral eligibility");
            Check(RelicWeights.Preferred.Count == 13 && new RelicModel[] { ModelDb.Relic<OpenEndWrench>(), ModelDb.Relic<SpinningScrew>(),
                ModelDb.Relic<SpareKnuckles>(), ModelDb.Relic<EmergencyOilCan>() }.All(r => RelicWeights.Weight(r) == 2),
                "All thirteen approved relic IDs include the four new workshop relics at weight two");
            using var audio = CharacterSelectAudio.Load();
            Check(Math.Abs(audio.GetLength() - .78) < .001 && audio.MixRate == 44100 && audio.Data.Length == 68796,
                "Packed selection cue matches the original 0.78-second mono PCM16 version restored for author audition");
            Check(ModelDb.Character<ConstructCharacter>().CharacterSelectSfx == CharacterSelectAudio.Cue &&
                ModelDb.Character<Defect>().CharacterSelectSfx != CharacterSelectAudio.Cue, "Construct cue is independent of native Defect selection");
            Check(CharacterSelectAudio.Play(1) == null, "Muted QA settings prevent selection audio playback");
            await SelectionAudioRouteCheck();
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<PunchConstructNormal>().ToMutable(), showTransition: false);
            await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Draft damage fixture");
            var enemy = player.Creature.CombatState!.HittableEnemies.Single();
            await CreatureCmd.SetMaxAndCurrentHp(enemy, 1000);
            foreach (var power in enemy.Powers.ToArray()) await PowerCmd.Remove(power);
            player.Creature.LoseBlockInternal(player.Creature.Block);
            enemy.LoseBlockInternal(enemy.Block);
            // The earlier event consumed the ordinary starter. Restore one only
            // inside this explicit QA fixture for the acquisition isolation check.
            await CardPileCmd.Add(run.CreateCard<IronWave>(player), PileType.Deck, skipVisuals: true);
            await ScorchingWaveChecks(player, enemy);
            await PowerCmd.Apply<NeverBackDownPower>(Context, player.Creature, 1, player.Creature, run.CreateCard<NeverBackDown>(player));
            var neverPlus = run.CreateCard<NeverBackDown>(player); CardCmd.Upgrade(neverPlus);
            await PowerCmd.Apply<NeverBackDownPower>(Context, player.Creature, 1, player.Creature, neverPlus);
            var neverPower = player.Creature.GetPower<NeverBackDownPower>()!;
            Check(StatusText(neverPower).Contains("2张[gold]拳斗[/gold]") && !StatusText(neverPower).Contains("拳斗+") &&
                StatusText(neverPower).Contains("2张[gold]灼烧[/gold]"), "Stacked bottom status matches the two ordinary generated fists and Burns");
            await NGame.Instance!.ReturnToMainMenu();
            SaveManager.Instance.DeleteCurrentRun();
        }
        finally { Engine.TimeScale = speed; }
    }

    private static void AddedRelicWeightChecks()
    {
        var construct = Player.CreateForNewRun<ConstructCharacter>(new UnlockState([], [], 0), 1234);
        var native = Player.CreateForNewRun<Ironclad>(new UnlockState([], [], 0), 5678);
        _ = RunState.CreateForTest([construct], seed: "NEWRELICWEIGHTS");
        _ = RunState.CreateForTest([native], seed: "NATIVERELICWEIGHTS");
        foreach (var target in new RelicModel[] { ModelDb.Relic<OpenEndWrench>(), ModelDb.Relic<SpinningScrew>(),
            ModelDb.Relic<SpareKnuckles>(), ModelDb.Relic<EmergencyOilCan>() })
        {
            var control = ModelDb.AllRelics.First(r => r.GetType().Assembly == typeof(Anchor).Assembly && r.Rarity == target.Rarity &&
                RelicWeights.Weight(r) == 1 && RelicWeights.Allowed(r));
            int Sample(Player owner, bool shop)
            {
                int count = 0;
                for (ulong seed = 0; seed < 600; seed++)
                {
                    var bag = new RelicGrabBag(); bag.Populate([target, control], new Rng(seed));
                    AccessTools.Field(typeof(Player), "<RelicGrabBag>k__BackingField").SetValue(owner, bag);
                    var result = shop ? RelicFactory.PullNextRelicFromBack(owner, target.Rarity, _ => true)
                        : RelicFactory.PullNextRelicFromFront(owner, target.Rarity);
                    if (result?.Id == target.Id) count++;
                }
                return count;
            }
            int reward = Sample(construct, false), shop = Sample(construct, true), nativeDraws = Sample(native, false);
            Check(reward is > 350 and < 450 && shop is > 350 and < 450 && nativeDraws is > 250 and < 350,
                $"Real reward/shop draws use two-to-one weights only for Construct: {target.Id} reward={reward}, shop={shop}, native={nativeDraws}");
        }
    }

    private static void MassiveScrollExclusionChecks()
    {
        var unlocks = new UnlockState([], [], 0);
        var construct = Player.CreateForNewRun<ConstructCharacter>(unlocks, 1);
        var native = Player.CreateForNewRun<Ironclad>(unlocks, 2);
        var run = RunState.CreateForTest([construct, native], seed: "MASSIVESCROLL");
        var scroll = ModelDb.Relic<MassiveScroll>();
        Check(scroll.IsAllowed(run), "Massive Scroll remains natively eligible in a two-player run");
        int constructScrolls = 0, constructCirclets = 0, nativeDraws = 0;
        for (ulong seed = 0; seed < 64; ++seed)
        {
            foreach (var (owner, front) in new[] { (construct, true), (construct, false), (native, true), (native, false) })
            {
                var bag = new RelicGrabBag();
                bag.Populate([scroll], new Rng(seed));
                AccessTools.Field(typeof(Player), "<RelicGrabBag>k__BackingField").SetValue(owner, bag);
                var result = front ? RelicFactory.PullNextRelicFromFront(owner, scroll.Rarity)
                    : RelicFactory.PullNextRelicFromBack(owner, scroll.Rarity, _ => true);
                if (owner == construct)
                {
                    if (result is MassiveScroll) ++constructScrolls;
                    else if (result is Circlet) ++constructCirclets;
                }
                else if (result is MassiveScroll) ++nativeDraws;
            }
        }
        Check(constructScrolls == 0, "Construct reward and shop draws never return Massive Scroll across 64 seeds");
        Check(constructCirclets == 128, "Construct draws fall back to the native Circlet instead of Massive Scroll across 64 seeds");
        Check(nativeDraws == 128, "Native Ironclad reward and shop draws keep Massive Scroll across 64 seeds");
        Check(RelicFactoryScope.CurrentPlayer == null && RelicFactoryScope.CurrentRng == null,
            "Massive Scroll exclusion restores the relic draw scope");
        var neow = ModelDb.Event<Neow>().ToMutable();
        var scrollOption = EventOption.FromRelic(ModelDb.Relic<MassiveScroll>().ToMutable(), neow, null!, "QA");
        var anchorOption = EventOption.FromRelic(ModelDb.Relic<Anchor>().ToMutable(), neow, null!, "QA");
        Check(NeowScrollExclusion.Filter([scrollOption, anchorOption], construct).All(o => o.Relic is not MassiveScroll),
            "Construct Neow blessing list hides the Massive Scroll option");
        Check(NeowScrollExclusion.Filter([scrollOption, anchorOption], native).Count == 2,
            "Native Ironclad Neow blessing list keeps the Massive Scroll option");
    }

    private static async Task SelectionAudioRouteCheck()
    {
        int bus = AudioServer.GetBusIndex("Master");
        bool mute = AudioServer.IsBusMute(bus);
        var settings = SaveManager.Instance.SettingsSave;
        float master = settings.VolumeMaster, sfx = settings.VolumeSfx;
        try
        {
            AudioServer.SetBusMute(bus, true);
            settings.VolumeMaster = .5f; settings.VolumeSfx = .5f;
            SfxCmd.Play(CharacterSelectAudio.Cue);
            SfxCmd.Play(CharacterSelectAudio.Cue);
            var players = NGame.Instance!.GetChildren().OfType<AudioStreamPlayer>()
                .Where(p => p.Name.ToString().StartsWith("ConstructSelectSound") && !p.IsQueuedForDeletion()).ToArray();
            Check(players.Length == 1 && players[0].Playing && players[0].Bus == "SFX" && players[0].VolumeDb == 0,
                "Real SfxCmd route plays custom cue once on SFX bus without double applying slider gain");
            await Frames(2);
            settings.VolumeSfx = 0;
            Check(CharacterSelectAudio.Play(1) == null, "SFX slider zero suppresses the custom cue");
            players[0].Stop(); players[0].QueueFree();
        }
        finally
        {
            settings.VolumeMaster = master; settings.VolumeSfx = sfx;
            AudioServer.SetBusMute(bus, mute);
        }
    }
}
