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

internal static class ConstructBugslayer
{
    // Let the native generator run, then replace this character's options.
    // A postfix keeps earlier option work from other mods in the result.
    internal static void Apply(Bugslayer __instance, ref IReadOnlyList<EventOption> __result)
    {
        if (!CharacterScope.IsConstruct(__instance.Owner)) return;
        __result = Build(__instance);
    }

    private static IReadOnlyList<EventOption> Build(Bugslayer __instance) =>
        new[] { typeof(QuickPunch), typeof(CombinationPunch), typeof(BoxingGuard) }.Select((type, index) =>
        {
            string page = new[] { "QUICK", "COMBINATION", "GUARD" }[index];
            string key = "PUNCHCONSTRUCT_BUGSLAYER.pages.INITIAL.options." + page;
            // Keep the original add-to-deck animation and event completion.
            Task Receive() => (Task)AccessTools.Method(typeof(Bugslayer), "AddAndPreview").MakeGenericMethod(type)
                .Invoke(__instance, [new LocString("events", "PUNCHCONSTRUCT_BUGSLAYER.pages." + page + ".description")])!;
            return new EventOption(__instance, Receive, new LocString("events", key + ".title"),
                new LocString("events", key + ".description"), key, [HoverTipFactory.FromCard(ModelDb.GetById<CardModel>(ModelDb.GetId(type)))]);
        }).ToArray();
}

// Reuse the native observation selection, enchant animation and synchronization.
internal static class SmallWaveSpiral
{
    internal static void Apply(CardModel __0, ref bool __result)
    {
        if (!RegentAdaptations.Owns(__0)) return;
        __result = __0 is SmallWave &&
            __0.Enchantment == null && !__0.Keywords.Contains(CardKeyword.Unplayable);
    }
}

internal static class ConstructAmalgamation
{
    // Native options are generated first; only this character's two choices
    // are replaced, preserving earlier option work from other mods.
    internal static void Apply(Amalgamator __instance, ref IReadOnlyList<EventOption> __result)
    {
        var owner = __instance.Owner;
        if (owner is null || !CharacterScope.IsConstruct(owner)) return;
        string prefix = "PUNCHCONSTRUCT_AMALGAMATOR.pages.INITIAL.options.";
        EventOption Option(string key, Func<Task> action, bool enabled, IEnumerable<IHoverTip> tips) => new(__instance,
            enabled ? action : null!, new LocString("events", prefix + key + ".title"),
            new LocString("events", prefix + key + ".description"), prefix + key, tips);
        __result = [
            Option("COMBINE_STRIKES", () => Combine(__instance), owner.Deck.Cards.Count(WaveEventRules.Small) >= 2,
                HoverTipFactory.FromCardWithCardHoverTips<IronWave>()),
            Option("COMBINE_DEFENDS", () => Reforge(__instance), owner.Deck.Cards.Any(WaveEventRules.Iron),
                [HoverTipFactory.FromCard<ScorchingIronWave>(true)])
        ];
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
internal static class ConstructFusionReward
{
    internal static MethodBase Target() => AccessTools.Method(typeof(Amalgamator).GetNestedTypes(BindingFlags.NonPublic)
        .Single(t => t.Name.StartsWith("<CombineStrikes>")), "MoveNext");
    internal static List<CodeInstruction> Transpile(List<CodeInstruction> original)
    {
        var create = original.Select(i => i.operand).OfType<MethodInfo>().FirstOrDefault(method =>
            method.Name == "CreateCard" && method.IsGenericMethod &&
            method.GetGenericArguments().SequenceEqual([typeof(UltimateStrike)]));
        if (create == null)
        {
            Diagnostics.Write("warn", "transpiler.skipped", new { site = "amalgamator fusion reward", expected = 1, replaced = 0 });
            return original;
        }
        return IlBridge.ReplaceCall(original, create, call =>
            new CodeInstruction[]
            {
                IlBridge.Inherit(call,
                    new CodeInstruction(OpCodes.Call,
                        AccessTools.Method(typeof(ConstructFusionReward), nameof(Create))))
            },
            "amalgamator fusion reward");
    }
    internal static CardModel Create(IRunState run, Player owner) => CharacterScope.IsConstruct(owner)
        ? run.CreateCard<IronWave>(owner) : run.CreateCard<UltimateStrike>(owner);
}

internal static class ConstructAmalgamationEligibility
{
    // Patch the per-player predicate so mixed parties keep each native teammate's rules.
    internal static MethodBase Target() => typeof(Amalgamator).GetNestedTypes(BindingFlags.NonPublic)
        .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
        .Single(m => m.Name.StartsWith("<IsAllowed>") && m.GetParameters() is [{ ParameterType: var p }] &&
            p == typeof(MegaCrit.Sts2.Core.Entities.Players.Player));
    internal static bool Apply(MegaCrit.Sts2.Core.Entities.Players.Player __0, ref bool __result)
    {
        if (!CharacterScope.IsConstruct(__0)) return true;
        __result = __0.Deck.Cards.Count(WaveEventRules.Small) >= 2 || __0.Deck.Cards.Any(WaveEventRules.Iron);
        return false;
    }
}

internal static class ConstructFusionIngredients
{
    internal static bool Apply(CardTag __0, CardModel __1, ref bool __result)
    {
        if (!RegentAdaptations.Owns(__1)) return true;
        __result = __0 == CardTag.Strike && WaveEventRules.Small(__1);
        return false;
    }
}
