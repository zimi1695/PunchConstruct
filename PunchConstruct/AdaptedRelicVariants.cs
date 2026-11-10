using System.Reflection;
using BaseLib.Abstracts;
using BaseLib.Utils;
using BaseLib.Utils.Attributes;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

// Relic variants are ordinary models: the construct's copy of a shared relic
// owns its rules directly instead of patching the shared native class. Native
// relics stay untouched for every other character.
internal static class AdaptedRelicVariants
{
    // Native relics whose construct behaviour lives in a variant model. Rolls
    // never offer the native version to a construct player and any direct grant
    // is swapped before it reaches the inventory.
    private static readonly Dictionary<Type, Func<RelicModel>> Variants = new()
    {
        [typeof(StrikeDummy)] = () => ModelDb.Relic<ConstructStrikeDummy>().ToMutable(),
        [typeof(FakeStrikeDummy)] = () => ModelDb.Relic<ConstructFakeStrikeDummy>().ToMutable(),
        [typeof(GhostSeed)] = () => ModelDb.Relic<ConstructGhostSeed>().ToMutable(),
        [typeof(RippleBasin)] = () => ModelDb.Relic<ConstructRippleBasin>().ToMutable(),
        [typeof(NutritiousSoup)] = () => ModelDb.Relic<ConstructNutritiousSoup>().ToMutable(),
        [typeof(LeafyPoultice)] = () => ModelDb.Relic<ConstructLeafyPoultice>().ToMutable(),
        [typeof(SeaGlass)] = () => ModelDb.Relic<ConstructSeaGlass>().ToMutable(),
        [typeof(DingyRug)] = () => ModelDb.Relic<ConstructDingyRug>().ToMutable(),
        [typeof(LargeCapsule)] = () => ModelDb.Relic<ConstructLargeCapsule>().ToMutable(),
        [typeof(NeowsTalisman)] = () => ModelDb.Relic<ConstructNeowsTalisman>().ToMutable(),
        [typeof(PrismaticGem)] = () => ModelDb.Relic<ConstructPrismaticGem>().ToMutable(),
        [typeof(PandorasBox)] = () => ModelDb.Relic<ConstructPandorasBox>().ToMutable(),
        [typeof(PaelsClaw)] = () => ModelDb.Relic<ConstructPaelsClaw>().ToMutable(),
        [typeof(RuinedHelmet)] = () => ModelDb.Relic<ConstructHelmet>().ToMutable(),
        [typeof(HelicalDart)] = () => ModelDb.Relic<ConstructDart>().ToMutable()
    };

    internal static bool IsNativeAdapted(RelicModel relic) => Variants.ContainsKey(relic.GetType());

    // A scripted RelicCmd.Obtain<T> call needs the native type back, so only
    // model-reference acquisitions are swapped (rewards, shops, events, chest).
    internal static void Substitute(ref RelicModel relic, Player player)
    {
        if (!CharacterScope.IsConstruct(player) || !Variants.TryGetValue(relic.GetType(), out var factory)) return;
        if (CalledFromGenericObtain()) return;
        var variant = factory();
        variant.FloorAddedToDeck = relic.FloorAddedToDeck;
        Diagnostics.Write("info", "relic.variant.substitute", new
        {
            player = Diagnostics.Slot(player.NetId),
            from = relic.Id.Entry,
            to = variant.Id.Entry
        });
        relic = variant;
    }

    // Save files and multiplayer syncs may still contain the native model.
    internal static void Migrate(Player player, ref IEnumerable<RelicModel> relics)
    {
        if (!CharacterScope.IsConstruct(player)) return;
        relics = relics.Select(relic => Variants.TryGetValue(relic.GetType(), out var factory) ? Swap(relic, factory) : relic).ToArray();
    }

    private static RelicModel Swap(RelicModel relic, Func<RelicModel> factory)
    {
        var variant = factory();
        variant.FloorAddedToDeck = relic.FloorAddedToDeck;
        return variant;
    }

    private static bool CalledFromGenericObtain() => new System.Diagnostics.StackTrace()
        .GetFrames().Any(frame => frame.GetMethod() is { } method && method.DeclaringType == typeof(RelicCmd) && method.IsGenericMethod);
}

internal static class VariantText
{
    // Titles and flavor already exist in the native table for every language;
    // only descriptions are authored per variant. The title argument is always
    // overridden by Title below so no text is duplicated here.
    internal static string Native(string entry, string field) => new LocString("relics", entry + "." + field).GetFormattedText();
    internal static LocString Title(string entry) => new("relics", entry + ".title");
    internal static string Flavor(string entry) => new LocString("relics", entry + ".flavor").GetFormattedText();
}

[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-ADAPTED_STRIKE_DUMMY")]
public sealed class ConstructStrikeDummy : CustomRelicModel
{
    protected override string IconBaseName => "strike_dummy";
    public override RelicRarity Rarity => RelicRarity.Common;
    public override string PackedIconPath => ModelDb.Relic<StrikeDummy>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/strike_dummy.tres";
    public override LocString Title => VariantText.Title("STRIKE_DUMMY");
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("ExtraDamage", 3m)];
    public override List<(string, string)> Localization => Loc.Relic(VariantText.Native("STRIKE_DUMMY", "title"),
        "名字中含有“斩波”的牌额外造成3点伤害。", VariantText.Flavor("STRIKE_DUMMY"));
    public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props, Creature? dealer,
        CardModel? cardSource, CardPlay? cardPlay) =>
        props.IsPoweredAttack() && cardSource != null && CharacterScope.IsWave(cardSource) &&
        (dealer == Owner.Creature || cardSource.Owner == Owner) ? DynamicVars["ExtraDamage"].BaseValue : 0m;
}

[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-ADAPTED_FAKE_STRIKE_DUMMY")]
public sealed class ConstructFakeStrikeDummy : CustomRelicModel
{
    protected override string IconBaseName => "fake_strike_dummy";
    public override RelicRarity Rarity => RelicRarity.Event;
    public override int MerchantCost => 50;
    public override string PackedIconPath => ModelDb.Relic<FakeStrikeDummy>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/fake_strike_dummy.tres";
    public override LocString Title => VariantText.Title("FAKE_STRIKE_DUMMY");
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("ExtraDamage", 1m)];
    public override List<(string, string)> Localization => Loc.Relic(VariantText.Native("FAKE_STRIKE_DUMMY", "title"),
        "名字中含有“斩波”的牌额外造成1点伤害。", VariantText.Flavor("FAKE_STRIKE_DUMMY"));
    public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props, Creature? dealer,
        CardModel? cardSource, CardPlay? cardPlay) =>
        props.IsPoweredAttack() && cardSource != null && CharacterScope.IsWave(cardSource) &&
        (dealer == Owner.Creature || cardSource.Owner == Owner) ? DynamicVars["ExtraDamage"].BaseValue : 0m;
}

[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-ADAPTED_GHOST_SEED")]
public sealed class ConstructGhostSeed : CustomRelicModel
{
    protected override string IconBaseName => "ghost_seed";
    public override RelicRarity Rarity => RelicRarity.Shop;
    public override string PackedIconPath => ModelDb.Relic<GhostSeed>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/ghost_seed.tres";
    public override LocString Title => VariantText.Title("GHOST_SEED");
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromKeyword(CardKeyword.Ethereal)];
    public override List<(string, string)> Localization => Loc.Relic(VariantText.Native("GHOST_SEED", "title"),
        "你的小斩波获得[gold]虚无[/gold]。", VariantText.Flavor("GHOST_SEED"));
    public override Task AfterCardEnteredCombat(CardModel card)
    {
        if (Affect(card) && card.Owner == Owner) CardCmd.ApplyKeyword(card, CardKeyword.Ethereal);
        return Task.CompletedTask;
    }
    public override Task AfterRoomEntered(AbstractRoom room)
    {
        if (room is not CombatRoom || Owner.PlayerCombatState is not { } combat) return Task.CompletedTask;
        foreach (var card in combat.AllCards)
            if (Affect(card)) CardCmd.ApplyKeyword(card, CardKeyword.Ethereal);
        return Task.CompletedTask;
    }
    private static bool Affect(CardModel card) => card is SmallWave &&
        !card.GetKeywordsWithSources(KeywordSources.Local).Contains(CardKeyword.Ethereal);
}

[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-ADAPTED_RIPPLE_BASIN")]
public sealed class ConstructRippleBasin : CustomRelicModel
{
    protected override string IconBaseName => "ripple_basin";
    public override RelicRarity Rarity => RelicRarity.Uncommon;
    public override string PackedIconPath => ModelDb.Relic<RippleBasin>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/ripple_basin.tres";
    public override LocString Title => VariantText.Title("RIPPLE_BASIN");
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(4m, ValueProp.Unpowered)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.Static(StaticHoverTip.Block)];
    public override List<(string, string)> Localization => Loc.Relic(VariantText.Native("RIPPLE_BASIN", "title"),
        "如果你在回合中打出过攻击牌，回合结束时获得4点[gold]格挡[/gold]。", VariantText.Flavor("RIPPLE_BASIN"));
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext choiceContext, CombatSide side, IEnumerable<Creature> participants)
    {
        if (!participants.Contains(Owner.Creature) ||
            !CombatManager.Instance.History.CardPlaysFinished.Any(e => e.HappenedThisTurn(Owner.Creature.CombatState!) &&
                e.CardPlay.Player == Owner && e.CardPlay.Card.Type == CardType.Attack)) return;
        Flash();
        Status = RelicStatus.Normal;
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, null);
    }
    public override Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
    {
        if (cardPlay.Player == Owner && cardPlay.Card.Type == CardType.Attack) Status = RelicStatus.Active;
        return Task.CompletedTask;
    }
    public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side,
        IReadOnlyList<Creature> participants, ICombatState combatState)
    {
        if (participants.Contains(Owner.Creature)) Status = RelicStatus.Active;
        return Task.CompletedTask;
    }
    public override Task AfterCombatEnd(CombatRoom room)
    {
        Status = RelicStatus.Normal;
        return Task.CompletedTask;
    }
}

[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-ADAPTED_NUTRITIOUS_SOUP")]
public sealed class ConstructNutritiousSoup : CustomRelicModel
{
    protected override string IconBaseName => "nutritious_soup";
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override string PackedIconPath => ModelDb.Relic<NutritiousSoup>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/nutritious_soup.tres";
    public override LocString Title => VariantText.Title("NUTRITIOUS_SOUP");
    public override bool HasUponPickupEffect => true;
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.FromKeyword(CardKeyword.Eternal), HoverTipFactory.FromCard<IronWave>()];
    public override List<(string, string)> Localization => Loc.Relic(VariantText.Native("NUTRITIOUS_SOUP", "title"),
        "拾起时，将你[gold]牌组[/gold]中的所有[gold]小斩波[/gold][gold]变化[/gold]为[gold]铁斩波[/gold]，并赋予[gold]永恒[/gold]。",
        VariantText.Flavor("NUTRITIOUS_SOUP"));
    public override async Task AfterObtained()
    {
        foreach (var card in Owner.Deck.Cards.Where(c => c is SmallWave && c.Enchantment == null).ToArray())
        {
            var replacement = Owner.RunState.CreateCard<IronWave>(Owner);
            if (card.IsUpgraded) { replacement.UpgradeInternal(); replacement.FinalizeUpgradeInternal(); }
            PersistentCardKeywords.MakeEternal(replacement);
            // Soup replaces every Small Wave, including an Eternal one. The
            // transform command rechecks removability; permit only this exact
            // source card until it completes.
            SoupWavePatch.Transforming.Add(card);
            try { await CardCmd.Transform(card, replacement); }
            finally { SoupWavePatch.Transforming.Remove(card); }
        }
    }
}

[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-ADAPTED_LEAFY_POULTICE")]
public sealed class ConstructLeafyPoultice : CustomRelicModel
{
    protected override string IconBaseName => "leafy_poultice";
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override string PackedIconPath => ModelDb.Relic<LeafyPoultice>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/leafy_poultice.tres";
    public override LocString Title => VariantText.Title("LEAFY_POULTICE");
    public override bool HasUponPickupEffect => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new MaxHpVar(12m)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.Static(StaticHoverTip.Transform)];
    public override List<(string, string)> Localization => Loc.Relic(VariantText.Native("LEAFY_POULTICE", "title"),
        "拾起时，失去[red]{MaxHp}[/red]点最大生命，并[gold]变化[/gold]你的[blue]2[/blue]张[gold]小斩波[/gold]。",
        VariantText.Flavor("LEAFY_POULTICE"));
    public override async Task AfterObtained()
    {
        var targets = Owner.Deck.Cards.OfType<SmallWave>().Where(c => c.IsTransformable).Take(2).ToArray();
        await CreatureCmd.LoseMaxHp(new ThrowingPlayerChoiceContext(), Owner.Creature, DynamicVars.MaxHp.BaseValue, false);
        foreach (var card in targets)
        {
            var replacement = CardFactory.CreateRandomCardForTransform(card, false, Owner.PlayerRng.Transformations);
            await CardCmd.Transform(card, replacement);
        }
    }
}

[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-ADAPTED_SEA_GLASS")]
public sealed class ConstructSeaGlass : CustomRelicModel
{
    protected override string IconBaseName => "sea_glass";
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override string PackedIconPath => ModelDb.Relic<SeaGlass>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/sea_glass.tres";
    public override LocString Title => VariantText.Title("SEA_GLASS");
    public override bool HasUponPickupEffect => true;
    public override List<(string, string)> Localization => Loc.Relic(VariantText.Native("SEA_GLASS", "title"),
        "查看储君、亡灵契约师、故障机器人各[blue]5[/blue]张牌，共[blue]15[/blue]张。从中选择任意数量的卡牌加入你的[gold]牌组[/gold]。",
        VariantText.Flavor("SEA_GLASS"));
    public override async Task AfterObtained()
    {
        var rewards = new List<CardCreationResult>();
        bool previous = CardAcquisition.SeaGlassSelection;
        CardAcquisition.SeaGlassSelection = true;
        try
        {
            foreach (var pool in new CardPoolModel[] { ModelDb.CardPool<RegentCardPool>(), ModelDb.CardPool<NecrobinderCardPool>(), ModelDb.CardPool<DefectCardPool>() })
            {
                var options = CardCreationOptions.ForNonCombatWithUniformOdds([pool], CardAcquisition.IsSelectedImport)
                    .WithFlags((CardCreationFlags)17);
                rewards.AddRange(CardFactory.CreateForReward(Owner, 5, options));
            }
        }
        finally { CardAcquisition.SeaGlassSelection = previous; }
        var prefs = new CardSelectorPrefs(new LocString("relics", "SEA_GLASS.selectionScreenPrompt"), 0, rewards.Count);
        var selected = await CardSelectCmd.FromSimpleGridForRewards(new BlockingPlayerChoiceContext(), rewards, Owner, prefs);
        foreach (var card in selected)
        {
            var result = await CardPileCmd.Add(card, PileType.Deck);
            CardCmd.PreviewCardPileAdd(result);
        }
    }
}

[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-ADAPTED_DINGY_RUG")]
public sealed class ConstructDingyRug : CustomRelicModel
{
    // Trial balance value: affects only selection within the native rarity roll.
    internal const int ColorlessMultiplier = 5;
    protected override string IconBaseName => "dingy_rug";
    public override RelicRarity Rarity => RelicRarity.Shop;
    public override string PackedIconPath => ModelDb.Relic<DingyRug>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/dingy_rug.tres";
    public override LocString Title => VariantText.Title("DINGY_RUG");
    public override List<(string, string)> Localization => Loc.Relic(VariantText.Native("DINGY_RUG", "title"),
        "卡牌奖励中出现[gold]无色牌[/gold]的概率大幅提高。", VariantText.Flavor("DINGY_RUG"));
    public override CardCreationOptions ModifyCardRewardCreationOptions(Player player, CardCreationOptions options)
    {
        // Construct reward pools already contain the selected colorless cards;
        // adding the global colorless pool would leak other characters' cards.
        return options;
    }
}

[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-ADAPTED_LARGE_CAPSULE")]
public sealed class ConstructLargeCapsule : CustomRelicModel
{
    protected override string IconBaseName => "large_capsule";
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override string PackedIconPath => ModelDb.Relic<LargeCapsule>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/large_capsule.tres";
    public override LocString Title => VariantText.Title("LARGE_CAPSULE");
    public override bool HasUponPickupEffect => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new IntVar("Relics", 2m)];
    public override List<(string, string)> Localization => Loc.Relic(VariantText.Native("LARGE_CAPSULE", "title"),
        "获得[blue]{Relics}[/blue]件随机[gold]遗物[/gold]。额外将[blue]2[/blue]张[gold]小斩波[/gold]加入你的[gold]牌组[/gold]。",
        VariantText.Flavor("LARGE_CAPSULE"));
    public override async Task AfterObtained()
    {
        for (int i = 0; i < DynamicVars["Relics"].IntValue; i++)
        {
            var relic = RelicFactory.PullNextRelicFromFront(Owner).ToMutable();
            await RelicCmd.Obtain(relic, Owner);
        }
        var results = new List<CardPileAddResult>(2);
        results.Add(await CardPileCmd.Add(Owner.RunState.CreateCard<SmallWave>(Owner), PileType.Deck));
        results.Add(await CardPileCmd.Add(Owner.RunState.CreateCard<SmallWave>(Owner), PileType.Deck));
        CardCmd.PreviewCardPileAdd(results, 2f);
    }
}

[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-ADAPTED_NEOWS_TALISMAN")]
public sealed class ConstructNeowsTalisman : CustomRelicModel
{
    protected override string IconBaseName => "neows_talisman";
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override string PackedIconPath => ModelDb.Relic<NeowsTalisman>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/neows_talisman.tres";
    public override LocString Title => VariantText.Title("NEOWS_TALISMAN");
    public override bool HasUponPickupEffect => true;
    public override List<(string, string)> Localization => Loc.Relic(VariantText.Native("NEOWS_TALISMAN", "title"),
        "[gold]强化[/gold]两张[gold]小斩波[/gold]。", VariantText.Flavor("NEOWS_TALISMAN"));
    public override Task AfterObtained()
    {
        // Same rule as the native talisman (last basic Strike and Defend), but
        // Small Wave is both, so the last two upgradable copies are upgraded
        // without opening a card-selection screen.
        foreach (var card in PileType.Deck.GetPile(Owner).Cards
                     .Where(c => c is SmallWave && c.IsUpgradable).TakeLast(2).ToArray())
            CardCmd.Upgrade(card);
        return Task.CompletedTask;
    }
}

[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-ADAPTED_PRISMATIC_GEM")]
public sealed class ConstructPrismaticGem : CustomRelicModel
{
    protected override string IconBaseName => "prismatic_gem";
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override string PackedIconPath => ModelDb.Relic<PrismaticGem>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/prismatic_gem.tres";
    public override LocString Title => VariantText.Title("PRISMATIC_GEM");
    protected override IEnumerable<DynamicVar> CanonicalVars => [new EnergyVar(1)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.ForEnergy(this)];
    public override List<(string, string)> Localization => Loc.Relic(VariantText.Native("PRISMATIC_GEM", "title"),
        "在每个回合开始时获得{Energy:energyIcons()}。战斗卡牌奖励不再出现[gold]无色牌[/gold]。",
        VariantText.Flavor("PRISMATIC_GEM"));
    public override decimal ModifyMaxEnergy(Player player, decimal amount) =>
        player == Owner ? amount + DynamicVars.Energy.IntValue : amount;
    public override CardCreationOptions ModifyCardRewardCreationOptions(Player player, CardCreationOptions options)
    {
        if (player != Owner || options.Flags.HasFlag(CardCreationFlags.NoModifyHooks) ||
            options.Flags.HasFlag(CardCreationFlags.NoCardPoolModifications) ||
            !options.Flags.HasFlag(CardCreationFlags.IsCardReward) ||
            !(options.Source == CardCreationSource.Encounter || options.Flags.HasFlag(CardCreationFlags.IsFromCombat)))
            return options;
        var previous = options.CardPoolFilter;
        return (options with { }).WithFilter(card => !card.Pool.IsColorless && (previous?.Invoke(card) ?? true));
    }
}

[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-ADAPTED_PANDORAS_BOX")]
public sealed class ConstructPandorasBox : CustomRelicModel
{
    protected override string IconBaseName => "pandoras_box";
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override string PackedIconPath => ModelDb.Relic<PandorasBox>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/pandoras_box.tres";
    public override LocString Title => VariantText.Title("PANDORAS_BOX");
    public override bool HasUponPickupEffect => true;
    public override List<(string, string)> Localization => Loc.Relic(VariantText.Native("PANDORAS_BOX", "title"),
        "拾起时，将所有可变化的[gold]小斩波[/gold]、[gold]打击[/gold]和[gold]防御[/gold][gold]变化[/gold]为其他牌。",
        VariantText.Flavor("PANDORAS_BOX"));
    public override async Task AfterObtained()
    {
        var source = PileType.Deck.GetPile(Owner).Cards
            .Where(c => (c.IsBasicStrikeOrDefend || c is SmallWave) && c.IsRemovable).ToList();
        var transformations = source
            .Select(c => new CardTransformation(c, CardFactory.CreateRandomCardForTransform(c, false, Owner.RunState.Rng.Niche)))
            .ToList();
        var results = (await CardCmd.Transform(transformations, null, CardPreviewStyle.None)).ToList();
        if (results.Count > 0 && LocalContext.IsMe(Owner))
            NSimpleCardsViewScreen.ShowScreen(results, new LocString("relics", "PANDORAS_BOX.infoText"));
    }
}

[Pool(typeof(ConstructRelicPool))]
[CustomID("PUNCHCONSTRUCT-ADAPTED_PAELS_CLAW")]
public sealed class ConstructPaelsClaw : CustomRelicModel
{
    protected override string IconBaseName => "paels_claw";
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override string PackedIconPath => ModelDb.Relic<PaelsClaw>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/paels_claw.tres";
    public override LocString Title => VariantText.Title("PAELS_CLAW");
    public override bool HasUponPickupEffect => true;
    protected override IEnumerable<IHoverTip> ExtraHoverTips => HoverTipFactory.FromEnchantment<Goopy>(1);
    public override List<(string, string)> Localization => Loc.Relic(VariantText.Native("PAELS_CLAW", "title"),
        "拾起时，为所有未附魔的[gold]小斩波[/gold]附魔[purple]黏糊[/purple]。",
        VariantText.Flavor("PAELS_CLAW"));
    public override Task AfterObtained()
    {
        foreach (var card in PileType.Deck.GetPile(Owner).Cards.ToList())
        {
            // Native Goopy only accepts Defend-tagged cards. Small Wave is the
            // construct's defence, so the tag check is replaced by the card
            // identity check; the enchantment itself stays native (Exhaust and
            // +1 permanent block per play).
            if (card is not SmallWave || card.Enchantment != null) continue;
            var goopy = ModelDb.Enchantment<Goopy>().ToMutable();
            card.EnchantInternal(goopy, 1m);
            goopy.ModifyCard();
            card.FinalizeUpgradeInternal();
            if (card.Pile?.Type == PileType.Deck)
                card.Owner.RunState.CurrentMapPointHistoryEntry?.GetEntry(card.Owner.NetId)
                    .CardsEnchanted.Add(new CardEnchantmentHistoryEntry(card, goopy.Id));
            if (NCardEnchantVfx.Create(card) is { } vfx)
                NRun.Instance?.GlobalUi.CardPreviewContainer.AddChildSafely(vfx);
        }
        return Task.CompletedTask;
    }
}

