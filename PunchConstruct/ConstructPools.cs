using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace PunchConstruct;

public sealed class ConstructCardPool : CustomCardPoolModel
{
    public override string Title => Loc.Pick("拳击构装体");
    public override bool IsColorless => false;
    public override string EnergyColorName => "defect";
    public override string CardFrameMaterialPath => "card_frame_blue";
    public override Color DeckEntryCardColor => new("9ec9c7");
    public override bool IsShared => false;

    // Pool construction runs before any Harmony guard. A removed/unregistered
    // native card must not throw here — that is the shortest path to "character
    // disappears after a game update". Collect missing names, skip them, mark the
    // pool DEGRADED, and keep loading (fail-open, consistent with F3).
    internal static bool PoolDegraded { get; private set; }
    internal static IReadOnlyList<string> MissingCards => missingCards;
    private static readonly List<string> missingCards = [];
    private static CardModel? TryCard<T>() where T : CardModel
    {
        try { return ModelDb.Card<T>(); }
        catch (Exception error)
        {
            // Diagnostics must not turn a degraded card into a failed pool:
            // this is the last boundary before BaseLib builds the character.
            try
            {
                missingCards.Add(typeof(T).Name);
                Diagnostics.Write("error", "pool.card_missing", new { card = typeof(T).Name, error = error.Message });
            }
            catch { }
            return null;
        }
    }

    // Acquisition gates live in EventCards; never force a particular event reward.
    protected override CardModel[] GenerateAllCards()
    {
        // BaseLib appends [Pool]-registered custom cards automatically. This is
        // the acquisition list; a card's Pool can retain its design origin.
        CardModel?[] cards = [
            TryCard<BodySlam>(), TryCard<IronWave>(), TryCard<Taunt>(),
            TryCard<Uppercut>(), TryCard<Rage>(), TryCard<FlameBarrier>(),
            TryCard<Mangle>(), TryCard<Juggernaut>(), TryCard<Neutralize>(), TryCard<SuckerPunch>(),
            TryCard<CloakAndDagger>(), TryCard<Dash>(), TryCard<BladeOfInk>(),
            TryCard<Abrasive>(), TryCard<Suppress>(), TryCard<Fisticuffs>(),
            TryCard<Shockwave>(), TryCard<Prowess>(), TryCard<Whistle>(),
            TryCard<FallingStar>(), TryCard<CrushUnder>(), TryCard<KnowThyPlace>(), TryCard<Patter>(),
            TryCard<GammaBlast>(), TryCard<Reflect>(), TryCard<Resonance>(), TryCard<Bulwark>(),
            TryCard<Comet>(), TryCard<DyingStar>(), TryCard<MonarchsGaze>(), TryCard<MeteorShower>(),
            TryCard<Parry>(), TryCard<Bodyguard>(),
            TryCard<SicEm>(), TryCard<NegativePulse>(), TryCard<Unleash>(), TryCard<BoneShards>(),
            TryCard<Putrefy>(), TryCard<Deathbringer>(), TryCard<Shroud>(), TryCard<Protector>(),
            TryCard<GoForTheEyes>(), TryCard<ColdSnap>(), TryCard<LightningRod>(), TryCard<Null>(),
            TryCard<Glasswork>(), TryCard<ShadowShield>(), TryCard<Hailstorm>(), TryCard<BulkUp>(),
            TryCard<IceLance>(), TryCard<Rainbow>(),
            // Native Fool designs. Their arrow slots, shooting and impale flow are
            // character-agnostic in the base game, so only the acquisition list and
            // the group gate in CardAcquisition are mod-side work. Cho Barbs,
            // Cho Ready and Wah Cho Ki are re-authored in FoolCards.cs with the
            // construct's 0.113 values and join through their [Pool] attributes.
            TryCard<WahCho>(),
            TryCard<HobblingShot>(), TryCard<BlossomShot>(), TryCard<WhistlingArrows>(),
            TryCard<Nail>(), TryCard<Enrapture>(), TryCard<CoveringFire>()
            // The eight independently implemented observer cards are appended via
            // their [Pool] attributes, alongside the other local designs.
        ];
        var present = cards.Where(c => c != null).Cast<CardModel>().ToArray();
        if (missingCards.Count > 0)
        {
            PoolDegraded = true;
            Diagnostics.Write("error", "pool.degraded", new { missing = missingCards, kept = present.Length });
        }
        return present;
    }
}

public sealed class ConstructRelicPool : CustomRelicPoolModel
{
    public override bool IsShared => false;
    protected override IEnumerable<RelicModel> GenerateAllRelics() => [];
}

public sealed class ConstructPotionPool : CustomPotionPoolModel
{
    public override bool IsShared => false;
    public override string EnergyColorName => "defect";
    // PotionFactory adds shared potions separately. Character-exclusive additions
    // stay empty until selected for this character; do not inherit another pool.
    protected override IEnumerable<PotionModel> GenerateAllPotions() => [];
}
