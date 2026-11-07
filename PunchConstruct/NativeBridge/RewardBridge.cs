using System.Reflection;
using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct.NativeBridge;

// All card/reward acquisition hooks. Each hook forwards into the reward-domain
// adapter; the native result survives any adapter failure.
[HarmonyPatch]
internal static class BridgeCombatCardSources
{
    private static IEnumerable<MethodBase> TargetMethods() => ConstructCombatCardSources.Targets();

    private static void Prefix(Player player, ref IEnumerable<CardModel> __1, ref int __2, MethodBase __originalMethod, out Player? __state)
    {
        var cards = __1;
        var count = __2;
        Player? state = null;
        AdaptationRegistry.Run("reward", "CombatCardSources", () =>
            ConstructCombatCardSources.Apply(player, ref cards, ref count, __originalMethod, out state));
        __1 = cards;
        __2 = count;
        __state = state;
    }

    private static void Postfix(Player player, ref IEnumerable<CardModel> __result)
    {
        var result = __result;
        AdaptationRegistry.Run("reward", "CombatCardSources.Result", () =>
            ConstructCombatCardSources.Resolve(player, ref result));
        __result = result;
    }

    private static void Finalizer(Player? __state) => ConstructCombatCardSources.Restore(__state);
}

[HarmonyPatch(typeof(CardCreationOptions), nameof(CardCreationOptions.GetPossibleCards))]
internal static class BridgeGroupCardGates
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(CardCreationOptions __instance, Player player, ref IEnumerable<CardModel> __result)
    {
        var result = __result;
        AdaptationRegistry.Run("reward", "GroupCardGates", () => GroupCardGates.Apply(__instance, player, ref result));
        __result = result;
    }
}

[HarmonyPatch(typeof(ColorfulPhilosophers), "OfferRewards")]
[PatchPolicy(true)]
internal static class BridgePhilosopherRandomRewards
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(ColorfulPhilosophers __instance, CardPoolModel pool, ref Task __result)
    {
        var result = __result;
        var runNative = AdaptationRegistry.Guard("reward", "PhilosopherRandomRewards",
            () => PhilosopherRandomRewards.Apply(__instance, pool, ref result));
        __result = result;
        return runNative;
    }
}

[HarmonyPatch]
internal static class BridgeCardFactoryScope
{
    private static IEnumerable<MethodBase> TargetMethods() => CardFactoryScope.Targets();

    private static void Prefix(Player player, MethodBase __originalMethod, object[] __args, out (Player?, bool) __state)
    {
        (Player?, bool) state = (null, false);
        AdaptationRegistry.Run("reward", "CardFactoryScope", () => CardFactoryScope.Apply(player, __originalMethod, __args, out state));
        __state = state;
    }

    private static void Finalizer((Player?, bool) __state) => CardFactoryScope.Restore(__state);

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = instructions.ToList();
        return AdaptationRegistry.Run("reward", "CardFactoryScope.Transpiler", original,
            () => CardFactoryScope.Transpile(original));
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Rarity), MethodType.Getter)]
internal static class BridgeConstructCardRarity
{
    private static void Postfix(CardModel __instance, ref CardRarity __result)
    {
        var rarity = __result;
        AdaptationRegistry.Run("reward", "ConstructCardRarity", () => ConstructCardRarity.Apply(__instance, ref rarity));
        __result = rarity;
    }
}

[HarmonyPatch]
internal static class BridgeMerchantCardGates
{
    private static IEnumerable<MethodBase> TargetMethods() => MerchantCardGates.Targets();

    private static void Prefix(Player player, ref IEnumerable<CardModel> __1, MethodBase __originalMethod)
    {
        var cards = __1;
        AdaptationRegistry.Run("reward", "MerchantCardGates", () =>
            MerchantCardGates.Apply(player, ref cards, __originalMethod));
        __1 = cards;
    }
}

[HarmonyPatch(typeof(MerchantInventory), "PopulateColorlessCardEntries")]
[PatchPolicy(true)]
internal static class BridgeConstructColorlessShop
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(MerchantInventory __instance) =>
        AdaptationRegistry.Guard("reward", "ConstructColorlessShop", () => ConstructColorlessShop.Apply(__instance));
}

[HarmonyPatch(typeof(ColorfulPhilosophers), "GenerateInitialOptions")]
internal static class BridgePhilosopherColors
{
    [HarmonyPriority(Priority.Last)]
    private static void Postfix(ColorfulPhilosophers __instance, ref IReadOnlyList<EventOption> __result)
    {
        var options = __result;
        AdaptationRegistry.Run("reward", "PhilosopherColors", () => PhilosopherColors.Apply(__instance, ref options));
        __result = options;
    }
}

[HarmonyPatch(typeof(DustyTome), nameof(DustyTome.SetupForPlayer))]
internal static class BridgeConstructDustyTome
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(DustyTome __instance, Player player) =>
        AdaptationRegistry.Guard("reward", "ConstructDustyTome", () => ConstructDustyTome.Apply(__instance, player));
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Owner), MethodType.Setter)]
internal static class BridgeRegentOwner
{
    private static void Postfix(CardModel __instance) =>
        AdaptationRegistry.Run("reward", "RegentOwner", () => RegentOwnerAdaptation.Apply(__instance));
}

[HarmonyPatch(typeof(CardModel), "AfterDowngraded")]
internal static class BridgeRegentDowngrade
{
    private static void Postfix(CardModel __instance) =>
        AdaptationRegistry.Run("reward", "RegentDowngrade", () => RegentDowngradeAdaptation.Apply(__instance));
}

[HarmonyPatch]
internal static class BridgeRegentStarCost
{
    private static IEnumerable<MethodBase> TargetMethods() => RegentStarCostAdaptation.Targets();

    private static void Postfix(CardModel __instance, ref int __result)
    {
        var cost = __result;
        AdaptationRegistry.Run("reward", "RegentStarCost", () => RegentStarCostAdaptation.Apply(__instance, ref cost));
        __result = cost;
    }
}

[HarmonyPatch]
internal static class BridgeRegentStarCostVisuals
{
    private static IEnumerable<MethodBase> TargetMethods() => RegentStarCostVisuals.Targets();

    private static void Postfix(NCard __instance, TextureRect? ____starIcon) =>
        AdaptationRegistry.Run("reward", "RegentStarCostVisuals",
            () => RegentStarCostVisuals.Apply(__instance, ____starIcon));
}

[HarmonyPatch(typeof(CardFactory), nameof(CardFactory.GetDefaultTransformationOptions))]
internal static class BridgeConstructTransformPool
{
    [HarmonyPriority(Priority.First)]
    private static bool Prefix(CardModel original, bool isInCombat, ref IEnumerable<CardModel> __result)
    {
        var result = __result;
        var runNative = AdaptationRegistry.Guard("reward", "ConstructTransformPool",
            () => ConstructTransformPool.Apply(original, isInCombat, ref result));
        __result = result;
        return runNative;
    }
}

[HarmonyPatch(typeof(CardFactory), "GetFilteredTransformationOptions")]
internal static class BridgeTransformDeckGates
{
    private static void Postfix(CardModel original, ref CardModel[] __result)
    {
        var result = __result;
        AdaptationRegistry.Run("reward", "TransformDeckGates", () => TransformDeckGates.Apply(original, ref result));
        __result = result;
    }
}

[HarmonyPatch]
internal static class BridgeTransformAffinity
{
    private static IEnumerable<MethodBase> TargetMethods() => TransformAffinity.Targets();

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = instructions.ToList();
        return AdaptationRegistry.Run("reward", "TransformAffinity", original,
            () => TransformAffinity.Transpile(original));
    }
}

[HarmonyPatch(typeof(MerchantInventory), "PopulateCharacterCardEntries")]
internal static class BridgeAncientShopCards
{
    private static void Postfix(MerchantInventory __instance) =>
        AdaptationRegistry.Run("reward", "AncientShopCards", () => AncientShopCards.Apply(__instance));
}

[HarmonyPatch(typeof(MerchantCardEntry), "GetCost")]
internal static class BridgeAncientShopPrice
{
    private static void Postfix(CardModel card, ref int __result)
    {
        var cost = __result;
        AdaptationRegistry.Run("reward", "AncientShopPrice", () => AncientShopPrice.Apply(card, ref cost));
        __result = cost;
    }
}

[HarmonyPatch(typeof(DingyRug), nameof(DingyRug.ModifyCardRewardCreationOptions))]
internal static class BridgeConstructDingyRug
{
    private static bool Prefix(DingyRug __instance, Player player, CardCreationOptions options, ref CardCreationOptions __result)
    {
        var result = __result;
        var runNative = AdaptationRegistry.Guard("reward", "ConstructDingyRug",
            () => ConstructDingyRug.Apply(__instance, player, options, ref result));
        __result = result;
        return runNative;
    }
}

[HarmonyPatch]
internal static class BridgePandoraWaveEligibility
{
    private static MethodBase TargetMethod() => PandoraWaveEligibility.Target();

    private static void Postfix(CardModel __0, ref bool __result)
    {
        var eligible = __result;
        AdaptationRegistry.Run("reward", "PandoraWaveEligibility",
            () => PandoraWaveEligibility.Apply(__0, ref eligible));
        __result = eligible;
    }
}

[HarmonyPatch(typeof(PrismaticGem), nameof(PrismaticGem.ModifyCardRewardCreationOptions))]
internal static class BridgeConstructPrismaticGem
{
    private static bool Prefix(PrismaticGem __instance, Player player, CardCreationOptions options, ref CardCreationOptions __result)
    {
        var result = __result;
        var runNative = AdaptationRegistry.Guard("reward", "ConstructPrismaticGem",
            () => ConstructPrismaticGem.Apply(__instance, player, options, ref result));
        __result = result;
        return runNative;
    }
}

[HarmonyPatch(typeof(CardModel), nameof(CardModel.Pool), MethodType.Getter)]
internal static class BridgeNativeCardOwnership
{
    private static void Postfix(CardModel __instance, ref CardPoolModel __result, ref CardPoolModel ____pool)
    {
        var pool = __result;
        var backing = ____pool;
        AdaptationRegistry.Run("reward", "NativeCardOwnership",
            () => NativeCardOwnership.Apply(__instance, ref pool, ref backing));
        __result = pool;
        ____pool = backing;
    }
}
