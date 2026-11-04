using System.Reflection;
using System.Runtime.CompilerServices;
using BaseLib.Abstracts;
using BaseLib.Utils;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

internal static class CharacterScope
{
    private static readonly AccessTools.FieldRef<RelicModel, Player?> RelicOwner = AccessTools.FieldRefAccess<RelicModel, Player?>("_owner");
    internal static bool IsConstruct(RelicModel relic) => (RelicOwner(relic) ?? RelicPreviewContext.Owner(relic))?.Character is ConstructCharacter;
    internal static bool IsWave(CardModel? card) => card is SmallWave or TonyWave or IronWave or ScorchingIronWave;
}

[HarmonyPatch]
internal static class WaveDummyPatch
{
    private static IEnumerable<MethodBase> TargetMethods() => [
        AccessTools.Method(typeof(StrikeDummy), nameof(StrikeDummy.ModifyDamageAdditive)),
        AccessTools.Method(typeof(FakeStrikeDummy), nameof(FakeStrikeDummy.ModifyDamageAdditive))
    ];
    private static bool Prefix(RelicModel __instance, Creature? dealer, ValueProp props, CardModel? cardSource, ref decimal __result)
    {
        if (!CharacterScope.IsConstruct(__instance)) return true;
        __result = dealer == __instance.Owner.Creature && cardSource?.Owner == __instance.Owner && props.IsPoweredAttack() && CharacterScope.IsWave(cardSource)
            ? __instance.DynamicVars["ExtraDamage"].BaseValue : 0;
        return false;
    }
}

[HarmonyPatch(typeof(GhostSeed), "CanAffect")]
internal static class GhostSeedWavePatch
{
    private static void Postfix(CardModel card, ref bool __result)
    {
        if (card.Owner?.Character is ConstructCharacter) __result = card is SmallWave;
    }
}

[HarmonyPatch(typeof(RippleBasin), nameof(RippleBasin.BeforeSideTurnEnd))]
internal static class AttackingBasinPatch
{
    private static bool Prefix(RippleBasin __instance, IEnumerable<Creature> participants, ref Task __result)
    {
        if (!CharacterScope.IsConstruct(__instance)) return true;
        __result = Grant(__instance, participants);
        return false;
    }
    private static async Task Grant(RippleBasin relic, IEnumerable<Creature> participants)
    {
        var owner = relic.Owner;
        if (!participants.Contains(owner.Creature)) return;
        if (CombatManager.Instance.History.CardPlaysFinished.Any(e => e.HappenedThisTurn(owner.Creature.CombatState!) && e.CardPlay.Player == owner && e.CardPlay.Card.Type == CardType.Attack))
            await CreatureCmd.GainBlock(owner.Creature, relic.DynamicVars.Block, null);
    }
}

[HarmonyPatch(typeof(RippleBasin), nameof(RippleBasin.AfterCardPlayed))]
internal static class BasinStatusPatch
{
    private static void Postfix(RippleBasin __instance, CardPlay cardPlay)
    {
        if (CharacterScope.IsConstruct(__instance) && cardPlay.Player == __instance.Owner && cardPlay.Card.Type == CardType.Attack)
            __instance.Status = RelicStatus.Active;
    }
}

[HarmonyPatch(typeof(NutritiousSoup), nameof(NutritiousSoup.AfterObtained))]
internal static class SoupWavePatch
{
    internal static readonly HashSet<CardModel> Transforming = [];
    private static bool Prefix(NutritiousSoup __instance, ref Task __result)
    {
        if (!CharacterScope.IsConstruct(__instance)) return true;
        __result = Transform(__instance.Owner);
        return false;
    }
    private static async Task Transform(Player owner)
    {
        foreach (var card in owner.Deck.Cards.Where(c => c is SmallWave && c.Enchantment == null).ToArray())
        {
            var replacement = owner.RunState.CreateCard<IronWave>(owner);
            if (card.IsUpgraded) { replacement.UpgradeInternal(); replacement.FinalizeUpgradeInternal(); }
            PersistentCardKeywords.MakeEternal(replacement);
            // Soup explicitly replaces every Small Wave, including an Eternal one.
            // The command rechecks after constructing the transformation. Permit only
            // this exact source card until that command completes, also on exceptions.
            // IsRemovable and every other card's transformation rules remain native.
            Transforming.Add(card);
            try { await CardCmd.Transform(card, replacement); }
            finally { Transforming.Remove(card); }
        }
    }
}

[HarmonyPatch(typeof(NutritiousSoup), "ExtraHoverTips", MethodType.Getter)]
internal static class SoupWaveHoverTips
{
    private static void Postfix(NutritiousSoup __instance, ref IEnumerable<IHoverTip> __result)
    {
        if (CharacterScope.IsConstruct(__instance))
            __result = [HoverTipFactory.FromKeyword(CardKeyword.Eternal), HoverTipFactory.FromCard<IronWave>()];
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.IsTransformable), MethodType.Getter)]
internal static class SoupExplicitReplacement
{
    private static void Postfix(CardModel __instance, ref bool __result)
    {
        if (SoupWavePatch.Transforming.Contains(__instance) && __instance is SmallWave && __instance.Owner?.Character is ConstructCharacter)
            __result = true;
    }
}

[HarmonyPatch(typeof(LeafyPoultice), nameof(LeafyPoultice.AfterObtained))]
internal static class LeafyPoulticeWaves
{
    private static bool Prefix(LeafyPoultice __instance, ref Task __result)
    {
        if (!CharacterScope.IsConstruct(__instance)) return true;
        __result = Transform(__instance);
        return false;
    }
    private static async Task Transform(LeafyPoultice relic)
    {
        var owner = relic.Owner;
        var targets = owner.Deck.Cards.OfType<SmallWave>().Where(c => c.IsTransformable).Take(2).ToArray();
        await CreatureCmd.LoseMaxHp(new ThrowingPlayerChoiceContext(), owner.Creature, relic.DynamicVars.MaxHp.BaseValue, false);
        foreach (var card in targets)
        {
            var replacement = MegaCrit.Sts2.Core.Factories.CardFactory.CreateRandomCardForTransform(card, false, owner.PlayerRng.Transformations);
            await CardCmd.Transform(card, replacement);
        }
    }
}

internal static class HelmetMirror
{
    private sealed class State { public bool Applying; }
    private static readonly ConditionalWeakTable<RelicModel, State> States = new();
    private static bool Used(RelicModel relic) => relic switch {
        ConstructHelmet helmet => CombatRelicState.HelmetUsed[helmet],
        RuinedHelmet helmet => CombatRelicState.NativeHelmetUsed[helmet], _ => false
    };
    private static void SetUsed(RelicModel relic, bool value)
    {
        if (relic is ConstructHelmet helmet) CombatRelicState.HelmetUsed[helmet] = value;
        else if (relic is RuinedHelmet native) CombatRelicState.NativeHelmetUsed[native] = value;
    }
    internal static void Reset(RelicModel relic) { States.Remove(relic); SetUsed(relic, false); }
    internal static async Task Apply(RelicModel relic, PlayerChoiceContext context, PowerModel power, decimal change)
    {
        if (power.Owner != relic.Owner.Creature || change <= 0 || power is not (StrengthPower or DexterityPower)) return;
        var state = States.GetOrCreateValue(relic);
        if (state.Applying || Used(relic)) return;
        bool strength = power is StrengthPower;
        SetUsed(relic, true);
        state.Applying = true;
        try
        {
            // A mirrored grant never reflects back into this same relic.
            if (strength) await PowerCmd.Apply<DexterityPower>(context, relic.Owner.Creature, change, relic.Owner.Creature, null);
            else await PowerCmd.Apply<StrengthPower>(context, relic.Owner.Creature, change, relic.Owner.Creature, null);
        }
        finally { state.Applying = false; }
    }
}

[Pool(typeof(ConstructRelicPool))]
public sealed class ConstructHelmet : CustomRelicModel
{
    protected override string IconBaseName => "ruined_helmet";
    public override RelicRarity Rarity => ModelDb.Relic<RuinedHelmet>().Rarity;
    public override string PackedIconPath => ModelDb.Relic<RuinedHelmet>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/ruined_helmet.tres";
    public override List<(string, string)> Localization => new RelicLoc("损毁头盔", "每场战斗首次获得[gold]力量[/gold]或[gold]敏捷[/gold]时，获得等量的另一项属性。整场共触发一次。", "在攻守之间找回平衡。");
    public override Task BeforeCombatStart() { HelmetMirror.Reset(this); return Task.CompletedTask; }
    public override Task AfterPowerAmountChanged(PlayerChoiceContext context, PowerModel power, decimal amount, Creature? applier, CardModel? card) => HelmetMirror.Apply(this, context, power, amount);
}

[Pool(typeof(ConstructRelicPool))]
public sealed class ConstructDart : CustomRelicModel
{
    protected override string IconBaseName => "helical_dart";
    public override RelicRarity Rarity => ModelDb.Relic<HelicalDart>().Rarity;
    public override string PackedIconPath => ModelDb.Relic<HelicalDart>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/helical_dart.tres";
    public override List<(string, string)> Localization => new RelicLoc("螺线飞镖", "每打出一张攻击牌，本回合获得1点[gold]敏捷[/gold]。", "让出拳成为下一次防御。");
    public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay play)
    {
        if (play.Player == Owner && play.Card.Type == CardType.Attack)
            await PowerCmd.Apply<HelicalDartPower>(context, Owner.Creature, 1, Owner.Creature, null);
    }
}

[HarmonyPatch(typeof(RuinedHelmet), nameof(RuinedHelmet.TryModifyPowerAmountReceived))]
internal static class NativeHelmetDisableDouble
{
    private static bool Prefix(RuinedHelmet __instance, ref bool __result)
    {
        if (!CharacterScope.IsConstruct(__instance)) return true;
        __result = false; return false;
    }
}

[HarmonyPatch(typeof(AbstractModel), nameof(AbstractModel.AfterPowerAmountChanged))]
internal static class NativeHelmetMirrorHook
{
    private static void Postfix(AbstractModel __instance, PlayerChoiceContext choiceContext, PowerModel power, decimal amount, ref Task __result)
    {
        if (__instance is RuinedHelmet relic && CharacterScope.IsConstruct(relic))
            __result = HelmetMirror.Apply(relic, choiceContext, power, amount);
    }
}

[HarmonyPatch(typeof(RuinedHelmet), nameof(RuinedHelmet.AfterCombatEnd))]
internal static class NativeHelmetReset
{
    private static void Postfix(RuinedHelmet __instance) { if (CharacterScope.IsConstruct(__instance)) HelmetMirror.Reset(__instance); }
}

[HarmonyPatch(typeof(HelicalDart), nameof(HelicalDart.AfterCardPlayed))]
internal static class NativeDartAdaptation
{
    private static bool Prefix(HelicalDart __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
    {
        if (!CharacterScope.IsConstruct(__instance)) return true;
        __result = cardPlay.Player == __instance.Owner && cardPlay.Card.Type == CardType.Attack
            ? Apply(choiceContext, __instance.Owner) : Task.CompletedTask;
        return false;
    }
    private static async Task Apply(PlayerChoiceContext context, Player owner) => await PowerCmd.Apply<HelicalDartPower>(context, owner.Creature, 1, owner.Creature, null);
}

[HarmonyPatch]
internal static class AdaptedRelicDescriptions
{
    private static IEnumerable<MethodBase> TargetMethods() => [
        AccessTools.PropertyGetter(typeof(RelicModel), "Description"),
        AccessTools.PropertyGetter(typeof(RelicModel), "EventDescription")
    ];
    private static void Postfix(RelicModel __instance, ref LocString __result)
    {
        if (!CharacterScope.IsConstruct(__instance)) return;
        string? key = __instance switch {
            StrikeDummy => "STRIKE_DUMMY", FakeStrikeDummy => "FAKE_STRIKE_DUMMY", GhostSeed => "GHOST_SEED",
            RippleBasin => "RIPPLE_BASIN", NutritiousSoup => "NUTRITIOUS_SOUP", RuinedHelmet => "RUINED_HELMET", HelicalDart => "HELICAL_DART",
            LeafyPoultice => "LEAFY_POULTICE", SeaGlass => "SEA_GLASS", DingyRug => "DINGY_RUG", LargeCapsule => "LARGE_CAPSULE", PrismaticGem => "PRISMATIC_GEM", ArchaicTooth => "ARCHAIC_TOOTH", NeowsTalisman => "NEOWS_TALISMAN", PandorasBox => "PANDORAS_BOX", _ => null
        };
        if (key != null) __result = new LocString("relics", "PUNCHCONSTRUCT_ADAPTED_" + key + ".description");
    }
}
