using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;

namespace PunchConstruct;

[Pool(typeof(ConstructCardPool))]
public sealed class BattleFervor() : CustomCardModel(1, CardType.Skill, CardRarity.Rare, TargetType.Self)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ColorlessCardPool>();
    public override string CustomPortraitPath => "res://PunchConstruct/art/battle_fervor.ctex";
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromCard<Burn>(), HoverTipFactory.FromCard<Fisticuffs>(), .. HoverTipFactory.FromEnchantment<Momentum>(5)];
    public override List<(string, string)> Localization => new CardLoc("愈战愈勇",
        "将一张[gold]灼烧[/gold]加入你的手牌。选择一张手牌[gold]变化[/gold]为附魔5层[gold]动量[/gold]的[gold]拳斗[/gold]。",
        [("selectionScreenPrompt", "选择一张手牌变化为拳斗。")]);
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var state = (MegaCrit.Sts2.Core.Combat.CombatState)CombatState!;
        var added = await CardPileCmd.AddGeneratedCardToCombat(state.CreateCard<Burn>(Owner), PileType.Hand, Owner);
        // Overflow has no hand-entry animation to notify the discard counter.
        if (added.cardAdded?.Pile?.Type == PileType.Discard) CardCmd.PreviewCardPileAdd(added);
        var selected = await CardSelectCmd.FromHand(context, Owner, new CardSelectorPrefs(SelectionScreenPrompt, 1),
            card => card.IsTransformable, this);
        foreach (var original in selected)
        {
            var replacement = state.CreateCard<Fisticuffs>(Owner);
            CardCmd.Enchant<Momentum>(replacement, 5);
            await HandTransformAudio.Transform(original, replacement);
        }
    }
    protected override void OnUpgrade() => RemoveKeyword(CardKeyword.Exhaust);
}
