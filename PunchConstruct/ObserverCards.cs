using BaseLib.Abstracts;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

// Visual origin only: acquisition stays in ConstructCardPool. No Watcher assembly,
// character, global rewards, unlocks or patches are installed by these cards.
public sealed class ObserverOriginPool : CustomCardPoolModel
{
    public override string Title => "观者";
    public override bool IsColorless => false;
    public override bool IsShared => false;
    public override string EnergyColorName => "necrobinder";
    public override float H => .715f;
    public override float S => .65f;
    public override float V => 1.1f;
    public override Color DeckEntryCardColor => new("b49ad4");
    protected override CardModel[] GenerateAllCards() => [];
}

public abstract class ObserverCard(int cost, CardType type, CardRarity rarity, TargetType target)
    : CustomCardModel(cost, type, rarity, target)
{
    public override CardPoolModel Pool => ModelDb.CardPool<ObserverOriginPool>();
    public override string CustomPortraitPath => "res://PunchConstruct/observer/" + GetType().Name + ".ctex";
    public override Material CreateCustomFrameMaterial
    {
        get
        {
            var material = new ShaderMaterial { Shader = ResourceLoader.Load<Shader>("res://shaders/hsv.gdshader") };
            material.SetShaderParameter("h", .715f);
            material.SetShaderParameter("s", .65f);
            material.SetShaderParameter("v", 1.1f);
            return material;
        }
    }
}

[Pool(typeof(ConstructCardPool))]
public sealed class ObserverJustLucky() : ObserverCard(0, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(3, ValueProp.Move), new BlockVar(2, ValueProp.Move), new DynamicVar("Scry", 1)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [ObserverRules.ScryTip];
    public override List<(string, string)> Localization => Loc.Card("幸运一击", "[gold]预见[/gold]{Scry:diff()}。\n获得{Block:diff()}点[gold]格挡[/gold]。\n造成{Damage:diff()}点伤害。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await ObserverRules.Scry(context, Owner, DynamicVars["Scry"].IntValue);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play);
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play).Targeting(play.Target!).Execute(context);
    }
    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(1); DynamicVars.Block.UpgradeValueBy(1); DynamicVars["Scry"].UpgradeValueBy(1); }
}

[Pool(typeof(ConstructCardPool))]
public sealed class ObserverSashWhip() : ObserverCard(1, CardType.Attack, CardRarity.Common, TargetType.AnyEnemy)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(8, ValueProp.Move), new DynamicVar("Weak", 1)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<WeakPower>()];
    public override List<(string, string)> Localization => Loc.Card("腰带抽打", "造成{Damage:diff()}点伤害。\n如果这场战斗中打出的上一张牌是攻击牌，给予{Weak:diff()}层[gold]虚弱[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        // Skip this play entry rather than all occurrences of the same card.
        var previous = CombatManager.Instance.History.CardPlaysStarted.Where(e => e.CardPlay.Player == Owner).Reverse().Skip(1).FirstOrDefault();
        bool applyWeak = previous?.CardPlay.Card.Type == CardType.Attack;
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play).Targeting(play.Target!).Execute(context);
        if (applyWeak && play.Target!.IsAlive) await PowerCmd.Apply<WeakPower>(context, play.Target, DynamicVars["Weak"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2); DynamicVars["Weak"].UpgradeValueBy(1); }
}

[Pool(typeof(ConstructCardPool))]
public sealed class ObserverProstrate() : ObserverCard(0, CardType.Skill, CardRarity.Common, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(4, ValueProp.Move), new DynamicVar("Mantra", 2)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<ObserverMantra>(), HoverTipFactory.FromPower<ObserverDivinity>()];
    public override List<(string, string)> Localization => Loc.Card("五体投地", "获得{Mantra:diff()}点[gold]真言[/gold]。\n获得{Block:diff()}点[gold]格挡[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await PowerCmd.Apply<ObserverMantra>(context, Owner.Creature, DynamicVars["Mantra"].BaseValue, Owner.Creature, this);
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play);
    }
    protected override void OnUpgrade() => DynamicVars["Mantra"].UpgradeValueBy(1);
}

[Pool(typeof(ConstructCardPool))]
public sealed class ObserverTalkToTheHand() : ObserverCard(1, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Exhaust];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(5, ValueProp.Move), new DynamicVar("Amount", 2)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<ObserverBlockReturn>()];
    public override List<(string, string)> Localization => Loc.Card("以手拒之", "造成{Damage:diff()}点伤害。\n每当目标受到攻击时，攻击者获得{Amount:diff()}点[gold]格挡[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play).Targeting(play.Target!).Execute(context);
        if (play.Target!.IsAlive) await PowerCmd.Apply<ObserverBlockReturn>(context, play.Target, DynamicVars["Amount"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() { DynamicVars.Damage.UpgradeValueBy(2); DynamicVars["Amount"].UpgradeValueBy(1); }
}

[Pool(typeof(ConstructCardPool))]
public sealed class ObserverWallop() : ObserverCard(2, CardType.Attack, CardRarity.Uncommon, TargetType.AnyEnemy)
{
    public override bool GainsBlock => true;
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DamageVar(9, ValueProp.Move)];
    public override List<(string, string)> Localization => Loc.Card("当头棒喝", "造成{Damage:diff()}点伤害。\n获得等同于未被[gold]格挡[/gold]的伤害量的[gold]格挡[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        var attack = await DamageCmd.Attack(DynamicVars.Damage.BaseValue).FromCard(this, play).Targeting(play.Target!).Execute(context);
        decimal amount = attack.Results.SelectMany(r => r).Sum(r => r.UnblockedDamage);
        if (amount > 0) await CreatureCmd.GainBlock(Owner.Creature, amount, ValueProp.Move | ValueProp.Unpowered, play);
    }
    protected override void OnUpgrade() => DynamicVars.Damage.UpgradeValueBy(3);
}

[Pool(typeof(ConstructCardPool))]
public sealed class ObserverColdObservation() : ObserverCard(2, CardType.Skill, CardRarity.Uncommon, TargetType.AllAllies)
{
    public override IEnumerable<CardKeyword> CanonicalKeywords => [CardKeyword.Retain];
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Amount", 1)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<ObserverColdObservationPower>()];
    public override List<(string, string)> Localization => Loc.Card("冷眼旁观", "所有玩家接下来的{Amount:diff()}次攻击，获得等同于造成伤害量的[gold]格挡[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        foreach (var ally in CombatState!.GetTeammatesOf(Owner.Creature).Where(c => c.IsAlive && c.Player != null))
            await PowerCmd.Apply<ObserverColdObservationPower>(context, ally, DynamicVars["Amount"].BaseValue, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars["Amount"].UpgradeValueBy(1);
}

[Pool(typeof(ConstructCardPool))]
public sealed class ObserverSwivel() : ObserverCard(2, CardType.Skill, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new BlockVar(8, ValueProp.Move)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<FreeAttackPower>()];
    public override List<(string, string)> Localization => Loc.Card("旋身", "获得{Block:diff()}点[gold]格挡[/gold]。\n你打出的下一张攻击牌耗能变为0。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await CreatureCmd.GainBlock(Owner.Creature, DynamicVars.Block, play);
        await PowerCmd.Apply<FreeAttackPower>(context, Owner.Creature, 1, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars.Block.UpgradeValueBy(3);
}

[Pool(typeof(ConstructCardPool))]
public sealed class ObserverFasting() : ObserverCard(2, CardType.Power, CardRarity.Uncommon, TargetType.Self)
{
    protected override IEnumerable<DynamicVar> CanonicalVars => [new DynamicVar("Amount", 3)];
    protected override IEnumerable<IHoverTip> ExtraHoverTips => [HoverTipFactory.FromPower<StrengthPower>(), HoverTipFactory.FromPower<DexterityPower>(), HoverTipFactory.FromPower<ObserverEnergyDown>()];
    public override List<(string, string)> Localization => Loc.Card("斋戒", "获得{Amount:diff()}点[gold]力量[/gold]。\n获得{Amount:diff()}点[gold]敏捷[/gold]。\n每回合开始时少获得1点[gold]能量[/gold]。");
    protected override async Task OnPlay(PlayerChoiceContext context, CardPlay play)
    {
        await PowerCmd.Apply<StrengthPower>(context, Owner.Creature, DynamicVars["Amount"].BaseValue, Owner.Creature, this);
        await PowerCmd.Apply<DexterityPower>(context, Owner.Creature, DynamicVars["Amount"].BaseValue, Owner.Creature, this);
        await PowerCmd.Apply<ObserverEnergyDown>(context, Owner.Creature, 1, Owner.Creature, this);
    }
    protected override void OnUpgrade() => DynamicVars["Amount"].UpgradeValueBy(1);
}

internal static class ObserverRules
{
    internal static IHoverTip ScryTip => new HoverTip(new LocString("cards", "PUNCHCONSTRUCT_OBSERVER_SCRY.title"), new LocString("cards", "PUNCHCONSTRUCT_OBSERVER_SCRY.description"));
    internal static async Task Scry(PlayerChoiceContext context, MegaCrit.Sts2.Core.Entities.Players.Player player, int amount)
    {
        var cards = player.PlayerCombatState!.DrawPile.Cards.Take(amount).ToArray();
        if (cards.Length == 0) return;
        var prompt = new LocString("cards", "PUNCHCONSTRUCT_OBSERVER_SCRY.prompt");
        var chosen = await CardSelectCmd.FromSimpleGrid(context, cards, player,
            new CardSelectorPrefs(prompt, 0, cards.Length) { RequireManualConfirmation = true, Cancelable = false });
        var discarded = chosen.ToArray();
        if (discarded.Length > 0) await CardCmd.Discard(context, discarded);
    }
}

// Upgrade/enchantment/floor metadata remains native. Rewrite only these eight
// former card IDs, and only when their original mod is absent.
internal static class ObserverSaveMigration
{
    internal static readonly Dictionary<string, Type> OldCards = new()
    {
        ["WATCHER_JUST_LUCKY"] = typeof(ObserverJustLucky), ["WATCHER_SASH_WHIP"] = typeof(ObserverSashWhip),
        ["WATCHER_PROSTRATE"] = typeof(ObserverProstrate), ["WATCHER_TALK_TO_THE_HAND"] = typeof(ObserverTalkToTheHand),
        ["WATCHER_WALLOP"] = typeof(ObserverWallop), ["WATCHER_COLD_OBSERVATION"] = typeof(ObserverColdObservation),
        ["WATCHER_SWIVEL"] = typeof(ObserverSwivel), ["WATCHER_FASTING2"] = typeof(ObserverFasting)
    };
    internal static void Apply(SerializableCard __0)
    {
        // The removed ancient variant must not prevent older runs from loading.
        // No model is registered under the deleted ID; only saved copies migrate.
        if (__0.Id?.Category == "CARD" && __0.Id.Entry == "PUNCHCONSTRUCT-GIANT_IRON_WAVE")
        {
            __0.Id = ModelDb.GetId<ScorchingIronWave>();
            return;
        }
        if (__0.Id?.Category == "CARD" && OldCards.TryGetValue(__0.Id.Entry, out var type) && ModelDb.GetByIdOrNull<CardModel>(__0.Id) == null)
            __0.Id = ModelDb.GetId(type);
    }
}
