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
    internal static bool Apply(CardModel original, bool isInCombat, ref IEnumerable<CardModel> __result)
    {
        if (!RegentAdaptations.Owns(original) || original.Type is CardType.Curse or CardType.Status || original.Rarity == CardRarity.Ancient) return true;
        var previous = CardFactoryScope.Player;
        CardFactoryScope.Player = original.Owner;
        try
        {
            __result = (CardModel[])AccessTools.Method(typeof(CardFactory), "GetFilteredTransformationOptions")
                .Invoke(null, [original, original.Owner.Character.CardPool.AllCards, isInCombat])!;
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
    internal static List<CodeInstruction> Transpile(List<CodeInstruction> original)
    {
        var rewritten = new List<CodeInstruction>(original.Count);
        foreach (var instruction in original)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Rng) && method.Name == nameof(Rng.NextItem) && method.IsGenericMethod && method.GetGenericArguments().SequenceEqual([typeof(CardModel)]))
            {
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(instruction.labels); load.blocks.AddRange(instruction.blocks);
                rewritten.Add(load);
                rewritten.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TransformAffinity), nameof(Pick))));
            }
            else rewritten.Add(instruction);
        }
        return rewritten;
    }
    internal static CardModel Pick(Rng rng, IEnumerable<CardModel> candidates, CardModel original) => CardAcquisition.Pick(rng, candidates, original.Owner);
}
