using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Entities.CardRewardAlternatives;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Vfx;
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

    // Observe the actual preview-to-pile flight, rather than only checking pile counts.
    private sealed class BurnAnimationProbe : IDisposable
    {
        private readonly Harmony _patch = new("PunchConstruct.QA.BurnAnimation");
        internal sealed class Flight(NCardFlyVfx animation, bool visiblePreview)
        {
            internal readonly NCardFlyVfx Animation = animation;
            internal readonly bool VisiblePreview = visiblePreview;
            internal bool EnteredTree;
        }
        private static readonly Dictionary<CardModel, List<Flight>> Flights = [];
        internal BurnAnimationProbe()
        {
            Flights.Clear();
            _patch.Patch(AccessTools.Method(typeof(NCardFlyVfx), nameof(NCardFlyVfx.Create),
                [typeof(NCard), typeof(PileType), typeof(bool), typeof(string)]),
                postfix: new HarmonyMethod(typeof(BurnAnimationProbe), nameof(Observe)));
        }
        private static void Observe(NCard card, PileType pileType, bool isAddingToPile, NCardFlyVfx? __result)
        {
            if (card.Model is not Burn || pileType != PileType.Discard || !isAddingToPile || __result == null) return;
            var flight = new Flight(__result, card.IsVisibleInTree());
            if (!Flights.TryGetValue(card.Model, out var list)) Flights[card.Model] = list = [];
            list.Add(flight);
            __result.TreeEntered += () => flight.EnteredTree = true;
        }
        internal static bool Finished(CardModel card) => Flights.TryGetValue(card, out var flights) &&
            flights.Count == 1 && flights[0].VisiblePreview && flights[0].EnteredTree &&
            flights[0].Animation.SwooshAwayCompletion?.Task.IsCompletedSuccessfully == true;
        public void Dispose() => _patch.UnpatchAll(_patch.Id);
    }

    private static async Task CheckBurnAnimations(IEnumerable<CardModel> cards, string scenario)
    {
        var burns = cards.OfType<Burn>().ToArray();
        await Until(() => burns.All(BurnAnimationProbe.Finished), "Burn preview and flight completion: " + scenario);
        Check(burns.Length > 0 && burns.All(c => c.Pile?.Type == PileType.Discard),
            "Visible Burn previews each complete one native flight into discard: " + scenario);
        await CheckDiscardCounter(burns[0].Owner, scenario);
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

    private static async Task AdditionsChecks()
    {
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
            using (CardSelectCmd.UseSelector(selector)) spell = await Play<BattleFervor>(player, upgraded: upgraded);
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
        using (CardSelectCmd.UseSelector(burnSelector)) await Play<BattleFervor>(player);
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
        using (CardSelectCmd.UseSelector(new SpecificHandSelector(overflowTarget)))
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
        using (CardSelectCmd.UseSelector(new SpecificHandSelector(null, cancel: true))) await Play<BattleFervor>(player);
        Check(transformSounds.Plays == cancelledSoundBefore && hand.Cards.Count == 2 && hand.Cards.Count(c => c is Burn) == 1 &&
            hand.Cards.All(c => c is Burn or SmallWave),
            "No selection means no transformation and no transform sound");

        await CardCmd.Discard(Context, hand.Cards.ToArray());
        using var burnAnimations = new BurnAnimationProbe();
        var normalNever = await Play<NeverBackDown>(player);
        var beforeSingleTurn = player.PlayerCombatState.AllCards.ToHashSet();
        await NextTurn(player, enemies[0]);
        var singleGenerated = player.PlayerCombatState.AllCards.Except(beforeSingleTurn).ToArray();
        Check(singleGenerated.OfType<Fisticuffs>().Count() == 1 && singleGenerated.OfType<Fisticuffs>().All(c => !c.IsUpgraded) && singleGenerated.OfType<Burn>().Count() == 1,
            "A normal Never Back Down generates one unupgraded Fisticuffs and one Burn at actual turn start");
        await Capture("qa-never-burn-preview");
        await CheckBurnAnimations(singleGenerated, "single power");
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
            Check(generated.OfType<Fisticuffs>().Count() == 2 && generated.OfType<Fisticuffs>().Count(c => c.IsUpgraded) == 0 && generated.OfType<Fisticuffs>().All(c => c.Pile?.Type == PileType.Hand) &&
                generated.OfType<Burn>().Count() == 2 && generated.OfType<Burn>().All(c => c.Pile?.Type == PileType.Discard),
                "Mixed Never Back Down copies generate two normal Fisticuffs, plus two discarded Burns: " + turn);
            await CheckBurnAnimations(generated, "stacked powers, turn " + turn);
        }

        await CardCmd.Discard(Context, hand.Cards.ToArray());
        for (int i = 0; i < 10; i++) await CardPileCmd.AddGeneratedCardToCombat(state.CreateCard<SmallWave>(player), PileType.Hand, player);
        var beforeFullHand = player.PlayerCombatState.AllCards.ToHashSet();
        await player.Creature.GetPower<NeverBackDownPower>()!.AfterPlayerTurnStart(Context, player);
        var fullHandGenerated = player.PlayerCombatState.AllCards.Except(beforeFullHand).ToArray();
        Check(hand.Cards.Count == 10 && fullHandGenerated.OfType<Fisticuffs>().Count() == 2 && fullHandGenerated.OfType<Fisticuffs>().Count(c => c.IsUpgraded) == 0 &&
            fullHandGenerated.All(c => c.Pile?.Type == PileType.Discard),
            "A full hand retains native Fisticuffs overflow and both generated Burns in discard");
        await CheckBurnAnimations(fullHandGenerated, "full hand");
        await PowerCmd.Remove<NeverBackDownPower>(player.Creature);
        await CardCmd.Discard(Context, hand.Cards.ToArray());

        await Play<NeverBackDown>(player, upgraded: true);
        var beforeUpgradedTurn = player.PlayerCombatState.AllCards.ToHashSet();
        await NextTurn(player, enemies[0]);
        var upgradedGenerated = player.PlayerCombatState.AllCards.Except(beforeUpgradedTurn).ToArray();
        Check(upgradedGenerated.OfType<Fisticuffs>().Count() == 1 && upgradedGenerated.OfType<Fisticuffs>().All(c => !c.IsUpgraded) &&
            upgradedGenerated.OfType<Burn>().Count() == 1,
            "An upgraded Never Back Down applied alone creates one normal Fisticuffs and one Burn");
        await CheckBurnAnimations(upgradedGenerated, "upgraded power alone");
        await PowerCmd.Remove<NeverBackDownPower>(player.Creature);
        await CardCmd.Discard(Context, hand.Cards.ToArray());

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
        await screw.AfterDeath(Context, player.Creature, false, 0);
        Check(player.Gold == gold, "Player death does not trigger Wrench");
        await NGame.Instance!.ReturnToMainMenu();
        MegaCrit.Sts2.Core.Saves.SaveManager.Instance.DeleteCurrentRun();
    }
}
