using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

// 发条机关: a construct-only power. The Burn is generated at the end of each
// player turn; every Status card drawn by its owner triggers one fixed package.
public sealed class ClockworkMechanismPower : ConstructSystemPower
{
    public override List<(string, string)> Localization => Loc.Power("发条机关",
        "回合结束时，将一张[gold]灼烧[/gold]加入你的弃牌堆。每抽到一张状态牌，获得1点[gold]活力[/gold]和1点[gold]格挡[/gold]，抽1张牌。",
        "回合结束时，将一张[gold]灼烧[/gold]加入你的弃牌堆。每抽到一张状态牌，获得1点[gold]活力[/gold]和1点[gold]格挡[/gold]，抽1张牌。");
    public override async Task BeforeSideTurnEnd(PlayerChoiceContext context, CombatSide side, IEnumerable<Creature> participants)
    {
        if (!participants.Contains(Owner) || Owner.Player is not { } player) return;
        for (int i = 0; i < Amount; i++)
        {
            var burn = ((CombatState)Owner.CombatState!).CreateCard<Burn>(player);
            var added = await CardPileCmd.AddGeneratedCardToCombat(burn, PileType.Discard, player);
            CardCmd.PreviewCardPileAdd(added);
        }
    }
    public override async Task AfterCardDrawn(PlayerChoiceContext context, CardModel card, bool fromHandDraw)
    {
        if (card.Type != CardType.Status || card.Owner != Owner.Player) return;
        for (int i = 0; i < Amount; i++)
        {
            await PowerCmd.Apply<VigorPower>(context, Owner, 1, Owner, null);
            await CreatureCmd.GainBlock(Owner, 1, ValueProp.Unpowered, null);
            await CardPileCmd.Draw(context, 1, Owner.Player!);
        }
    }
}

[Pool(typeof(ConstructCardPool))]
public sealed class ConstructClockworkMechanism() : CustomCardModel(2, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    public override string CustomPortraitPath => "res://PunchConstruct/art/clockwork_mechanism.ctex";
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<VigorPower>(), HoverTipFactory.FromCard<Burn>()];
    public override List<(string, string)> Localization => Loc.Card("发条机关",
        "回合结束时，将一张[gold]灼烧[/gold]加入你的弃牌堆。\n每抽到一张状态牌，获得1点[gold]活力[/gold]和1点[gold]格挡[/gold]，抽1张牌。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await PowerCmd.Apply<ClockworkMechanismPower>(context, Owner.Creature, 1, Owner.Creature, this);
    }
    protected override void OnUpgrade() => EnergyCost.UpgradeBy(-1);
}

// Mechanical Ascension's play path pre-adds (Scale - 1) per point of Strength
// before the native pipeline adds its own +1. The native DamageVar preview only
// sees the bare base, so it would show 12 + Strength instead of 12 + Scale x
// Strength. Re-run the native preview with the same scaled base the play path
// uses; Weak/Vulnerable then multiply the whole value exactly like resolution.
internal sealed class ScaledDamageVar(decimal damage, ValueProp props) : DamageVar(damage, props)
{
    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
    {
        base.UpdateCardPreview(card, previewMode, target, runGlobalHooks);
        if (!runGlobalHooks || card.Owner is not { } owner ||
            !card.DynamicVars.TryGetValue("Scale", out var scaleVar)) return;
        decimal strength = owner.Creature.GetPowerAmount<StrengthPower>();
        if (strength == 0 || scaleVar.BaseValue <= 1) return;
        var scaled = new DamageVar(Name, BaseValue + (scaleVar.BaseValue - 1) * strength, Props);
        scaled.SetOwner(card);
        scaled.UpdateCardPreview(card, previewMode, target, true);
        PreviewValue = scaled.PreviewValue;
    }
}

internal sealed class ScaledBlockVar(decimal block, ValueProp props) : BlockVar(block, props)
{
    public override void UpdateCardPreview(CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
    {
        base.UpdateCardPreview(card, previewMode, target, runGlobalHooks);
        if (!runGlobalHooks || card.Owner is not { } owner ||
            !card.DynamicVars.TryGetValue("Scale", out var scaleVar)) return;
        decimal dexterity = owner.Creature.GetPowerAmount<DexterityPower>();
        if (dexterity == 0 || scaleVar.BaseValue <= 1) return;
        var scaled = new BlockVar(Name, BaseValue + (scaleVar.BaseValue - 1) * dexterity, Props);
        scaled.SetOwner(card);
        scaled.UpdateCardPreview(card, previewMode, target, true);
        PreviewValue = scaled.PreviewValue;
    }
}

[Pool(typeof(ConstructCardPool))]
public sealed class ConstructMechanicalAscension() : CustomCardModel(3, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    public override string CustomPortraitPath => "res://PunchConstruct/art/mechanical_ascension.ctex";
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<StrengthPower>(), HoverTipFactory.FromPower<DexterityPower>()];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new ScaledDamageVar(12, ValueProp.Move), new ScaledBlockVar(12, ValueProp.Move), new DynamicVar("Scale", 5)];
    public override List<(string, string)> Localization => Loc.Card("机械飞升",
        "造成{Damage:diff()}点伤害。\n获得{Block:diff()}点[gold]格挡[/gold]。\n每有1点[gold]力量[/gold]，伤害额外+{Scale:diff()}；每有1点[gold]敏捷[/gold]，格挡额外+{Scale:diff()}。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        // Native Strength/Dexterity still add their own +1 per point, so pre-add
        // the remaining (Scale - 1) per point to reach the designed +Scale.
        // ScaledDamageVar/ScaledBlockVar mirror this formula for the card text;
        // keep the two paths in sync if the scaling changes.
        decimal scale = DynamicVars["Scale"].BaseValue;
        decimal damage = DynamicVars.Damage.BaseValue + (scale - 1) * Owner.Creature.GetPowerAmount<StrengthPower>();
        decimal block = DynamicVars.Block.BaseValue + (scale - 1) * Owner.Creature.GetPowerAmount<DexterityPower>();
        await DamageCmd.Attack(damage).FromCard(this, play).Targeting(play.Target!).Execute(context);
        await CreatureCmd.GainBlock(Owner.Creature, block, ValueProp.Move, play);
    }
    protected override void OnUpgrade()
    {
        DynamicVars["Scale"].UpgradeValueBy(3);
        AddKeyword(CardKeyword.Retain);
    }
}

[Pool(typeof(ConstructCardPool))]
public sealed class ConstructBurnOut() : CustomCardModel(0, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    public override string CustomPortraitPath => "res://PunchConstruct/art/burn_out.ctex";
    protected override IEnumerable<IHoverTip> ExtraHoverTips =>
        [HoverTipFactory.FromPower<StrengthPower>(), HoverTipFactory.FromPower<DexterityPower>(), HoverTipFactory.FromPower<DisintegrationPower>()];
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new DynamicVar("Strength", 1), new DynamicVar("Dexterity", 1), new DynamicVar("Disintegration", 3)];
    public override List<(string, string)> Localization => Loc.Card("燃尽",
        "获得{Strength:diff()}点[gold]力量[/gold]和{Dexterity:diff()}点[gold]敏捷[/gold]。\n获得{Disintegration:diff()}层[gold]瓦解[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await PowerCmd.Apply<StrengthPower>(context, Owner.Creature, DynamicVars["Strength"].BaseValue, Owner.Creature, this);
        await PowerCmd.Apply<DexterityPower>(context, Owner.Creature, DynamicVars["Dexterity"].BaseValue, Owner.Creature, this);
        await PowerCmd.Apply<DisintegrationPower>(context, Owner.Creature, DynamicVars["Disintegration"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars["Disintegration"].UpgradeValueBy(-2);
}

public sealed class ConstructDomineeringPower : CustomPowerModel
{
    private List<PendingDomineering> _pending = [];
    private record PendingDomineering(CardModel Card, CardPlay Play, decimal Damage, decimal Block, decimal Energy, decimal Cards);
    public override PowerType Type => PowerType.Buff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override string CustomPackedIconPath => ConstructPowerIcons.Packed(GetType());
    public override string CustomBigIconPath => ConstructPowerIcons.Big(GetType());
    public override string CustomBigBetaIconPath => CustomBigIconPath;
    public override List<(string, string)> Localization => [("title", Loc.Pick("霸无敌")),
        ("description", Loc.Pick("下回合开始时，依次结算{Amount}次，每段获得能量、抽牌，对所有敌人造成伤害并获得格挡：\n{Waves}"))];
    public override LocString Description
    {
        get
        {
            var text = base.Description;
            // List every segment's own energy/cards/damage/block: a mixed stack
            // (upgraded + base) must not show only the first segment's values.
            text.Add("Waves", string.Join(Loc.Pick("；"),
                _pending.Select(w => $"{w.Energy}{Loc.Pick("能量")}{Loc.Pick("、")}{Loc.Pick("抽")}{w.Cards}{Loc.Count(w.Cards, "张", " card", " cards")}{Loc.Pick("、")}{w.Damage}{Loc.Pick("伤害")}{Loc.Pick("、")}{w.Block}{Loc.Pick("格挡")}")));
            return text;
        }
    }
    public void Enqueue(CardModel card, CardPlay play, decimal damage, decimal block, decimal energy, decimal cards)
    {
        _pending.Add(new(card, play, damage, block, energy, cards));
        InvokeDisplayAmountChanged();
    }
    protected override void DeepCloneFields() { base.DeepCloneFields(); _pending = [.._pending]; }
    public override async Task AfterPlayerTurnStart(PlayerChoiceContext context, Player player)
    {
        if (player.Creature != Owner) return;
        var owner = Owner;
        var combat = owner.CombatState!;
        var waves = _pending.ToArray();
        // Pay before clearing so a mid-loop failure keeps the remaining segments.
        foreach (var wave in waves)
        {
            if (!combat.HittableEnemies.Any()) break;
            await PlayerCmd.GainEnergy(wave.Energy, player);
            await CardPileCmd.Draw(context, (int)wave.Cards, player);
            await DamageCmd.Attack(wave.Damage).FromCard(wave.Card, wave.Play).TargetingAllOpponents(combat).Execute(context);
            await CreatureCmd.GainBlock(owner, wave.Block, ValueProp.Move, wave.Play);
        }
        _pending.Clear();
        await PowerCmd.Remove(this);
    }
}

[Pool(typeof(ConstructCardPool))]
public sealed class ConstructDomineering() : CustomCardModel(1, CardType.Skill, CardRarity.Ancient, TargetType.Self)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain];
    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override string CustomPortraitPath => "res://PunchConstruct/art/domineering.ctex";
    protected override IEnumerable<DynamicVar> CanonicalVars =>
        [new EnergyVar(1), new CardsVar(1), new DamageVar(15, ValueProp.Move), new BlockVar(15, ValueProp.Move)];
    public override List<(string, string)> Localization => Loc.Card("霸无敌",
        "下回合开始时，获得{Energy:energyIcons()}，抽{Cards:diff()}张牌，对所有敌人造成{Damage:diff()}点伤害，获得{Block:diff()}点[gold]格挡[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var power = await PowerCmd.Apply<ConstructDomineeringPower>(context, Owner.Creature, 1, Owner.Creature, this);
        power?.Enqueue(this, play, DynamicVars.Damage.BaseValue, DynamicVars.Block.BaseValue,
            DynamicVars["Energy"].BaseValue, DynamicVars["Cards"].BaseValue);
    }
    protected override void OnUpgrade()
    {
        EnergyCost.UpgradeBy(-1);
        DynamicVars["Energy"].UpgradeValueBy(1);
        DynamicVars["Cards"].UpgradeValueBy(1);
    }
}

[Pool(typeof(ConstructCardPool))]
public sealed class ConstructCoreSurge() : CustomCardModel(1, CardType.Attack, CardRarity.Rare, TargetType.AnyEnemy)
{
    public override CardPoolModel Pool => ModelDb.CardPool<DefectCardPool>();
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    public override string CustomPortraitPath => "res://PunchConstruct/art/core_surge.ctex";
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<ArtifactPower>()];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(11, ValueProp.Move)];
    public override List<(string, string)> Localization => Loc.Card("核心电涌",
        "造成{Damage:diff()}点伤害。\n获得1层[gold]人工制品[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play).Targeting(play.Target!).Execute(context);
        await PowerCmd.Apply<ArtifactPower>(context, Owner.Creature, 1, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(4);
}

[Pool(typeof(ConstructCardPool))]
public sealed class ConstructPanacea() : CustomCardModel(0, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    public override bool CanBeGeneratedInCombat => false;
    public override bool CanBeGeneratedByModifiers => false;
    public override string CustomPortraitPath => "res://PunchConstruct/art/panacea.ctex";
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<ArtifactPower>()];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Artifact", 1)];
    public override List<(string, string)> Localization => Loc.Card("万能药",
        "获得{Artifact:diff()}层[gold]人工制品[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await PowerCmd.Apply<ArtifactPower>(context, Owner.Creature, DynamicVars["Artifact"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars["Artifact"].UpgradeValueBy(1);
}
