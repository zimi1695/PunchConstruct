using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private sealed class SpecificHandSelector(CardModel? target, bool cancel = false) : ICardSelector
    {
        public CardModel[] Offered = [];
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> cards, int min, int max)
        {
            Offered = cards.ToArray();
            return Task.FromResult<IEnumerable<CardModel>>(cancel ? [] : Offered.Where(c => target == null ? c is Burn : ReferenceEquals(c, target)).Take(1));
        }
        public CardRewardSelection GetSelectedCardReward(IReadOnlyList<CardCreationResult> cards, IReadOnlyList<CardRewardAlternative> alternatives)
            => throw new InvalidOperationException("Unexpected reward selector");
    }

    // Installed only by this explicit QA mode; observes the real native audio call.
    private sealed class TransformSoundProbe : IDisposable
    {
        private readonly Harmony _patch = new("PunchConstruct.QA.TransformSound");
        private static int _plays;
        private static NCard? _visual;
        private static CardModel? _replacement;
        private static ulong _createdFrame;
        private static bool _lastAligned;
        internal int Plays => _plays;
        internal bool SoundAtReveal => _lastAligned;
        internal TransformSoundProbe()
        {
            _plays = 0;
            _visual = null; _replacement = null; _lastAligned = false;
            _patch.Patch(AccessTools.Method(typeof(NAudioManager), nameof(NAudioManager.PlayOneShot), [typeof(string), typeof(float)]),
                postfix: new HarmonyMethod(typeof(TransformSoundProbe), nameof(Observe)));
            _patch.Patch(AccessTools.Method(typeof(NCardTransformShineVfx), nameof(NCardTransformShineVfx.Create)),
                postfix: new HarmonyMethod(typeof(TransformSoundProbe), nameof(ObserveShine)));
        }
        private static void ObserveShine(NCard __0, CardModel __1, NCardTransformShineVfx? __result)
        {
            if (__result == null) return;
            _visual = __0; _replacement = __1; _createdFrame = Engine.GetProcessFrames();
        }
        private static void Observe(string path)
        {
            if (path != "event:/sfx/ui/cards/card_transform") return;
            _plays++;
            _lastAligned = _visual != null && GodotObject.IsInstanceValid(_visual) && _visual.IsVisibleInTree() &&
                ReferenceEquals(_visual.Model, _replacement) && Engine.GetProcessFrames() > _createdFrame;
            GD.Print($"[Construct TRANSFORM AUDIO] frame={Engine.GetProcessFrames()}; created={_createdFrame}; visualReplaced={_lastAligned}");
        }
        public void Dispose() => _patch.UnpatchAll(_patch.Id);
    }

    private static int VisibleDiscardCount() => int.Parse(((Label)AccessTools.Field(typeof(NCombatCardPile), "_countLabel")
        .GetValue(NCombatRoom.Instance!.Ui.DiscardPile)!).Text);

    private static async Task CheckDiscardCounter(MegaCrit.Sts2.Core.Entities.Players.Player player, string scenario)
    {
        await Until(() => VisibleDiscardCount() == player.PlayerCombatState!.DiscardPile.Cards.Count,
            "Discard label catches up after animations: " + scenario);
        Check(VisibleDiscardCount() == player.PlayerCombatState!.DiscardPile.Cards.Count,
            "Actual discard button label equals the model count: " + scenario);
    }

    private static async Task ClockworkChecks(Player player, Creature[] enemies, CombatState state)
    {
        var unlocks = new UnlockState([], [], 0);
        var native = Player.CreateForNewRun<Ironclad>(unlocks, 4242);
        _ = RunState.CreateForTest([native], seed: "CLOCKWORKNATIVE");
        CharacterModel[] nativeCharacters = [ModelDb.Character<Ironclad>(), ModelDb.Character<Silent>(),
            ModelDb.Character<Regent>(), ModelDb.Character<Necrobinder>(), ModelDb.Character<Defect>()];
        CardModel[] newCards = [ModelDb.Card<ConstructClockworkMechanism>(), ModelDb.Card<ConstructMechanicalAscension>(),
            ModelDb.Card<ConstructBurnOut>(), ModelDb.Card<ConstructDomineering>(), ModelDb.Card<ConstructCoreSurge>(),
            ModelDb.Card<ConstructPanacea>()];
        Check(newCards.All(c => player.Character.CardPool.AllCardIds.Contains(c.Id)),
            "All six new cards are registered in the construct acquisition pool");
        Check(nativeCharacters.All(ch => newCards.All(c => !ch.CardPool.AllCardIds.Contains(c.Id))),
            "No native character pool contains a new construct card");
        Check(ModelDb.Card<ConstructClockworkMechanism>() is { Type: CardType.Power, Rarity: CardRarity.Uncommon } &&
            ModelDb.Card<ConstructMechanicalAscension>() is { Type: CardType.Attack, Rarity: CardRarity.Common } &&
            ModelDb.Card<ConstructBurnOut>() is { Type: CardType.Skill, Rarity: CardRarity.Common } &&
            ModelDb.Card<ConstructBurnOut>().EnergyCost.Canonical == 0 &&
            ModelDb.Card<ConstructDomineering>() is { Type: CardType.Skill, Rarity: CardRarity.Ancient } &&
            ModelDb.Card<ConstructCoreSurge>() is { Type: CardType.Attack, Rarity: CardRarity.Rare } &&
            ModelDb.Card<ConstructPanacea>() is { Type: CardType.Skill, Rarity: CardRarity.Common },
            "New card types and rarities match the approved roster");
        Check(ModelDb.Card<ConstructClockworkMechanism>().Pool is ColorlessCardPool &&
            ModelDb.Card<ConstructMechanicalAscension>().Pool is ColorlessCardPool &&
            ModelDb.Card<ConstructBurnOut>().Pool is ColorlessCardPool &&
            ModelDb.Card<ConstructDomineering>().Pool is ConstructCardPool &&
            ModelDb.Card<ConstructCoreSurge>().Pool is DefectCardPool &&
            ModelDb.Card<ConstructPanacea>().Pool is ColorlessCardPool,
            "New cards keep their native art pools (original Domineering stays construct) while acquiring only through the construct pool");
        Check(!CardAcquisition.Eligible(player, ModelDb.Card<ConstructDomineering>()) &&
            !CardAcquisition.Eligible(player, ModelDb.Card<ConstructPanacea>()) &&
            new CardModel[] { ModelDb.Card<ConstructClockworkMechanism>(), ModelDb.Card<ConstructMechanicalAscension>(),
                ModelDb.Card<ConstructBurnOut>(), ModelDb.Card<ConstructCoreSurge>() }.All(c => CardAcquisition.Eligible(player, c)),
            "Ancient Domineering and shop-only Panacea stay out of ordinary acquisition while the other four remain eligible");

        var mechPlus = state.CreateCard<ConstructMechanicalAscension>(player);
        mechPlus.UpgradeInternal(); mechPlus.FinalizeUpgradeInternal();
        var clockPlus = state.CreateCard<ConstructClockworkMechanism>(player);
        clockPlus.UpgradeInternal(); clockPlus.FinalizeUpgradeInternal();
        var burnPlus = state.CreateCard<ConstructBurnOut>(player);
        burnPlus.UpgradeInternal(); burnPlus.FinalizeUpgradeInternal();
        var corePlus = state.CreateCard<ConstructCoreSurge>(player);
        corePlus.UpgradeInternal(); corePlus.FinalizeUpgradeInternal();
        var domPlus = state.CreateCard<ConstructDomineering>(player);
        domPlus.UpgradeInternal(); domPlus.FinalizeUpgradeInternal();
        var panaceaPlus = state.CreateCard<ConstructPanacea>(player);
        panaceaPlus.UpgradeInternal(); panaceaPlus.FinalizeUpgradeInternal();
        Check(mechPlus.DynamicVars["Scale"].BaseValue == 8 && mechPlus.Keywords.Contains(CardKeyword.Retain) &&
            ModelDb.Card<ConstructMechanicalAscension>().EnergyCost.Canonical == 3 &&
            clockPlus.EnergyCost.GetWithModifiers(CostModifiers.None) == 1 &&
            burnPlus.EnergyCost.GetWithModifiers(CostModifiers.None) == 0 && burnPlus.DynamicVars["Disintegration"].BaseValue == 1 &&
            corePlus.DynamicVars.Damage.BaseValue == 15 &&
            domPlus.EnergyCost.GetWithModifiers(CostModifiers.None) == 0 &&
            domPlus.DynamicVars["Energy"].BaseValue == 2 && domPlus.DynamicVars["Cards"].BaseValue == 2 &&
            panaceaPlus.DynamicVars["Artifact"].BaseValue == 2,
            "All six upgrades match their designed values");

        var hand = player.PlayerCombatState!.Hand;
        await CreatureCmd.SetCurrentHp(player.Creature, player.Creature.MaxHp);
        foreach (var enemy in enemies.Where(e => e.IsAlive)) await CreatureCmd.SetMaxAndCurrentHp(enemy, 1000);
        await CardCmd.Discard(Context, hand.Cards.ToArray());
        await PowerCmd.Apply<StrengthPower>(Context, player.Creature, 2, player.Creature, null);
        await PowerCmd.Apply<DexterityPower>(Context, player.Creature, 3, player.Creature, null);
        player.Creature.LoseBlockInternal(player.Creature.Block);
        enemies[0].LoseBlockInternal(enemies[0].Block);
        int hp = enemies[0].CurrentHp;
        await Play<ConstructMechanicalAscension>(player, enemies[0]);
        Check(enemies[0].CurrentHp == hp - 22 && player.Creature.Block == 27,
            "Mechanical Ascension scales damage and block by five per Strength and Dexterity including native bonuses");
        await PowerCmd.Remove<StrengthPower>(player.Creature);
        await PowerCmd.Remove<DexterityPower>(player.Creature);

        await CardCmd.Discard(Context, hand.Cards.ToArray());
        await Play<ConstructBurnOut>(player);
        Check(player.Creature.GetPowerAmount<StrengthPower>() == 1 && player.Creature.GetPowerAmount<DexterityPower>() == 1 &&
            player.Creature.GetPowerAmount<DisintegrationPower>() == 3,
            "Burn Out grants one Strength, one Dexterity and three Disintegration");
        await PowerCmd.Remove<StrengthPower>(player.Creature);
        await PowerCmd.Remove<DexterityPower>(player.Creature);
        await PowerCmd.Remove<DisintegrationPower>(player.Creature);
        await CardCmd.Discard(Context, hand.Cards.ToArray());
        await Play<ConstructBurnOut>(player, upgraded: true);
        Check(player.Creature.GetPowerAmount<StrengthPower>() == 1 && player.Creature.GetPowerAmount<DexterityPower>() == 1 &&
            player.Creature.GetPowerAmount<DisintegrationPower>() == 1,
            "Burn Out+ keeps one Strength and one Dexterity and reduces Disintegration to one");
        await PowerCmd.Remove<StrengthPower>(player.Creature);
        await PowerCmd.Remove<DexterityPower>(player.Creature);
        await PowerCmd.Remove<DisintegrationPower>(player.Creature);

        await CardCmd.Discard(Context, hand.Cards.ToArray());
        hp = enemies[0].CurrentHp;
        var core = await Play<ConstructCoreSurge>(player, enemies[0]);
        Check(enemies[0].CurrentHp == hp - 11 && player.Creature.GetPowerAmount<ArtifactPower>() == 1 &&
            core.Keywords.Contains(CardKeyword.Exhaust) && core.Pile?.Type == PileType.Exhaust,
            "Core Surge deals eleven damage, grants one Artifact and exhausts");
        await PowerCmd.Remove<ArtifactPower>(player.Creature);
        hp = enemies[0].CurrentHp;
        await Play<ConstructCoreSurge>(player, enemies[0], upgraded: true);
        Check(enemies[0].CurrentHp == hp - 15, "Core Surge+ deals fifteen damage");
        await PowerCmd.Remove<ArtifactPower>(player.Creature);

        await CardCmd.Discard(Context, hand.Cards.ToArray());
        var panacea = await Play<ConstructPanacea>(player);
        Check(player.Creature.GetPowerAmount<ArtifactPower>() == 1 && panacea.EnergyCost.GetWithModifiers(CostModifiers.None) == 0 &&
            panacea.Pile?.Type == PileType.Exhaust, "Panacea grants one Artifact for zero energy and exhausts");
        await PowerCmd.Remove<ArtifactPower>(player.Creature);
        await Play<ConstructPanacea>(player, upgraded: true);
        Check(player.Creature.GetPowerAmount<ArtifactPower>() == 2, "Panacea+ grants two Artifact");
        await PowerCmd.Remove<ArtifactPower>(player.Creature);

        await CardCmd.Discard(Context, hand.Cards.ToArray());
        player.Creature.LoseBlockInternal(player.Creature.Block);
        var domineering = await Play<ConstructDomineering>(player);
        var domPower = player.Creature.GetPower<ConstructDomineeringPower>();
        Check(domineering.Keywords.Contains(CardKeyword.Retain) && domPower is { Amount: 1 } &&
            domPower.GetDumbHoverTip().Description.Contains("15伤害、15格挡") &&
            domineering.EnergyCost.Canonical == 1 &&
            domineering.DynamicVars["Energy"].BaseValue == 1 && domineering.DynamicVars["Cards"].BaseValue == 1,
            "Domineering costs one, retains, queues one wave of one energy and one draw, and describes its damage and block");
        int[] enemyHp = enemies.Where(e => e.IsAlive).Select(e => e.CurrentHp).ToArray();
        await PlayerCmd.SetEnergy(0, player);
        int domHandBefore = player.PlayerCombatState!.Hand.Cards.Count;
        await domPower!.AfterPlayerTurnStart(Context, player);
        Check(enemies.Where(e => e.IsAlive).Select((e, i) => e.CurrentHp == enemyHp[i] - 15).All(v => v) &&
            player.Creature.Block == 15 && !player.Creature.HasPower<ConstructDomineeringPower>() &&
            player.PlayerCombatState.Energy == 1 && player.PlayerCombatState.Hand.Cards.Count == domHandBefore + 1,
            "Domineering resolves next turn: one energy, one draw, fifteen damage to every enemy and fifteen block once");

        await CardCmd.Discard(Context, hand.Cards.ToArray());
        player.Creature.LoseBlockInternal(player.Creature.Block);
        var domineeringPlus = await Play<ConstructDomineering>(player, upgraded: true);
        var domPowerPlus = player.Creature.GetPower<ConstructDomineeringPower>();
        int[] enemyHpPlus = enemies.Where(e => e.IsAlive).Select(e => e.CurrentHp).ToArray();
        await PlayerCmd.SetEnergy(0, player);
        int handBeforePlus = player.PlayerCombatState!.Hand.Cards.Count;
        await domPowerPlus!.AfterPlayerTurnStart(Context, player);
        Check(domineeringPlus.EnergyCost.GetWithModifiers(CostModifiers.None) == 0 &&
            enemies.Where(e => e.IsAlive).Select((e, i) => e.CurrentHp == enemyHpPlus[i] - 15).All(v => v) &&
            player.Creature.Block == 15 && !player.Creature.HasPower<ConstructDomineeringPower>() &&
            player.PlayerCombatState.Energy == 2 && player.PlayerCombatState.Hand.Cards.Count == handBeforePlus + 2,
            "Domineering+ is free and resolves two energy, two draws, fifteen damage to every enemy and fifteen block");

        await CardCmd.Discard(Context, hand.Cards.ToArray());
        player.Creature.LoseBlockInternal(player.Creature.Block);
        int burns = player.PlayerCombatState!.DiscardPile.Cards.Count(c => c is Burn);
        await Play<ConstructClockworkMechanism>(player);
        var clockPower = player.Creature.GetPower<ClockworkMechanismPower>();
        Check(clockPower is { Amount: 1 }, "Clockwork Mechanism installs one end-turn power");
        await clockPower!.BeforeSideTurnEnd(Context, CombatSide.Player, [player.Creature]);
        Check(player.PlayerCombatState.DiscardPile.Cards.Count(c => c is Burn) == burns + 1,
            "Clockwork Mechanism adds exactly one Burn to the discard pile at end of turn");
        await CardPileCmd.AddGeneratedCardToCombat(state.CreateCard<SmallWave>(player), PileType.Draw, player);
        int vigor = player.Creature.GetPowerAmount<VigorPower>(), block = player.Creature.Block;
        int handBefore = hand.Cards.Count;
        await clockPower.AfterCardDrawn(Context, state.CreateCard<Burn>(player), true);
        Check(player.Creature.GetPowerAmount<VigorPower>() == vigor + 1 && player.Creature.Block == block + 1 &&
            hand.Cards.Count == handBefore + 1,
            "Clockwork Mechanism reacts to a status draw with one Vigor, one block and one card");
        await PowerCmd.Remove<ClockworkMechanismPower>(player.Creature);
        await CardCmd.Discard(Context, hand.Cards.ToArray());
        await PowerCmd.Remove<VigorPower>(player.Creature);

        var ancient = ModelDb.Potion<ConstructAncientPotion>().ToMutable();
        var murky = ModelDb.Potion<ConstructMurkyOil>().ToMutable();
        var stew = ModelDb.Potion<ConstructFightersStew>().ToMutable();
        PotionModel[] newPotions = [ancient, murky, stew];
        Check(newPotions.All(p => p.Pool is ConstructPotionPool) &&
            ancient.Rarity == PotionRarity.Common && ancient.Usage == PotionUsage.CombatOnly && ancient.TargetType == TargetType.Self &&
            murky.Rarity == PotionRarity.Uncommon && murky.Usage == PotionUsage.CombatOnly && murky.TargetType == TargetType.Self &&
            stew.Rarity == PotionRarity.Rare && stew.Usage == PotionUsage.CombatOnly && stew.TargetType == TargetType.Self,
            "Three construct potions have the approved rarity, combat-only usage and self targeting");
        Check(newPotions.All(p => p.Image != null && p.Outline != null && p.LargeImage != null),
            "All construct potion icon, outline and large art load");
        var nativePotionIds = nativeCharacters.SelectMany(ch => ch.PotionPool.AllPotionIds)
            .Concat(ModelDb.PotionPool<SharedPotionPool>().AllPotionIds).ToHashSet();
        Check(newPotions.All(p => !nativePotionIds.Contains(p.Id)) &&
            newPotions.All(p => !PotionFactory.GetPotionOptions(native).Any(x => x.Id == p.Id)),
            "Native potion pools and factories never offer construct exclusives");

        async Task UsePotion(PotionModel potion)
        {
            var result = await PotionCmd.TryToProcure(potion, player);
            Check(result.success, "Potion procured: " + potion.Title.GetFormattedText());
            await potion.OnUseWrapper(Context, player.Creature);
        }

        await UsePotion(ancient);
        Check(player.Creature.GetPowerAmount<ArtifactPower>() == 1, "Ancient Potion grants one Artifact only to its user");
        await PowerCmd.Remove<ArtifactPower>(player.Creature);
        await UsePotion(murky);
        Check(player.Creature.GetPowerAmount<VigorPower>() == 10 && player.Creature.GetPowerAmount<StretchPower>() == 10,
            "Murky Oil grants ten Vigor and ten Stretch");
        await PowerCmd.Remove<VigorPower>(player.Creature);
        await PowerCmd.Remove<StretchPower>(player.Creature);

        CardModel[] waves = [state.CreateCard<SmallWave>(player), state.CreateCard<TonyWave>(player),
            state.CreateCard<IronWave>(player), state.CreateCard<ScorchingIronWave>(player), state.CreateCard<IronWaveForm>(player)];
        foreach (var wave in waves) await CardPileCmd.AddGeneratedCardToCombat(wave, PileType.Hand, player);
        var nonWave = state.CreateCard<SmallShockwave>(player);
        await CardPileCmd.AddGeneratedCardToCombat(nonWave, PileType.Hand, player);
        int[] replay = waves.Select(c => c.BaseReplayCount).ToArray();
        int nonReplay = nonWave.BaseReplayCount;
        await UsePotion(stew);
        Check(waves.Select((c, i) => c.BaseReplayCount == replay[i] + 1).All(v => v) && nonWave.BaseReplayCount == nonReplay,
            "Fighters Stew gives Replay 1 to every 斩波 card, including Iron Wave Form, and not to 小震荡");
        await CardCmd.Discard(Context, hand.Cards.ToArray());

        var souvenir = await RelicCmd.Obtain<ConstructClockworkSouvenir>(player);
        Check(souvenir.Rarity == RelicRarity.Shop && souvenir.Icon != null && souvenir.BigIcon != null && souvenir.IconOutline != null &&
            player.Character.RelicPool.AllRelics.Any(r => r is ConstructClockworkSouvenir) &&
            nativeCharacters.All(ch => !ch.RelicPool.AllRelics.Any(r => r is ConstructClockworkSouvenir)),
            "Clockwork Souvenir is a construct-only shop relic with all three icon variants");
        int artifacts = player.Creature.GetPowerAmount<ArtifactPower>();
        await souvenir.BeforeCombatStart();
        Check(player.Creature.GetPowerAmount<ArtifactPower>() == artifacts + 1,
            "Clockwork Souvenir grants one Artifact at combat start");
        await PowerCmd.Remove<ArtifactPower>(player.Creature);
        await RelicCmd.Remove(souvenir);
    }

    private static async Task InfestedAutomatonChecks()
    {
        var unlocks = new UnlockState([], [], 0);
        var construct = Player.CreateForNewRun<ConstructCharacter>(unlocks, 8801);
        var constructRun = RunState.CreateForTest([construct], seed: "INFESTEDCONSTRUCT");
        construct.Gold = 67;
        var room = (InfestedAutomaton)ModelDb.Event<InfestedAutomaton>().ToMutable();
        await room.BeginEvent(construct, null, false);
        Check(room.CurrentOptions.Count == 2 &&
            room.CurrentOptions[0].TextKey.Contains("REPAIR") && room.CurrentOptions[1].TextKey.Contains("SEARCH") &&
            !room.CurrentOptions[0].IsLocked && !room.CurrentOptions[1].IsLocked &&
            room.CurrentOptions.All(o => o.HoverTips.Any() && !string.IsNullOrWhiteSpace(o.Title.GetFormattedText())),
            "Construct Infested Automaton offers unlocked REPAIR and SEARCH with previews");
        Check(ModelDb.Event<InfestedAutomaton>().IsAllowed(constructRun),
            "All-construct party at 67 gold without the souvenir allows the event");
        construct.Gold = 66;
        Check(!ModelDb.Event<InfestedAutomaton>().IsAllowed(constructRun),
            "All-construct party below 67 gold hides the event");
        var poor = (InfestedAutomaton)ModelDb.Event<InfestedAutomaton>().ToMutable();
        await poor.BeginEvent(construct, null, false);
        Check(poor.CurrentOptions[0].IsLocked && !poor.CurrentOptions[1].IsLocked,
            "REPAIR is locked below 67 gold while SEARCH stays usable");
        construct.Gold = 100;
        var souvenir = await RelicCmd.Obtain<ConstructClockworkSouvenir>(construct);
        Check(!ModelDb.Event<InfestedAutomaton>().IsAllowed(constructRun),
            "Owning the souvenir hides the event from an all-construct party");
        await RelicCmd.Remove(souvenir);
        Check(ModelDb.Event<InfestedAutomaton>().IsAllowed(constructRun),
            "Removing the souvenir restores the all-construct event");

        var search = (InfestedAutomaton)ModelDb.Event<InfestedAutomaton>().ToMutable();
        construct.Gold = 67;
        await search.BeginEvent(construct, null, false);
        int deckBefore = construct.Deck.Cards.Count;
        await search.CurrentOptions[1].Chosen();
        Check(search.IsFinished && construct.Deck.Cards.Count == deckBefore + 1 && construct.Deck.Cards.Any(c => c is ConstructPanacea),
            "SEARCH adds one Panacea to the deck and finishes the event");

        var repair = (InfestedAutomaton)ModelDb.Event<InfestedAutomaton>().ToMutable();
        await repair.BeginEvent(construct, null, false);
        int goldBefore = construct.Gold;
        await repair.CurrentOptions[0].Chosen();
        Check(repair.IsFinished && construct.Gold == goldBefore - 67 &&
            construct.Relics.Any(r => r is ConstructClockworkSouvenir),
            "REPAIR spends exactly 67 gold and grants the souvenir");

        var native = Player.CreateForNewRun<Ironclad>(unlocks, 8802);
        var nativeRun = RunState.CreateForTest([native], seed: "INFESTEDNATIVE");
        Check(ModelDb.Event<InfestedAutomaton>().IsAllowed(nativeRun),
            "A native party keeps the native Infested Automaton event");
        var nativeRoom = (InfestedAutomaton)ModelDb.Event<InfestedAutomaton>().ToMutable();
        await nativeRoom.BeginEvent(native, null, false);
        IReadOnlyList<MegaCrit.Sts2.Core.Events.EventOption> nativeOptions = nativeRoom.CurrentOptions;
        Check(nativeOptions.Count == 2 && nativeOptions.All(o => o.TextKey.StartsWith("INFESTED_AUTOMATON")),
            "Native owner receives the original STUDY and TOUCH_CORE options");
        ConstructInfestedAutomaton.Apply(nativeRoom, ref nativeOptions);
        Check(nativeOptions.Count == 2 && nativeOptions.All(o => o.TextKey.StartsWith("INFESTED_AUTOMATON")),
            "Construct event revision is a no-op for a native owner");
    }

    private static async Task AdditionsChecks()
    {
        await InfestedAutomatonChecks();
        await PandoraChecks();
        var run = await ReliabilityRun("SCREWANDFERVOR");
        var player = run.Players.Single();
        var keywordPreview = run.CreateCard<BattleFervor>(player);
        Check(keywordPreview.Keywords.Contains(CardKeyword.Exhaust), "Base Fervor has native Exhaust");
        keywordPreview.UpgradeInternal(); keywordPreview.FinalizeUpgradeInternal();
        Check(!keywordPreview.Keywords.Contains(CardKeyword.Exhaust), "Upgrading Fervor removes Exhaust");
        keywordPreview.DowngradeInternal(); keywordPreview.FinalizeUpgradeInternal();
        Check(keywordPreview.Keywords.Contains(CardKeyword.Exhaust), "Downgrading Fervor restores Exhaust");
        Check(ModelDb.Card<NeverBackDown>().Rarity == CardRarity.Rare && ModelDb.Card<BattleFervor>().Rarity == CardRarity.Rare &&
            ModelDb.Card<BattleFervor>().Type == CardType.Skill && ModelDb.Card<BattleFervor>().Pool is ColorlessCardPool,
            "Never Back Down is gold; Battle Fervor is a colorless gold skill");
        Check(player.Character.RelicPool.AllRelics.Any(r => r is OpenEndWrench) && ModelDb.Relic<OpenEndWrench>().Rarity == RelicRarity.Common &&
            !ModelDb.Character<Ironclad>().RelicPool.AllRelics.Any(r => r is OpenEndWrench),
            "Open-end Wrench is a common in this character's pool only");
        Check(ModelDb.Card<BattleFervor>().Portrait != null && ModelDb.Relic<OpenEndWrench>().Icon != null &&
            ModelDb.Relic<OpenEndWrench>().BigIcon != null && ModelDb.Relic<OpenEndWrench>().IconOutline != null,
            "Adopted Fervor art and all Wrench icon variants load");
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "New additions room ready");
        var enemies = player.Creature.CombatState!.HittableEnemies.ToArray();
        Check(enemies.Length >= 2, "Direct fixture contains multiple enemies");
        foreach (var e in enemies) await CreatureCmd.SetMaxAndCurrentHp(e, 1000);
        var hand = player.PlayerCombatState!.Hand;
        var deckIds = player.Deck.Cards.Select(c => c.Id).ToArray();
        var state = (MegaCrit.Sts2.Core.Combat.CombatState)player.Creature.CombatState!;
        using var transformSounds = new TransformSoundProbe();
        var silentBurns = new List<CardPileAddResult>();
        for (int i = 0; i < 2; i++)
            silentBurns.Add(await CardPileCmd.AddGeneratedCardToCombat(state.CreateCard<Burn>(player), PileType.Discard, player));
        await Frames(15);
        Check(player.PlayerCombatState.DiscardPile.Cards.Count == 2 && VisibleDiscardCount() == 0,
            "Legacy silent generation reproduces two real Burns while the discard label remains zero");
        foreach (var added in silentBurns) CardCmd.PreviewCardPileAdd(added);
        await CheckDiscardCounter(player, "same two Burns after native generation previews");
        foreach (bool upgraded in new[] { false, true })
        {
            await CardCmd.Discard(Context, hand.Cards.ToArray());
            var target = state.CreateCard<SmallWave>(player);
            await CardPileCmd.AddGeneratedCardToCombat(target, PileType.Hand, player);
            var other = state.CreateCard<SmallShockwave>(player);
            await CardPileCmd.AddGeneratedCardToCombat(other, PileType.Hand, player);
            int before = hand.Cards.Count;
            var selector = new SpecificHandSelector(target);
            CardModel spell;
            int soundBefore = transformSounds.Plays;
            using (RunManager.Instance.CardSelectorStack.UseSelector(player, selector)) spell = await Play<BattleFervor>(player, upgraded: upgraded);
            Check(transformSounds.Plays == soundBefore + 1,
                "Actual hand transformation dispatches the native transform sound exactly once: upgraded=" + upgraded);
            Check(transformSounds.SoundAtReveal, "Transform audio waits for actual visual replacement after the anticipation frames: upgraded=" + upgraded);
            var fist = hand.Cards.Single(c => c is Fisticuffs);
            Check(selector.Offered.Length == 3 && selector.Offered.Count(c => c is Burn) == 1 && selector.Offered.Contains(target) &&
                !hand.Cards.Contains(target) && hand.Cards.Contains(other) && hand.Cards.Count == before + 1 && hand.Cards.Count(c => c is Burn) == 1,
                "Fervor adds one selectable Burn before replacing exactly one original hand card: upgraded=" + upgraded);
            Check(!fist.IsUpgraded && fist.Enchantment is Momentum { Amount: 5 } && spell.EnergyCost.GetWithModifiers(CostModifiers.None) == 1 &&
                spell.Keywords.Contains(CardKeyword.Exhaust) == !upgraded && spell.Pile?.Type == (upgraded ? PileType.Discard : PileType.Exhaust),
                "Both Fervor versions make ordinary Momentum-5 Fisticuffs; only the base spell actually exhausts: " + upgraded);
            Check(player.Deck.Cards.Select(c => c.Id).SequenceEqual(deckIds), "Combat transformation leaves the permanent deck intact");
            await PlayerCmd.SetEnergy(20, player);
            var beforeMomentum = fist.Enchantment!.EnchantDamageAdditive(0, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move);
            Check(fist.TryManualPlay(enemies[0]), "Momentum Fisticuffs can actually be played");
            await Until(() => fist.Pile?.Type == PileType.Discard, "Generated Fisticuffs finishes playing");
            await Frames(15);
            Check(fist.Enchantment is Momentum && fist.Enchantment.EnchantDamageAdditive(0, MegaCrit.Sts2.Core.ValueProps.ValueProp.Move) == beforeMomentum + 5,
                "Playing transformed Fisticuffs actually increases native Momentum damage by five");
        }
        await CardCmd.Discard(Context, hand.Cards.ToArray());
        var burnSelector = new SpecificHandSelector(null);
        int burnSoundBefore = transformSounds.Plays;
        using (RunManager.Instance.CardSelectorStack.UseSelector(player, burnSelector)) await Play<BattleFervor>(player);
        Check(transformSounds.Plays == burnSoundBefore + 1, "Transforming a newly generated Burn also plays the native sound once");
        Check(transformSounds.SoundAtReveal, "Generated Burn transformation sound aligns with the actual card reveal");
        Check(hand.Cards.Count == 1 && hand.Cards.All(c => c is Fisticuffs) &&
            hand.Cards.OfType<Fisticuffs>().Single().Enchantment is Momentum { Amount: 5 },
            "Fervor with no other hand cards automatically transforms its sole new Burn into Momentum 5 Fisticuffs");

        await CardCmd.Discard(Context, hand.Cards.ToArray());
        var overflowTarget = state.CreateCard<SmallWave>(player);
        await CardPileCmd.AddGeneratedCardToCombat(overflowTarget, PileType.Hand, player);
        for (int i = 0; i < 9; i++) await CardPileCmd.AddGeneratedCardToCombat(state.CreateCard<SmallWave>(player), PileType.Hand, player);
        var cardsBeforeOverflow = player.PlayerCombatState.AllCards.ToHashSet();
        int overflowSoundBefore = transformSounds.Plays;
        // Auto-play from draw pile keeps all ten slots occupied when the effect starts.
        var overflowSpell = state.CreateCard<BattleFervor>(player);
        await CardPileCmd.AddGeneratedCardToCombat(overflowSpell, PileType.Draw, player);
        using (RunManager.Instance.CardSelectorStack.UseSelector(player, new SpecificHandSelector(overflowTarget)))
            await CardCmd.AutoPlay(Context, overflowSpell, null);
        Check(overflowSpell.Pile?.Type == PileType.Exhaust, "Base Fervor auto-play exhausts with all ten hand slots occupied");
        Check(transformSounds.Plays == overflowSoundBefore + 1, "A full-hand transformation plays one native sound without duplication");
        Check(transformSounds.SoundAtReveal, "Full-hand transformation sound aligns with the actual card reveal");
        var overflowBurns = player.PlayerCombatState.AllCards.Except(cardsBeforeOverflow).OfType<Burn>().ToArray();
        Check(hand.Cards.Count == 10 && overflowBurns.Length == 1 && overflowBurns.All(c => c.Pile?.Type == PileType.Discard) &&
            overflowBurns.Count(c => c.Pile?.Type == PileType.Discard) == 1 && !hand.Cards.Contains(overflowTarget),
            "Fervor at the hand limit generates exactly one Burn in discard and still completes transformation");
        await CheckDiscardCounter(player, "Fervor Burn overflow");

        await CardCmd.Discard(Context, hand.Cards.ToArray());
        await CardPileCmd.AddGeneratedCardToCombat(state.CreateCard<SmallWave>(player), PileType.Hand, player);
        int cancelledSoundBefore = transformSounds.Plays;
        using (RunManager.Instance.CardSelectorStack.UseSelector(player, new SpecificHandSelector(null, cancel: true))) await Play<BattleFervor>(player);
        Check(transformSounds.Plays == cancelledSoundBefore && hand.Cards.Count == 2 && hand.Cards.Count(c => c is Burn) == 1 &&
            hand.Cards.All(c => c is Burn or SmallWave),
            "No selection means no transformation and no transform sound");

        await CardCmd.Discard(Context, hand.Cards.ToArray());
        var normalNever = await Play<NeverBackDown>(player);
        var beforeSingleTurn = player.PlayerCombatState.AllCards.ToHashSet();
        await NextTurn(player, enemies[0]);
        var singleGenerated = player.PlayerCombatState.AllCards.Except(beforeSingleTurn).ToArray();
        Check(singleGenerated.OfType<Fisticuffs>().Count() == 1 && singleGenerated.OfType<Fisticuffs>().All(c => !c.IsUpgraded) &&
            singleGenerated.OfType<Fisticuffs>().All(c => c.Pile?.Type == PileType.Hand) && !singleGenerated.OfType<Burn>().Any(),
            "A normal Never Back Down generates exactly one unupgraded Fisticuffs at actual turn start and no Burn");
        await Capture("qa-never-fisticuffs");
        await CardCmd.Discard(Context, hand.Cards.ToArray());
        var upgradedNever = await Play<NeverBackDown>(player, upgraded: true);
        Check(normalNever.EnergyCost.GetWithModifiers(CostModifiers.None) == 2 && upgradedNever.EnergyCost.GetWithModifiers(CostModifiers.None) == 1 &&
            player.Creature.GetPower<NeverBackDownPower>()?.Amount == 2 && hand.Cards.Count == 0,
            "Never Back Down and its upgrade cost two/one energy, stack twice, and do not generate cards immediately");
        for (int turn = 0; turn < 2; turn++)
        {
            var beforeTurn = player.PlayerCombatState.AllCards.ToHashSet();
            await NextTurn(player, enemies[0]);
            var generated = player.PlayerCombatState.AllCards.Except(beforeTurn).ToArray();
            Check(generated.OfType<Fisticuffs>().Count() == 2 && generated.OfType<Fisticuffs>().Count(c => c.IsUpgraded) == 0 &&
                generated.OfType<Fisticuffs>().All(c => c.Pile?.Type == PileType.Hand) && !generated.OfType<Burn>().Any(),
                "Stacked Never Back Down copies generate two normal Fisticuffs and no Burn: " + turn);
        }

        await CardCmd.Discard(Context, hand.Cards.ToArray());
        for (int i = 0; i < 10; i++) await CardPileCmd.AddGeneratedCardToCombat(state.CreateCard<SmallWave>(player), PileType.Hand, player);
        var beforeFullHand = player.PlayerCombatState.AllCards.ToHashSet();
        await player.Creature.GetPower<NeverBackDownPower>()!.AfterPlayerTurnStart(Context, player);
        var fullHandGenerated = player.PlayerCombatState.AllCards.Except(beforeFullHand).ToArray();
        Check(hand.Cards.Count == 10 && fullHandGenerated.OfType<Fisticuffs>().Count() == 2 && fullHandGenerated.OfType<Fisticuffs>().Count(c => c.IsUpgraded) == 0 &&
            fullHandGenerated.All(c => c.Pile?.Type == PileType.Discard) && !fullHandGenerated.OfType<Burn>().Any(),
            "A full hand retains native Fisticuffs overflow in discard and generates no Burn");
        await PowerCmd.Remove<NeverBackDownPower>(player.Creature);
        await CardCmd.Discard(Context, hand.Cards.ToArray());

        await Play<NeverBackDown>(player, upgraded: true);
        var beforeUpgradedTurn = player.PlayerCombatState.AllCards.ToHashSet();
        await NextTurn(player, enemies[0]);
        var upgradedGenerated = player.PlayerCombatState.AllCards.Except(beforeUpgradedTurn).ToArray();
        Check(upgradedGenerated.OfType<Fisticuffs>().Count() == 1 && upgradedGenerated.OfType<Fisticuffs>().All(c => !c.IsUpgraded) &&
            !upgradedGenerated.OfType<Burn>().Any(),
            "An upgraded Never Back Down applied alone creates one normal Fisticuffs and no Burn");
        await PowerCmd.Remove<NeverBackDownPower>(player.Creature);
        await CardCmd.Discard(Context, hand.Cards.ToArray());

        await ClockworkChecks(player, enemies, state);

        var screw = await RelicCmd.Obtain<OpenEndWrench>(player);
        await CreatureCmd.SetCurrentHp(player.Creature, 50);
        int gold = player.Gold;
        await CreatureCmd.SetCurrentHp(enemies[0], 1);
        await Play<SmallWave>(player, enemies[0]);
        Check(player.Creature.CurrentHp == 51 && player.Gold == gold + 5, "First actual enemy defeat heals one and grants five gold");
        await CreatureCmd.SetCurrentHp(player.Creature, player.Creature.MaxHp);
        foreach (var e in enemies.Where(e => e.IsAlive)) await CreatureCmd.SetCurrentHp(e, 1);
        int remaining = enemies.Count(e => e.IsAlive);
        gold = player.Gold;
        await Play<MegaCrit.Sts2.Core.Models.Cards.Thunderclap>(player);
        await Until(() => !MegaCrit.Sts2.Core.Combat.CombatManager.Instance.IsInProgress, "Debug-prepared final enemy defeat");
        Check(player.Gold == gold + 5 * remaining && player.Creature.CurrentHp == player.Creature.MaxHp,
            "Final/AoE defeats each pay once; healing is capped at max HP");
        gold = player.Gold;
        await screw.AfterDeath(Context, player.Creature, null, false, 0);
        Check(player.Gold == gold, "Player death does not trigger Wrench");
        await ShowcaseChecks(player);
        await NGame.Instance!.ReturnToMainMenu();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.DeleteCurrentRun();
    }
}
