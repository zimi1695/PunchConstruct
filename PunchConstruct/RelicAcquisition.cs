using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

internal static class RelicWeights
{
    // First playtest value, adjustable without changing the approved list or vanilla rarity channels.
    internal const float PreferredWeight = 2f;
    internal static readonly HashSet<string> Preferred = [
        "BRONZE_SCALES", "ORNAMENTAL_FAN", "KUNAI", "PARRYING_SHIELD", "RAINBOW_RING",
        "SPARKLING_ROUGE", "LETTER_OPENER", "KIFUDA", "DAUGHTER_OF_THE_WIND",
        ModelDb.GetId<OpenEndWrench>().Entry, ModelDb.GetId<SpinningScrew>().Entry,
        ModelDb.GetId<SpareKnuckles>().Entry, ModelDb.GetId<EmergencyOilCan>().Entry
    ];
    internal static float Weight(RelicModel? relic) => relic != null && Preferred.Contains(relic.Id.Entry) ? PreferredWeight : 1f;
    internal static bool Allowed(RelicModel relic) => relic is not (Orichalcum or FakeOrichalcum or ChemicalX or MassiveScroll);
}

[HarmonyPatch]
internal static class RelicFactoryScope
{
    [ThreadStatic] internal static Player? CurrentPlayer;
    [ThreadStatic] internal static Rng? CurrentRng;
    private static IEnumerable<MethodBase> TargetMethods() => typeof(RelicFactory).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(m => m.Name.StartsWith("PullNextRelic") && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(Player));
    private static void Prefix(Player player, out Player? __state) { __state = CurrentPlayer; CurrentPlayer = player; }
    private static void Finalizer(Player? __state) => CurrentPlayer = __state;
}

[HarmonyPatch]
internal static class ConstructRelicRolls
{
    private static readonly MethodInfo Available = AccessTools.Method(typeof(RelicGrabBag), "GetAvailableDeque");
    private static IEnumerable<MethodBase> TargetMethods() => typeof(RelicGrabBag).GetMethods()
        .Where(m => m.Name is "PullFromFront" or "PullFromBack" && m.GetParameters().Length == 3);
    private static bool Prefix(RelicGrabBag __instance, RelicRarity rarity, Func<RelicModel, bool> filter, IRunState runState, MethodBase __originalMethod, ref RelicModel? __result)
    {
        var player = RelicFactoryScope.CurrentPlayer ?? runState.Players.FirstOrDefault(p => ReferenceEquals(p.RelicGrabBag, __instance));
        if (player?.Character is not ConstructCharacter) return true;
        bool Eligible(RelicModel r) => RelicWeights.Allowed(r) && filter(r);
        var deque = (List<RelicModel>?)Available.Invoke(__instance, [rarity, runState, (Func<RelicModel, bool>)Eligible]);
        var candidates = deque?.Where(Eligible).ToArray() ?? [];
        if (candidates.Length == 0) { __result = null; return false; }
        var rng = RelicFactoryScope.CurrentRng ?? (__originalMethod.Name == "PullFromBack" ? player.PlayerRng.Shops : player.PlayerRng.Rewards);
        __result = rng.WeightedNextItem(candidates, RelicWeights.Weight);
        if (__result != null) deque!.Remove(__result);
        return false;
    }
}

[HarmonyPatch(typeof(TreasureRoomRelicSynchronizer), nameof(TreasureRoomRelicSynchronizer.BeginRelicPicking))]
internal static class ConstructTreasureRelics
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var pull = AccessTools.Method(typeof(RelicGrabBag), nameof(RelicGrabBag.PullFromFront), [typeof(RelicRarity), typeof(IRunState)]);
        var rng = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_rng");
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            if (!instruction.Calls(pull)) { yield return instruction; continue; }
            // The native loop's player local identifies whose contribution is being
            // generated. The shared bag has no owner and bypasses RelicFactory.
            var owner = new CodeInstruction(OpCodes.Ldloc_1);
            owner.labels.AddRange(instruction.labels);
            owner.blocks.AddRange(instruction.blocks);
            yield return owner;
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Ldfld, rng);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ConstructTreasureRelics), nameof(Pull)));
            ++replaced;
        }
        if (replaced != 1) throw new InvalidOperationException("Expected one native shared treasure relic draw.");
    }

    internal static RelicModel? Pull(RelicGrabBag bag, RelicRarity rarity, IRunState run, Player player, Rng rng)
    {
        var previousPlayer = RelicFactoryScope.CurrentPlayer;
        var previousRng = RelicFactoryScope.CurrentRng;
        try
        {
            RelicFactoryScope.CurrentPlayer = player;
            RelicFactoryScope.CurrentRng = rng;
            // Native teammates keep the native draw. Construct draws use the
            // same exclusion/weight rules with the synchronized chest RNG.
            return bag.PullFromFront(rarity, run);
        }
        finally
        {
            RelicFactoryScope.CurrentPlayer = previousPlayer;
            RelicFactoryScope.CurrentRng = previousRng;
        }
    }
}

[HarmonyPatch(typeof(Neow), "GenerateInitialOptions")]
internal static class NeowScrollExclusion
{
    private static void Postfix(Neow __instance, ref IReadOnlyList<EventOption> __result) =>
        __result = Filter(__result, __instance.Owner);
    internal static IReadOnlyList<EventOption> Filter(IReadOnlyList<EventOption> options, Player? owner) =>
        owner?.Character is ConstructCharacter && options.Any(o => o.Relic is MassiveScroll)
            ? options.Where(o => o.Relic is not MassiveScroll).ToArray() : options;
}

[HarmonyPatch(typeof(FakeMerchant), "BeforeEventStarted")]
internal static class FakeMerchantExclusions
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var field = AccessTools.Field(typeof(FakeMerchant), "_inventoryRelics");
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.LoadsField(field))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FakeMerchantExclusions), nameof(Filter)));
            }
        }
    }
    private static RelicModel[] Filter(RelicModel[] originals, FakeMerchant room) => room.Owner?.Character is ConstructCharacter
        ? originals.Where(RelicWeights.Allowed).ToArray() : originals;
}

[HarmonyPatch]
internal static class WindDollPriority
{
    private static MethodInfo? _stableShuffle;
    private static IEnumerable<MethodBase> TargetMethods() => typeof(DollRoom).GetNestedTypes(BindingFlags.NonPublic)
        .Where(t => t.Name.StartsWith("<ChooseRandom>") || t.Name.StartsWith("<TakeSomeTime>") || t.Name.StartsWith("<Examine>"))
        .Select(t => AccessTools.Method(t, "MoveNext"));
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var owner = AccessTools.Field(__originalMethod.DeclaringType, "<>4__this");
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.IsGenericMethod && method.Name is "NextItem" or "StableShuffle")
            {
                string replacement = method.Name == "NextItem" ? nameof(Next) : nameof(Shuffle);
                if (method.Name == "StableShuffle") _stableShuffle = method.GetGenericMethodDefinition();
                var load = new CodeInstruction(OpCodes.Ldarg_0); load.labels.AddRange(instruction.labels);
                yield return load;
                yield return new CodeInstruction(OpCodes.Ldfld, owner);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WindDollPriority), replacement).MakeGenericMethod(method.GetGenericArguments()));
            }
            else yield return instruction;
        }
    }
    private static bool IsWind<T>(T item) => typeof(T).GetField("relic")!.GetValue(item) is DaughterOfTheWind;
    private static T Next<T>(Rng rng, IEnumerable<T> items, DollRoom room) => (room.Owner?.Character is ConstructCharacter
        ? items.First(IsWind) : rng.NextItem(items))!;
    private static List<T> Shuffle<T>(List<T> items, Rng rng, DollRoom room)
    {
        var shuffled = (List<T>)_stableShuffle!.MakeGenericMethod(typeof(T)).Invoke(null, [items, rng])!;
        // Keep the other two dolls in their native random order; never mutate the shared _dolls array.
        return room.Owner?.Character is ConstructCharacter ? shuffled.OrderBy(item => IsWind(item) ? 0 : 1).ToList() : shuffled;
    }
}
