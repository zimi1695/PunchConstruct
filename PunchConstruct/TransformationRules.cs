using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace PunchConstruct;

// Reward-domain adapter: see NativeBridge/RewardBridge.cs.
internal static class ConstructTransformPool
{
    private static readonly SoftRef.Ref<MethodInfo> FilteredTransform =
        SoftRef.Register<MethodInfo>("CardFactory.GetFilteredTransformationOptions", () => AccessTools.Method(typeof(CardFactory), "GetFilteredTransformationOptions"));

    internal static bool Apply(CardModel original, bool isInCombat, ref IEnumerable<CardModel> __result)
    {
        if (!RegentAdaptations.Owns(original) || original.Type is CardType.Curse or CardType.Status || original.Rarity == CardRarity.Ancient) return true;
        var filtered = FilteredTransform.Value;
        if (filtered == null) return true;
        var previous = CardFactoryScope.Player;
        CardFactoryScope.Player = original.Owner;
        try
        {
            __result = (CardModel[])filtered.Invoke(null, [original, original.Owner.Character.CardPool.AllCards, isInCombat])!;
        }
        catch (Exception error)
        {
            Diagnostics.Write("warn", "transform.pool.native_fallback", new { error = error.ToString() });
            return true;
        }
        finally { CardFactoryScope.Player = previous; }
        return false;
    }
}

internal static class TransformDeckGates
{
    internal static void Apply(CardModel original, ref CardModel[] __result)
    {
        if (RegentAdaptations.Owns(original) && original.Type is not (CardType.Curse or CardType.Status))
            __result = __result.Where(c => CardAcquisition.Eligible(original.Owner, c)).ToArray();
    }
}

internal static class TransformAffinity
{
    internal static IEnumerable<MethodBase> Targets() => typeof(CardFactory).GetMethods().Where(m => m.Name == nameof(CardFactory.CreateRandomCardForTransform));
    internal static List<CodeInstruction> Transpile(List<CodeInstruction> original) =>
        IlBridge.ReplaceAll(original,
            i => i.operand is MethodInfo m && m.DeclaringType == typeof(Rng) && m.Name == nameof(Rng.NextItem) && m.IsGenericMethod && m.GetGenericArguments().SequenceEqual([typeof(CardModel)]),
            call =>
            {
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(call.labels); load.blocks.AddRange(call.blocks);
                return [load, new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TransformAffinity), nameof(Pick)))];
            },
            "transform affinity");
    internal static CardModel Pick(Rng rng, IEnumerable<CardModel> candidates, CardModel original) => CardAcquisition.Pick(rng, candidates, original.Owner);
}
