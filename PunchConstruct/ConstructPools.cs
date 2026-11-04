using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace PunchConstruct;

public sealed class ConstructCardPool : CustomCardPoolModel
{
    public override string Title => "拳击构装体";
    public override bool IsColorless => false;
    public override string EnergyColorName => "defect";
    public override string CardFrameMaterialPath => "card_frame_blue";
    public override Color DeckEntryCardColor => new("9ec9c7");
    public override bool IsShared => false;

    // Acquisition gates live in EventCards; never force a particular event reward.
    protected override CardModel[] GenerateAllCards() => [
        // BaseLib appends [Pool]-registered custom cards automatically. This is
        // the acquisition list; a card's Pool can retain its design origin.
        ModelDb.Card<BodySlam>(), ModelDb.Card<IronWave>(), ModelDb.Card<Taunt>(),
        ModelDb.Card<Uppercut>(), ModelDb.Card<Rage>(), ModelDb.Card<FlameBarrier>(),
        ModelDb.Card<Mangle>(), ModelDb.Card<Juggernaut>(), ModelDb.Card<Neutralize>(), ModelDb.Card<SuckerPunch>(),
        ModelDb.Card<CloakAndDagger>(), ModelDb.Card<Dash>(), ModelDb.Card<BladeOfInk>(),
        ModelDb.Card<Abrasive>(), ModelDb.Card<Suppress>(), ModelDb.Card<Fisticuffs>(),
        ModelDb.Card<Shockwave>(), ModelDb.Card<Prowess>(), ModelDb.Card<Whistle>(),
        ModelDb.Card<FallingStar>(), ModelDb.Card<CrushUnder>(), ModelDb.Card<KnowThyPlace>(), ModelDb.Card<Patter>(),
        ModelDb.Card<GammaBlast>(), ModelDb.Card<Reflect>(), ModelDb.Card<Resonance>(), ModelDb.Card<Bulwark>(),
        ModelDb.Card<Comet>(), ModelDb.Card<DyingStar>(), ModelDb.Card<MonarchsGaze>(), ModelDb.Card<MeteorShower>(),
        ModelDb.Card<Parry>(), ModelDb.Card<Bodyguard>(),
        ModelDb.Card<SicEm>(), ModelDb.Card<NegativePulse>(), ModelDb.Card<Unleash>(), ModelDb.Card<BoneShards>(),
        ModelDb.Card<Putrefy>(), ModelDb.Card<Deathbringer>(), ModelDb.Card<Shroud>(), ModelDb.Card<Protector>(),
        ModelDb.Card<GoForTheEyes>(), ModelDb.Card<ColdSnap>(), ModelDb.Card<LightningRod>(), ModelDb.Card<Null>(),
        ModelDb.Card<Glasswork>(), ModelDb.Card<ShadowShield>(), ModelDb.Card<Hailstorm>(), ModelDb.Card<BulkUp>(),
        ModelDb.Card<IceLance>(), ModelDb.Card<Rainbow>()
        // The eight independently implemented observer cards are appended via
        // their [Pool] attributes, alongside the other local designs.
    ];
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
