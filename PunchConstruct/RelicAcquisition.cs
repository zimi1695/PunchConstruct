using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
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
    // Shared relics with a construct variant are never rolled natively; the
    // variant in the character pool carries the adapted rules.
    internal static bool Allowed(RelicModel relic) => relic is not (Orichalcum or FakeOrichalcum or ChemicalX or MassiveScroll) &&
        !AdaptedRelicVariants.IsNativeAdapted(relic);
}

internal static class RelicFactoryScope
{
    [ThreadStatic] internal static Player? CurrentPlayer;
    [ThreadStatic] internal static Rng? CurrentRng;
    internal static IEnumerable<MethodBase> Targets() => typeof(RelicFactory).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Where(m => m.Name.StartsWith("PullNextRelic") && m.GetParameters().FirstOrDefault()?.ParameterType == typeof(Player));
    internal static void Apply(Player player, out Player? __state) { __state = CurrentPlayer; CurrentPlayer = player; }
    internal static void Restore(Player? __state) => CurrentPlayer = __state;
}


internal static class ConstructRelicRolls
{
    private static readonly MethodInfo Available = AccessTools.Method(typeof(RelicGrabBag), "GetAvailableDeque");
    // Relic weighting uses its own deterministic stream: it never advances the
    // native RNG, yet still gets a fresh draw on every pull. Seeded from the run
    // seed and player slot so multiplayer peers agree.
    private static readonly ConditionalWeakTable<Player, Rng> WeightStreams = new();
    private static Rng WeightStream(Player player) => WeightStreams.GetValue(player,
        p => new Rng(p, ModelDb.GetId<ConstructCharacter>(), 0x52454C4943574549UL));
    internal static IEnumerable<MethodBase> Targets() => typeof(RelicGrabBag).GetMethods()
        .Where(m => m.Name is "PullFromFront" or "PullFromBack" && m.GetParameters().Length == 3);
    internal static bool Apply(RelicGrabBag __instance, RelicRarity rarity, Func<RelicModel, bool> filter, IRunState runState, MethodBase __originalMethod, ref RelicModel? __result)
    {
        var player = RelicFactoryScope.CurrentPlayer ?? runState.Players.FirstOrDefault(p => ReferenceEquals(p.RelicGrabBag, __instance));
        if (player is null || !CharacterScope.IsConstruct(player)) return true;
        bool Eligible(RelicModel r) => RelicWeights.Allowed(r) && filter(r);
        var deque = (List<RelicModel>?)Available.Invoke(__instance, [rarity, runState, (Func<RelicModel, bool>)Eligible]);
        var candidates = deque?.Where(Eligible).ToArray() ?? [];
        // The native consumer expects a relic and dereferences it. Never return
        // null: fall back to the same Circlet replacement the game uses when a
        // pool has no candidates left.
        if (candidates.Length == 0) { __result = ModelDb.Relic<Circlet>(); return false; }
        // Relic weighting must not consume the native stream: vanilla pulls take
        // the first deque entry without a roll. Use the mod's own stream so RNG
        // predictors that replay the native sequence stay aligned.
        __result = WeightStream(player).WeightedNextItem(candidates, RelicWeights.Weight);
        if (__result != null) deque!.Remove(__result);
        return false;
    }
}

internal static class ConstructTreasureRelics
{
    internal static List<CodeInstruction> Transpile(List<CodeInstruction> original)
    {
        var pull = AccessTools.Method(typeof(RelicGrabBag), nameof(RelicGrabBag.PullFromFront), [typeof(RelicRarity), typeof(IRunState)]);
        var rng = AccessTools.Field(typeof(TreasureRoomRelicSynchronizer), "_rng");
        return IlBridge.ReplaceCall(original, pull, call =>
            new CodeInstruction[]
            {
                // The native loop's player local identifies whose contribution is
                // being generated. The shared bag has no owner and bypasses RelicFactory.
                IlBridge.Inherit(call, new CodeInstruction(OpCodes.Ldloc_1)),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, rng),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ConstructTreasureRelics), nameof(Pull)))
            },
            "treasure shared relic");
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

internal static class NeowScrollExclusion
{
    internal static void Apply(Neow __instance, ref IReadOnlyList<EventOption> __result) =>
        __result = Filter(__result, __instance.Owner);
    internal static IReadOnlyList<EventOption> Filter(IReadOnlyList<EventOption> options, Player? owner) =>
        CharacterScope.IsConstruct(owner) && options.Any(o => o.Relic is MassiveScroll)
            ? options.Where(o => o.Relic is not MassiveScroll).ToArray() : options;
}

internal static class FakeMerchantExclusions
{
    internal static List<CodeInstruction> Transpile(List<CodeInstruction> original)
    {
        var rewritten = new List<CodeInstruction>(original.Count);
        var field = AccessTools.Field(typeof(FakeMerchant), "_inventoryRelics");
        foreach (var instruction in original)
        {
            rewritten.Add(instruction);
            if (instruction.LoadsField(field))
            {
                rewritten.Add(new CodeInstruction(OpCodes.Ldarg_0));
                rewritten.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(FakeMerchantExclusions), nameof(Filter))));
            }
        }
        return rewritten;
    }
    private static RelicModel[] Filter(RelicModel[] originals, FakeMerchant room) => CharacterScope.IsConstruct(room.Owner)
        ? originals.Where(RelicWeights.Allowed).ToArray() : originals;
}

internal static class WindDollPriority
{
    private static MethodInfo? _stableShuffle;
    internal static IEnumerable<MethodBase> Targets() => typeof(DollRoom).GetNestedTypes(BindingFlags.NonPublic)
        .Where(t => t.Name.StartsWith("<ChooseRandom>") || t.Name.StartsWith("<TakeSomeTime>") || t.Name.StartsWith("<Examine>"))
        .Select(t => AccessTools.Method(t, "MoveNext"));
    internal static List<CodeInstruction> Transpile(List<CodeInstruction> original, MethodBase __originalMethod)
    {
        var rewritten = new List<CodeInstruction>(original.Count);
        var owner = AccessTools.Field(__originalMethod.DeclaringType, "<>4__this");
        foreach (var instruction in original)
        {
            if (instruction.operand is MethodInfo method && method.IsGenericMethod && method.Name is "NextItem" or "StableShuffle")
            {
                string replacement = method.Name == "NextItem" ? nameof(Next) : nameof(Shuffle);
                if (method.Name == "StableShuffle") _stableShuffle = method.GetGenericMethodDefinition();
                var load = new CodeInstruction(OpCodes.Ldarg_0); load.labels.AddRange(instruction.labels);
                rewritten.Add(load);
                rewritten.Add(new CodeInstruction(OpCodes.Ldfld, owner));
                rewritten.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(WindDollPriority), replacement).MakeGenericMethod(method.GetGenericArguments())));
            }
            else rewritten.Add(instruction);
        }
        return rewritten;
    }
    private static bool IsWind<T>(T item) => typeof(T).GetField("relic")!.GetValue(item) is DaughterOfTheWind;
    private static T Next<T>(Rng rng, IEnumerable<T> items, DollRoom room)
    {
        // Consume the native draw even when the wind doll wins, so a predictor
        // replaying the vanilla stream stays on the same random counter.
        var native = rng.NextItem(items);
        if (CharacterScope.IsConstruct(room.Owner))
        {
            var wind = items.FirstOrDefault(IsWind);
            if (wind is not null) return wind;
        }
        return native!;
    }
    private static List<T> Shuffle<T>(List<T> items, Rng rng, DollRoom room)
    {
        var shuffled = (List<T>)_stableShuffle!.MakeGenericMethod(typeof(T)).Invoke(null, [items, rng])!;
        // Keep the other two dolls in their native random order; never mutate the shared _dolls array.
        return CharacterScope.IsConstruct(room.Owner) ? shuffled.OrderBy(item => IsWind(item) ? 0 : 1).ToList() : shuffled;
    }
}
