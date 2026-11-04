using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Entities.Players;

namespace PunchConstruct;

internal static class WaveEventRules
{
    internal static bool Small(CardModel card) => card is SmallWave && card.IsRemovable;
    internal static bool Iron(CardModel card) => card is IronWave && card.IsTransformable;
    internal static CardModel ScorchingReplacement(CardModel source, int upgrades)
    {
        var result = source.Owner.RunState.CreateCard<ScorchingIronWave>(source.Owner);
        // This is a fixed upgrade count, independent of the removed starter's upgrade.
        for (int i = 0; i < upgrades; i++) CardCmd.Upgrade(result);
        if (source.Enchantment is { } enchantment)
            CardCmd.Enchant((EnchantmentModel)enchantment.MutableClone(), result, enchantment.Amount);
        return result;
    }
    internal static void Finish(EventModel room, string page) => AccessTools.Method(typeof(EventModel), "SetEventFinished")
        .Invoke(room, [new LocString("events", "PUNCHCONSTRUCT_" + room.Id.Entry + ".pages." + page + ".description")]);
}

[HarmonyPatch(typeof(Bugslayer), "GenerateInitialOptions")]
internal static class ConstructBugslayer
{
    private static bool Prefix(Bugslayer __instance, ref IReadOnlyList<EventOption> __result)
    {
        if (__instance.Owner?.Character is not ConstructCharacter) return true;
        __result = new[] { typeof(QuickPunch), typeof(CombinationPunch), typeof(BoxingGuard) }.Select((type, index) =>
        {
            string page = new[] { "QUICK", "COMBINATION", "GUARD" }[index];
            string key = "PUNCHCONSTRUCT_BUGSLAYER.pages.INITIAL.options." + page;
            // Keep the original add-to-deck animation and event completion.
            Task Receive() => (Task)AccessTools.Method(typeof(Bugslayer), "AddAndPreview").MakeGenericMethod(type)
                .Invoke(__instance, [new LocString("events", "PUNCHCONSTRUCT_BUGSLAYER.pages." + page + ".description")])!;
            return new EventOption(__instance, Receive, new LocString("events", key + ".title"),
                new LocString("events", key + ".description"), key, [HoverTipFactory.FromCard(ModelDb.GetById<CardModel>(ModelDb.GetId(type)))]);
        }).ToArray();
        return false;
    }
}

// Reuse the native observation selection, enchant animation and synchronization.
[HarmonyPatch(typeof(Spiral), nameof(Spiral.CanEnchant))]
internal static class SmallWaveSpiral
{
    private static void Postfix(CardModel __0, ref bool __result)
    {
        if (!RegentAdaptations.Owns(__0)) return;
        __result = __0 is SmallWave &&
            __0.Enchantment == null && !__0.Keywords.Contains(CardKeyword.Unplayable);
    }
}

[HarmonyPatch(typeof(Amalgamator), "GenerateInitialOptions")]
internal static class ConstructAmalgamation
{
    private static bool Prefix(Amalgamator __instance, ref IReadOnlyList<EventOption> __result)
    {
        if (__instance.Owner?.Character is not ConstructCharacter) return true;
        string prefix = "PUNCHCONSTRUCT_AMALGAMATOR.pages.INITIAL.options.";
        EventOption Option(string key, Func<Task> action, bool enabled, IEnumerable<IHoverTip> tips) => new(__instance,
            enabled ? action : null!, new LocString("events", prefix + key + ".title"),
            new LocString("events", prefix + key + ".description"), prefix + key, tips);
        __result = [
            Option("COMBINE_STRIKES", () => Combine(__instance), __instance.Owner.Deck.Cards.Count(WaveEventRules.Small) >= 2,
                HoverTipFactory.FromCardWithCardHoverTips<IronWave>()),
            Option("COMBINE_DEFENDS", () => Reforge(__instance), __instance.Owner.Deck.Cards.Any(WaveEventRules.Iron),
                [HoverTipFactory.FromCard<ScorchingIronWave>(true)])
        ];
        return false;
    }
    private static Task Combine(Amalgamator room) => (Task)AccessTools.Method(typeof(Amalgamator), "CombineStrikes").Invoke(room, [])!;
    private static async Task Reforge(Amalgamator room)
    {
        var cards = await CardSelectCmd.FromDeckGeneric(room.Owner!,
            new CardSelectorPrefs(CardSelectorPrefs.TransformSelectionPrompt, 1), WaveEventRules.Iron);
        foreach (var source in cards) await CardCmd.Transform(source, WaveEventRules.ScorchingReplacement(source, 1));
        WaveEventRules.Finish(room, "COMBINE_DEFENDS");
    }
}

// Keep native ingredient selection, smithing sounds, previews and choice sync;
// replace only this owner's generated reward inside the native async callback.
[HarmonyPatch]
internal static class ConstructFusionReward
{
    private static MethodBase TargetMethod() => AccessTools.Method(typeof(Amalgamator).GetNestedTypes(BindingFlags.NonPublic)
        .Single(t => t.Name.StartsWith("<CombineStrikes>")), "MoveNext");
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        int found = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.Name == "CreateCard" && method.IsGenericMethod &&
                method.GetGenericArguments().SequenceEqual([typeof(UltimateStrike)]))
            {
                var replacement = new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ConstructFusionReward), nameof(Create)));
                replacement.labels.AddRange(instruction.labels); replacement.blocks.AddRange(instruction.blocks);
                yield return replacement;
                found++;
            }
            else yield return instruction;
        }
        if (found != 1) throw new InvalidOperationException("Expected one native fusion reward creation.");
    }
    private static CardModel Create(IRunState run, Player owner) => owner.Character is ConstructCharacter
        ? run.CreateCard<IronWave>(owner) : run.CreateCard<UltimateStrike>(owner);
}

[HarmonyPatch]
internal static class ConstructAmalgamationEligibility
{
    // Patch the per-player predicate so mixed parties keep each native teammate's rules.
    private static MethodBase TargetMethod() => typeof(Amalgamator).GetNestedTypes(BindingFlags.NonPublic)
        .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
        .Single(m => m.Name.StartsWith("<IsAllowed>") && m.GetParameters() is [{ ParameterType: var p }] &&
            p == typeof(MegaCrit.Sts2.Core.Entities.Players.Player));
    private static bool Prefix(MegaCrit.Sts2.Core.Entities.Players.Player __0, ref bool __result)
    {
        if (__0.Character is not ConstructCharacter) return true;
        __result = __0.Deck.Cards.Count(WaveEventRules.Small) >= 2 || __0.Deck.Cards.Any(WaveEventRules.Iron);
        return false;
    }
}

[HarmonyPatch(typeof(Amalgamator), "IsValid")]
internal static class ConstructFusionIngredients
{
    private static bool Prefix(CardTag __0, CardModel __1, ref bool __result)
    {
        if (!RegentAdaptations.Owns(__1)) return true;
        __result = __0 == CardTag.Strike && WaveEventRules.Small(__1);
        return false;
    }
}
