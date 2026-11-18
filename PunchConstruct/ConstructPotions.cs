using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Potions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using Godot;

namespace PunchConstruct;

// Character-exclusive potions. They live only in ConstructPotionPool, so
// native characters never see them in drops, shops, or event rewards.
[Pool(typeof(ConstructPotionPool))]
public sealed class ConstructAncientPotion : CustomPotionModel
{
    public override PotionRarity Rarity => PotionRarity.Common;
    public override PotionUsage Usage => PotionUsage.CombatOnly;
    public override TargetType TargetType => TargetType.Self;
    public override string? CustomPackedImagePath => "res://PunchConstruct/art/potion_ancient.ctex";
    public override string? CustomPackedOutlinePath => "res://PunchConstruct/art/potion_ancient_outline.ctex";
    public override string? CustomLargeImagePath => "res://PunchConstruct/art/potion_ancient_large.ctex";
    public override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<ArtifactPower>()];
    public override List<(string, string)> Localization => Loc.Potion("先古药水",
        "获得1层[gold]人工制品[/gold]。");
    protected override async Task OnUse(PlayerChoiceContext context, Creature? target)
    {
        PotionModel.AssertValidForTargetedPotion(target);
        await PowerCmd.Apply<ArtifactPower>(context, Owner.Creature, 1, Owner.Creature, null);
    }
}

[Pool(typeof(ConstructPotionPool))]
public sealed class ConstructMurkyOil : CustomPotionModel
{
    public override PotionRarity Rarity => PotionRarity.Uncommon;
    public override PotionUsage Usage => PotionUsage.CombatOnly;
    public override TargetType TargetType => TargetType.Self;
    public override string? CustomPackedImagePath => "res://PunchConstruct/art/potion_murky_oil.ctex";
    public override string? CustomPackedOutlinePath => "res://PunchConstruct/art/potion_murky_oil_outline.ctex";
    public override string? CustomLargeImagePath => "res://PunchConstruct/art/potion_murky_oil_large.ctex";
    public override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<VigorPower>(), HoverTipFactory.FromPower<StretchPower>()];
    public override List<(string, string)> Localization => Loc.Potion("浑浊机油",
        "获得10点[gold]活力[/gold]和10点[gold]拉伸[/gold]。");
    protected override async Task OnUse(PlayerChoiceContext context, Creature? target)
    {
        PotionModel.AssertValidForTargetedPotion(target);
        await PowerCmd.Apply<VigorPower>(context, Owner.Creature, 10, Owner.Creature, null);
        await PowerCmd.Apply<StretchPower>(context, Owner.Creature, 10, Owner.Creature, null);
    }
}

[Pool(typeof(ConstructPotionPool))]
public sealed class ConstructFightersStew : CustomPotionModel
{
    public override PotionRarity Rarity => PotionRarity.Rare;
    public override PotionUsage Usage => PotionUsage.CombatOnly;
    public override TargetType TargetType => TargetType.Self;
    public override string? CustomPackedImagePath => "res://PunchConstruct/art/potion_fighters_stew.ctex";
    public override string? CustomPackedOutlinePath => "res://PunchConstruct/art/potion_fighters_stew_outline.ctex";
    public override string? CustomLargeImagePath => "res://PunchConstruct/art/potion_fighters_stew_large.ctex";
    public override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.Static(StaticHoverTip.ReplayStatic)];
    public override List<(string, string)> Localization => Loc.Potion("拳手炖汤",
        "在本场战斗中，所有名字带有“斩波”的卡牌获得[gold]重放[/gold]1。");
    private static bool IsWave(CardModel card) =>
        card is SmallWave or TonyWave or IronWave or ScorchingIronWave or IronWaveForm;
    protected override Task OnUse(PlayerChoiceContext context, Creature? target)
    {
        PotionModel.AssertValidForTargetedPotion(target);
        var cards = Owner.PlayerCombatState?.AllCards.Where(IsWave).ToArray() ?? [];
        foreach (var card in cards) card.BaseReplayCount++;
        if (target != null) MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance?.PlaySplashVfx(target, new Color("b8b8b8"));
        return Task.CompletedTask;
    }
}
