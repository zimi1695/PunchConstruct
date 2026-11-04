using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Random;

namespace PunchConstruct;

[HarmonyPatch(typeof(CardFactory), nameof(CardFactory.GetDefaultTransformationOptions))]
internal static class ConstructTransformPool
{
    private static bool Prefix(CardModel original, bool isInCombat, ref IEnumerable<CardModel> __result)
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

[HarmonyPatch(typeof(CardFactory), "GetFilteredTransformationOptions")]
internal static class TransformDeckGates
{
    private static void Postfix(CardModel original, ref CardModel[] __result)
    {
        if (RegentAdaptations.Owns(original) && original.Type is not (CardType.Curse or CardType.Status))
            __result = __result.Where(c => CardAcquisition.Eligible(original.Owner, c)).ToArray();
    }
}

[HarmonyPatch]
internal static class TransformAffinity
{
    private static IEnumerable<MethodBase> TargetMethods() => typeof(CardFactory).GetMethods().Where(m => m.Name == nameof(CardFactory.CreateRandomCardForTransform));
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo method && method.DeclaringType == typeof(Rng) && method.Name == nameof(Rng.NextItem) && method.IsGenericMethod && method.GetGenericArguments().SequenceEqual([typeof(CardModel)]))
            {
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(instruction.labels); load.blocks.AddRange(instruction.blocks);
                yield return load;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(TransformAffinity), nameof(Pick)));
            }
            else yield return instruction;
        }
    }
    private static CardModel Pick(Rng rng, IEnumerable<CardModel> candidates, CardModel original) => CardAcquisition.Pick(rng, candidates, original.Owner);
}
