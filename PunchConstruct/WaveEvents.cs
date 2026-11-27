using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.CardSelection;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Entities.Gold;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Entities.Players;

namespace PunchConstruct;

internal static class WaveEventRules
{
    private static readonly SoftRef.Ref<MethodInfo> SetEventFinished =
        SoftRef.Register<MethodInfo>("EventModel.SetEventFinished", () => AccessTools.Method(typeof(EventModel), "SetEventFinished"));
    internal static bool CanFinish => SetEventFinished.Value != null;
    internal static MethodInfo? FinishMethod => SetEventFinished.Value;
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
    // Native ancient tooth rule: an upgraded starter yields an upgraded
    // transcendent card, an unupgraded starter yields an unupgraded one, and
    // any enchantment is copied with its amount.
    internal static CardModel DomineeringReplacement(CardModel source)
    {
        var result = source.Owner.RunState.CreateCard<ConstructDomineering>(source.Owner);
        if (source.IsUpgraded) CardCmd.Upgrade(result);
        if (source.Enchantment is { } enchantment)
            CardCmd.Enchant((EnchantmentModel)enchantment.MutableClone(), result, enchantment.Amount);
        return result;
    }
    internal static void Finish(EventModel room, string page)
    {
        var finish = SetEventFinished.Value;
        if (finish == null)
        {
            Diagnostics.Write("error", "event.finish_unavailable", new { room = room.Id.Entry, page });
            return;
        }
        try
        {
            finish.Invoke(room, [new LocString("events", "PUNCHCONSTRUCT_" + room.Id.Entry + ".pages." + page + ".description")]);
        }
        catch (Exception error)
        {
            Diagnostics.Write("error", "event.finish_failed", new { room = room.Id.Entry, page, error = error.ToString() });
        }
    }
}

internal static class ConstructBugslayer
{
    private static readonly SoftRef.Ref<MethodInfo> AddAndPreview =
        SoftRef.Register<MethodInfo>("Bugslayer.AddAndPreview", () => AccessTools.Method(typeof(Bugslayer), "AddAndPreview"));

    // Let the native generator run, then replace this character's options.
    // A postfix keeps earlier option work from other mods in the result. If the
    // native add method is gone, keep the native options instead of offering a
    // choice that cannot complete the event.
    internal static void Apply(Bugslayer __instance, ref IReadOnlyList<EventOption> __result)
    {
        if (!CharacterScope.IsConstruct(__instance.Owner)) return;
        var built = Build(__instance);
        if (built != null) __result = built;
    }

    private static IReadOnlyList<EventOption>? Build(Bugslayer __instance)
    {
        var add = AddAndPreview.Value;
        if (add == null)
        {
            Diagnostics.Write("warn", "event.bugslayer.native_fallback", new { reason = "Bugslayer.AddAndPreview unresolved" });
            return null;
        }
        try
        {
            return new[] { typeof(QuickPunch), typeof(CombinationPunch), typeof(BoxingGuard) }.Select((type, index) =>
            {
                string page = new[] { "QUICK", "COMBINATION", "GUARD" }[index];
                string key = "PUNCHCONSTRUCT_BUGSLAYER.pages.INITIAL.options." + page;
                // Keep the original add-to-deck animation and event completion.
                Task Receive()
                {
                    try
                    {
                        return (Task)add.MakeGenericMethod(type)
                            .Invoke(__instance, [new LocString("events", "PUNCHCONSTRUCT_BUGSLAYER.pages." + page + ".description")])!;
                    }
                    catch (Exception error)
                    {
                        Diagnostics.Write("error", "event.bugslayer.receive_failed", new { card = type.Name, error = error.ToString() });
                        return Task.CompletedTask;
                    }
                }
                return new EventOption(__instance, Receive, new LocString("events", key + ".title"),
                    new LocString("events", key + ".description"), key, [HoverTipFactory.FromCard(ModelDb.GetById<CardModel>(ModelDb.GetId(type)))]);
            }).ToArray();
        }
        catch (Exception error)
        {
            Diagnostics.Write("error", "event.bugslayer.build_failed", new { error = error.ToString() });
            return null;
        }
    }
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
    private static readonly SoftRef.Ref<MethodInfo> CombineStrikes =
        SoftRef.Register<MethodInfo>("Amalgamator.CombineStrikes", () => AccessTools.Method(typeof(Amalgamator), "CombineStrikes"));

    // Native options are generated first; only this character's two choices
    // are replaced, preserving earlier option work from other mods. If either
    // native completion method is gone, keep the native options.
    internal static void Apply(Amalgamator __instance, ref IReadOnlyList<EventOption> __result)
    {
        var owner = __instance.Owner;
        if (owner is null || !CharacterScope.IsConstruct(owner)) return;
        var combine = CombineStrikes.Value;
        if (combine == null || !WaveEventRules.CanFinish)
        {
            Diagnostics.Write("warn", "event.amalgamator.native_fallback", new
            {
                combineResolved = combine != null,
                finishResolved = WaveEventRules.CanFinish
            });
            return;
        }
        string prefix = "PUNCHCONSTRUCT_AMALGAMATOR.pages.INITIAL.options.";
        EventOption Option(string key, Func<Task> action, bool enabled, IEnumerable<IHoverTip> tips) => new(__instance,
            enabled ? action : null!, new LocString("events", prefix + key + ".title"),
            new LocString("events", prefix + key + ".description"), prefix + key, tips);
        __result = [
            Option("COMBINE_STRIKES", () => Combine(__instance, combine), owner.Deck.Cards.Count(WaveEventRules.Small) >= 2,
                HoverTipFactory.FromCardWithCardHoverTips<IronWave>()),
            Option("COMBINE_DEFENDS", () => Reforge(__instance), owner.Deck.Cards.Any(WaveEventRules.Iron),
                [HoverTipFactory.FromCard<ScorchingIronWave>(true)])
        ];
    }
    private static Task Combine(Amalgamator room, MethodInfo combine)
    {
        try { return (Task)combine.Invoke(room, [])!; }
        catch (Exception error)
        {
            Diagnostics.Write("error", "event.amalgamator.combine_failed", new { error = error.ToString() });
            return Task.CompletedTask;
        }
    }
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

// 缺陷构装体修订：keep the native event room and layout; replace only this
// character's page text and options. Native players never enter this path.
internal static class ConstructInfestedAutomaton
{
    internal static void Apply(InfestedAutomaton room, ref IReadOnlyList<EventOption> __result)
    {
        if (!CharacterScope.IsConstruct(room.Owner)) return;
        if (!WaveEventRules.CanFinish)
        {
            Diagnostics.Write("warn", "event.infested_automaton.native_fallback", new { reason = "EventModel.SetEventFinished unresolved" });
            return;
        }
        string prefix = "PUNCHCONSTRUCT_INFESTED_AUTOMATON.pages.INITIAL.options.";
        EventOption Option(string key, Func<Task>? action, bool enabled, IEnumerable<IHoverTip> tips) => new(room,
            enabled ? action : null, new LocString("events", prefix + key + ".title"),
            new LocString("events", prefix + key + ".description"), prefix + key, tips);
        __result = [
            Option("REPAIR", () => Repair(room), room.Owner!.Gold >= 67,
                HoverTipFactory.FromRelic<ConstructClockworkSouvenir>()),
            // SEARCH grants the base panacea (Search() never upgrades); keep the preview unupgraded to match.
            Option("SEARCH", () => Search(room), true,
                [HoverTipFactory.FromCard<ConstructPanacea>(false)])
        ];
    }

    private static async Task Repair(InfestedAutomaton room)
    {
        await PlayerCmd.LoseGold(67, room.Owner!, GoldLossType.Spent);
        await RelicCmd.Obtain<ConstructClockworkSouvenir>(room.Owner!);
        WaveEventRules.Finish(room, "REPAIR");
    }

    private static async Task Search(InfestedAutomaton room)
    {
        var card = room.Owner!.RunState.CreateCard<ConstructPanacea>(room.Owner);
        CardCmd.PreviewCardPileAdd(await CardPileCmd.Add(card, PileType.Deck), 1.2f, CardPreviewStyle.EventLayout);
        WaveEventRules.Finish(room, "SEARCH");
    }
}

internal static class ConstructInfestedAutomatonAllowed
{
    // The room set is shared, so keep the native event for any native party
    // member. An all-construct party sees it only while someone can still use it.
    internal static void Apply(EventModel __instance, IRunState runState, ref bool __result)
    {
        if (__instance is not InfestedAutomaton) return;
        var players = runState.Players;
        if (players.Count == 0 || players.Any(p => !CharacterScope.IsConstruct(p))) return;
        __result = players.Any(p => p.Gold >= 67 && !p.Relics.Any(r => r is ConstructClockworkSouvenir));
    }
}
