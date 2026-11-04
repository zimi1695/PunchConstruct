using Godot;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using HarmonyLib;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private sealed class ScrySelector(int count) : ICardSelector
    {
        internal CardModel[] Offered = [];
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> cards, int minSelect, int maxSelect)
        {
            Offered = cards.ToArray();
            return Task.FromResult<IEnumerable<CardModel>>(Offered.Take(count));
        }
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> cards, IReadOnlyList<CardRewardAlternative> alternatives) => throw new InvalidOperationException("Unexpected reward selector");
    }

    private static async Task ObserverChecks()
    {
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Watcher") &&
            !typeof(ConstructCharacter).Assembly.GetReferencedAssemblies().Any(a => a.Name == "Watcher"),
            "No Watcher assembly loaded or referenced");
        var cards = new CardModel[] { ModelDb.Card<ObserverJustLucky>(), ModelDb.Card<ObserverSashWhip>(), ModelDb.Card<ObserverProstrate>(),
            ModelDb.Card<ObserverTalkToTheHand>(), ModelDb.Card<ObserverWallop>(), ModelDb.Card<ObserverColdObservation>(),
            ModelDb.Card<ObserverSwivel>(), ModelDb.Card<ObserverFasting>() };
        foreach (var card in cards)
        {
            var upgrade = card.ToMutable(); upgrade.UpgradeInternal(); upgrade.FinalizeUpgradeInternal();
            Check(card.Pool is ObserverOriginPool && card.Portrait != null && card.FrameMaterial != null && card.EnergyIcon != null &&
                upgrade.Portrait != null && upgrade.FrameMaterial != null && card.FrameMaterial != ModelDb.Card<SmallWave>().FrameMaterial,
                "Independent purple origin, portraits and energy icons load: " + card.Title);
        }
        PowerModel[] powers = [ModelDb.Power<ObserverMantra>(), ModelDb.Power<ObserverDivinity>(), ModelDb.Power<ObserverBlockReturn>(),
            ModelDb.Power<ObserverColdObservationPower>(), ModelDb.Power<ObserverEnergyDown>()];
        Check(powers.All(p => ResourceLoader.Load<Texture2D>(p.PackedIconPath) != null && ResourceLoader.Load<Texture2D>(((ObserverPower)p).CustomBigIconPath) != null),
            "Five independent support power icons load");
        foreach (var (oldId, type) in ObserverSaveMigration.OldCards)
        {
            var original = ModelDb.GetById<CardModel>(ModelDb.GetId(type)).ToMutable();
            original.UpgradeInternal(); original.FinalizeUpgradeInternal();
            if (original.Type == CardType.Attack) CardCmd.Enchant<Momentum>(original, 5);
            var save = original.ToSerializable(); save.Id = new ModelId("CARD", oldId);
            var restored = CardModel.FromSerializable(save);
            Check(restored.GetType() == type && restored.IsUpgraded &&
                (original.Type != CardType.Attack || restored.Enchantment is Momentum { Amount: 5 }),
                "Legacy card ID restores upgrade and enchantment without Watcher: " + oldId);
        }

        await ReliabilityRun("observer-standalone");
        var player = RunManager.Instance!.DebugOnlyGetState()!.Players[0];
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Observer debug room ready");
        var enemies = player.Creature.CombatState!.HittableEnemies.ToArray();
        foreach (var enemy in enemies) await CreatureCmd.SetMaxAndCurrentHp(enemy, 2000);
        var target = enemies[0]; var owner = player.Creature; var state = (CombatState)owner.CombatState!;
        var rewardCandidates = CardCreationOptions.ForNonCombatWithUniformOdds([player.Character.CardPool]).GetPossibleCards(player).ToArray();
        Check(ModelDb.Card<ObserverColdObservation>().MultiplayerConstraint == CardMultiplayerConstraint.None &&
            cards.All(card => rewardCandidates.Any(candidate => candidate.Id == card.Id)),
            "All eight independent cards, including Cold Observation, enter actual single-player reward candidates");
        await CardCmd.Discard(Context, player.PlayerCombatState!.Hand.Cards.ToArray());

        // Actual Prostrate ordering and repeated thresholds, not direct mutation.
        await PowerCmd.Apply<ObserverMantra>(Context, owner, 8, owner, null);
        int block = owner.Block;
        await Play<ObserverProstrate>(player, upgraded: true);
        Check(owner.GetPower<ObserverMantra>()?.Amount == 1 && owner.HasPower<ObserverDivinity>() && player.PlayerCombatState.Energy == 23 && owner.Block == block + 4,
            "Upgraded Prostrate crosses ten Mantra, keeps remainder, gains three energy then four block");
        await Frames(12);
        var divineVisual = ObserverDivinityVfx.Find(owner);
        Check(!owner.GetPower<ObserverDivinity>()!.IsVisible,
            "Divinity matches original hidden stance: no bottom status icon or visible-power entry wait");
        Check(divineVisual is { Visible: true } && divineVisual.GetParent() == MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance!.GetCreatureNode(owner)!.Visuals,
            "Divinity attaches visible effects to the actual owner visual");
        Check(divineVisual!.GetChildren().OfType<CpuParticles2D>().Count(p => p.Emitting && p.Texture != null && p.Material != null) == 2,
            "Native-rendered purple fog and spark emitters have original textures and additive materials");
        Check(divineVisual.GetChildren().OfType<CpuParticles2D>().Single(p => p.Amount == 14) is { LifetimeRandomness: .4 } glow &&
            glow.ColorRamp.GetColor(0).A == .4f && glow.ColorRamp.GetColor(1).A == 0,
            "Original glow lifetime randomness and two-stop color fade are preserved");
        await Until(() => divineVisual.GetChildren().OfType<Sprite2D>().Any(s => s.HasMeta("observer_divinity_eye") && s.Texture != null), "Original eye grace and first spawn");
        Check(divineVisual.GetChildren().OfType<Sprite2D>().Any(s => s.HasMeta("observer_divinity_eye") && s.Texture != null && s.ZIndex == 50),
            "Divinity spawns original animated eye frames");
        Check(divineVisual.GetChildren().OfType<Sprite2D>().Where(s => s.HasMeta("observer_divinity_eye")).All(s => s.TopLevel && s.GlobalScale.X > 0),
            "Original world-space eyes keep their orientation regardless of actor facing");
        Check(Enumerable.Range(0, 7).All(i => ObserverDivinityVfx.Texture("eye" + i) != null) &&
            ResourceLoader.Load<Shader>("res://PunchConstruct/observer_vfx/divinity_border.gdshader") != null,
            "Seven eye animation frames and entry border shader load without Watcher");
        Check(ObserverDivinityVfx.EntryRayTexture() is AtlasTexture { Region: var region } && region == new Rect2(1402, 19, 122, 17),
            "Entry rays use the original strikeLine2 atlas region instead of generic spark dots");
        Check(new[] { "divinity", "mantra" }.All(s => AudioStreamOggVorbis.LoadFromBuffer(Godot.FileAccess.GetFileAsBytes("res://PunchConstruct/observer_vfx/" + s + ".ogg")) is { } audio && audio.GetLength() > 0) &&
            !Descendants(divineVisual).OfType<AudioStreamPlayer>().Any(),
            "Both original stance sounds decode, and muted QA creates no audio playback");
        await Capture("qa-observer-divinity");
        await ObserverAudioChecks(divineVisual);
        int hp = target.CurrentHp; block = owner.Block;
        await Play<ObserverWallop>(player, target);
        Check(hp - target.CurrentHp == 27 && owner.Block - block == 27,
            "Divinity triples Wallop attack and Wallop gains actual unblocked damage as block");
        var divine = owner.GetPower<ObserverDivinity>()!;
        Check(divine.ModifyDamageMultiplicative(target, 10, ValueProp.Unpowered, owner, null, null) == 1 &&
            divine.ModifyDamageMultiplicative(owner, 10, ValueProp.Move, target, null, null) == 1,
            "Divinity does not triple unpowered damage or enemy attacks");
        await PlayerCmd.SetEnergy(7, player);
        await PowerCmd.Apply<ObserverMantra>(Context, owner, 10, owner, null);
        Check(owner.GetPower<ObserverMantra>()?.Amount == 1 && player.PlayerCombatState.Energy == 7,
            "Crossing Mantra again while already divine does not grant duplicate entry energy");
        await NextTurn(player, target);
        Check(!owner.HasPower<ObserverDivinity>() && owner.GetPower<ObserverMantra>()?.Amount == 1,
            "Divinity has exited by the next owner turn while remaining Mantra persists");
        Check(ObserverDivinityVfx.Find(owner) == null, "Turn exit clears particles, eyes, timers and entry border together");
        await PowerCmd.Apply<ObserverMantra>(Context, owner, 24, owner, null);
        Check(owner.GetPower<ObserverMantra>()?.Amount == 15 && owner.HasPower<ObserverDivinity>(),
            "Original Mantra rule deducts ten once per callback, retaining fifteen from a twenty-five total");
        await owner.GetPower<ObserverDivinity>()!.AfterSideTurnEnd(Context, owner.Side, [owner]);
        Check(!owner.HasPower<ObserverDivinity>() && ObserverDivinityVfx.Find(owner) == null,
            "Owner turn-end hook exits Divinity immediately, before the enemy turn starts");
        await PowerCmd.Apply<ObserverMantra>(Context, owner, 1, owner, null);
        Check(owner.GetPowerAmount<ObserverMantra>() == 6 && owner.HasPower<ObserverDivinity>(),
            "The next Mantra gain resolves one further threshold and re-enters Divinity");
        await PowerCmd.Remove(owner.GetPower<ObserverDivinity>()!);
        await PowerCmd.Remove(owner.GetPower<ObserverMantra>()!);
        await Frames(2);
        Check(ObserverDivinityVfx.Find(owner) == null, "Explicit removal clears the second divinity entry");
        var entryClock = System.Diagnostics.Stopwatch.StartNew();
        var entry = PowerCmd.Apply<ObserverDivinity>(Context, owner, 1, owner, null);
        Check(ObserverDivinityVfx.Find(owner) != null,
            "Hidden stance starts its entry burst in the same call, before waiting for command completion");
        var rays = ObserverDivinityVfx.Find(owner)!.GetChildren().OfType<Sprite2D>()
            .Where(s => s.Texture is AtlasTexture { Region: var region } && region == new Rect2(1402, 19, 122, 17)).ToArray();
        Check(rays.Length == 20 && rays.All(s => s.Visible && s.Modulate.A == 1 && s.TopLevel),
            "All twenty original pink rays flash at the center immediately, before randomized inward-motion delays");
        var entryMs = entryClock.Elapsed.TotalMilliseconds;
        await entry;
        Godot.GD.Print($"[Construct OBSERVER ENTRY] immediate VFX {entryMs:F1}ms; command {entryClock.Elapsed.TotalMilliseconds:F1}ms");
        Check(ObserverDivinityVfx.Find(owner) != null, "Reentry recreates the stance visuals after cleanup");
        await PowerCmd.Remove(owner.GetPower<ObserverDivinity>()!);
        await Frames(2);
        Check(!Descendants(MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance!).Any(n => n.Name.ToString() == "ObserverDivinityVfx"),
            "Repeated entry/removal leaves no orphaned stance nodes in the combat room");

        // Preview top two, choose one, preserve all unchosen draw order.
        var draw = player.PlayerCombatState.DrawPile;
        await CardPileCmd.AddGeneratedCardToCombat(state.CreateCard<SmallWave>(player), PileType.Draw, player, CardPilePosition.Top);
        await CardPileCmd.AddGeneratedCardToCombat(state.CreateCard<SmallShockwave>(player), PileType.Draw, player, CardPilePosition.Top);
        var beforeDraw = draw.Cards.ToArray(); var selector = new ScrySelector(1);
        block = owner.Block; hp = target.CurrentHp;
        using (CardSelectCmd.UseSelector(selector)) await Play<ObserverJustLucky>(player, target, upgraded: true);
        Check(selector.Offered.SequenceEqual(beforeDraw.Take(2)) && selector.Offered[0].Pile?.Type == PileType.Discard &&
            draw.Cards.SequenceEqual(beforeDraw.Skip(1)) && owner.Block == block + 3 && hp - target.CurrentHp == 4,
            "Lucky+ scries top two before block and damage; chosen card discarded, unchosen order retained");
        selector = new ScrySelector(0); beforeDraw = draw.Cards.ToArray();
        using (CardSelectCmd.UseSelector(selector)) await Play<ObserverJustLucky>(player, target);
        Check(draw.Cards.SequenceEqual(beforeDraw) && selector.Offered.Length == Math.Min(1, beforeDraw.Length),
            "Scry permits keeping every card without shuffling or drawing");

        await Play<SmallShockwave>(player);
        await Play<ObserverSashWhip>(player, target);
        // Shock applied one Weak before the first Whip; a second attack must add one.
        int weak = target.GetPower<WeakPower>()?.Amount ?? 0;
        await Play<ObserverSashWhip>(player, target, upgraded: true);
        Check(target.GetPower<WeakPower>()?.Amount == weak + 2, "Sash Whip+ applies two Weak after an attack");
        await Play<ObserverProstrate>(player);
        weak = target.GetPower<WeakPower>()?.Amount ?? 0;
        await Play<ObserverSashWhip>(player, target);
        Check(target.GetPower<WeakPower>()?.Amount == weak, "Sash Whip does not apply Weak after a skill");

        await PowerCmd.Remove(target.GetPower<VulnerablePower>()!);
        var talk = await Play<ObserverTalkToTheHand>(player, target);
        Check(talk.Pile?.Type == PileType.Exhaust && target.GetPower<ObserverBlockReturn>()?.Amount == 2,
            "Talk to the Hand exhausts and applies the persistent target debuff");
        block = owner.Block;
        await Play<FightMe>(player, target);
        Check(owner.Block - block == 4, "Talk to the Hand grants block on both Fight Me attack hits");
        await PowerCmd.Remove(target.GetPower<ObserverBlockReturn>()!);
        await PowerCmd.Remove(owner.GetPower<DexterityPower>()!);

        await Play<ObserverColdObservation>(player, upgraded: true);
        Check(owner.GetPower<ObserverColdObservationPower>()?.Amount == 2, "Cold Observation+ applies two attack uses");
        block = owner.Block; hp = target.CurrentHp;
        await Play<FightMe>(player, target);
        Check(owner.Block - block == 12 && hp - target.CurrentHp == 12 && owner.GetPower<ObserverColdObservationPower>()?.Amount == 1,
            "Cold Observation covers both hits of Fight Me and consumes one use");
        var attack = player.PlayerCombatState.DiscardPile.Cards.Last(c => c is FightMe);
        await CardPileCmd.Add(attack, PileType.Hand);
        block = owner.Block;
        await PlayerCmd.SetEnergy(20, player);
        Check(attack.TryManualPlay(target), "The same physical attack card can be played again");
        await Until(() => attack.Pile?.Type == PileType.Discard, "Repeated physical card resolution"); await Frames(15);
        Check(owner.Block - block == 12 && !owner.HasPower<ObserverColdObservationPower>(),
            "Replaying the same physical card consumes the final Cold Observation use and covers all hits");
        await PowerCmd.Remove(owner.GetPower<DexterityPower>()!);

        await Play<ObserverSwivel>(player, upgraded: true);
        var costly = state.CreateCard<ObserverWallop>(player);
        await CardPileCmd.AddGeneratedCardToCombat(costly, PileType.Hand, player);
        await PlayerCmd.SetEnergy(0, player);
        Check(costly.EnergyCost.GetWithModifiers(CostModifiers.All) == 0 && costly.TryManualPlay(target), "Swivel makes a two-cost attack playable at zero energy");
        await Until(() => costly.Pile?.Type == PileType.Discard, "Free attack resolution"); await Frames(15);
        Check(!owner.HasPower<FreeAttackPower>(), "Native free attack charge is consumed after use");

        await Play<ObserverFasting>(player, upgraded: true);
        Check(owner.GetPower<StrengthPower>()?.Amount == 4 && owner.GetPower<DexterityPower>()?.Amount == 4 && owner.GetPower<ObserverEnergyDown>()?.Amount == 1,
            "Fasting+ grants four Strength, four Dexterity and one energy penalty");
        await NextTurn(player, target);
        Check(player.PlayerCombatState.Energy == 2, "Fasting reduces actual next-turn energy from three to two");
        if (OS.GetCmdlineArgs().Contains("--punchconstruct-visual"))
        {
            await CardCmd.Discard(Context, player.PlayerCombatState.Hand.Cards.ToArray());
            foreach (var card in cards)
                await CardPileCmd.AddGeneratedCardToCombat(state.CreateCard(card, player), PileType.Hand, player);
            await Frames(30);
            await Capture("qa-independent-observer");
        }
    }

    private static async Task ObserverAudioChecks(Node effects)
    {
        // Exercise real decoding, player startup and mixing while the output bus
        // remains muted. Never enable sound on the user's computer or save QA settings.
        int master = AudioServer.GetBusIndex("Master"), sfx = AudioServer.GetBusIndex("SFX");
        Check(master >= 0 && sfx >= 0, "Native Master and SFX audio buses exist");
        var settings = SaveManager.Instance.SettingsSave;
        float oldMaster = settings.VolumeMaster, oldSfx = settings.VolumeSfx;
        bool wasMuted = AudioServer.IsBusMute(master);
        float oldMasterDb = AudioServer.GetBusVolumeDb(master), oldSfxDb = AudioServer.GetBusVolumeDb(sfx);
        var capture = new AudioEffectCapture { BufferLength = 1 };
        int effectIndex = AudioServer.GetBusEffectCount(sfx);
        AudioServer.SetBusMute(master, true);
        AudioServer.AddBusEffect(sfx, capture);
        try
        {
            settings.VolumeMaster = settings.VolumeSfx = .5f;
            AudioServer.SetBusVolumeDb(master, Mathf.LinearToDb(.25f));
            AudioServer.SetBusVolumeDb(sfx, Mathf.LinearToDb(.25f));
            foreach (string sound in new[] { "mantra", "divinity" })
            {
                capture.ClearBuffer();
                var player = ObserverDivinityVfx.PlaySound(effects, sound);
                Check(player is { Playing: true } && player.Bus == "SFX" && player.VolumeDb == 0 && player.GetParent() == MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance,
                    "Actual stance one-shot starts on native SFX with no duplicate slider gain: " + sound);
                float peak = 0;
                for (int frame = 0; frame < 90 && peak == 0; frame++)
                {
                    await Frames(1);
                    foreach (var sample in capture.GetBuffer(capture.GetFramesAvailable()))
                        peak = Math.Max(peak, Math.Max(Math.Abs(sample.X), Math.Abs(sample.Y)));
                }
                Check(peak > .0001f && AudioServer.IsBusMute(master),
                    "Real audio mixer receives nonzero stance samples while speaker output stays muted: " + sound);
                Godot.GD.Print($"[Construct OBSERVER AUDIO] {sound} peak={peak:F6}; driver={AudioServer.GetDriverName()}; masterMuted=true");
                if (GodotObject.IsInstanceValid(player)) { player!.Stop(); player.QueueFree(); }
            }
            settings.VolumeMaster = 0;
            Check(ObserverDivinityVfx.PlaySound(effects, "divinity") == null, "Master zero prevents stance playback");
            settings.VolumeMaster = .5f; settings.VolumeSfx = 0;
            Check(ObserverDivinityVfx.PlaySound(effects, "mantra") == null, "SFX zero prevents stance playback");
        }
        finally
        {
            settings.VolumeMaster = oldMaster; settings.VolumeSfx = oldSfx;
            AudioServer.RemoveBusEffect(sfx, effectIndex);
            AudioServer.SetBusVolumeDb(master, oldMasterDb); AudioServer.SetBusVolumeDb(sfx, oldSfxDb);
            AudioServer.SetBusMute(master, wasMuted);
        }
    }

    private static async Task ObserverPersistence(NMainMenu menu, bool resume)
    {
        Check(!AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "Watcher"), "Legacy save fixture runs without Watcher");
        if (!resume)
        {
            var run = await ReliabilityRun("observer-legacy-save");
            var player = run.Players.Single();
            foreach (var type in ObserverSaveMigration.OldCards.Values)
            {
                var card = run.CreateCard(ModelDb.GetById<CardModel>(ModelDb.GetId(type)), player);
                CardCmd.Upgrade(card);
                if (card.Type == CardType.Attack) CardCmd.Enchant<Momentum>(card, 5);
                await CardPileCmd.Add(card, PileType.Deck, skipVisuals: true);
            }
            await RunManager.Instance!.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<DollRoom>(), showTransition: false);
            await SaveManager.Instance.SaveRun(run.CurrentRoom);
            Check(SaveManager.Instance.LoadRunSave().Success && player.Deck.Cards.Count == 16, "Eight upgraded observer cards written to isolated native run save");
            return;
        }
        Check(SaveManager.Instance.LoadRunSave().Success && !RunManager.Instance!.IsInProgress, "Fresh process sees the legacy-ID run save");
        await (Task)AccessTools.Method(typeof(NMainMenu), "OnContinueButtonPressedAsync").Invoke(menu, [])!;
        await Frames(90);
        var restored = RunManager.Instance!.DebugOnlyGetState()!.Players.Single();
        foreach (var type in ObserverSaveMigration.OldCards.Values)
        {
            var card = restored.Deck.Cards.Single(c => c.GetType() == type);
            Check(card.IsUpgraded && (card.Type != CardType.Attack || card.Enchantment is Momentum { Amount: 5 }),
                "Native Continue migrates old ID and retains upgrade/enchantment: " + card.Title);
        }
        Check(restored.Deck.Cards.Count == 16 && restored.Character is ConstructCharacter, "Fresh-process Continue retains character and complete sixteen-card fixture deck");
    }
}
