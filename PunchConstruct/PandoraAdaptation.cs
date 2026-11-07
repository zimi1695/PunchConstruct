using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace PunchConstruct;

// Keep the native batch transform, RNG, result preview and ordinary Strike /
// Defend rule. Extend only this relic's own predicate for this character.
internal static class PandoraWaveEligibility
{
    internal static MethodBase Target() => AccessTools.FirstMethod(
        typeof(PandorasBox).GetNestedType("<>c", BindingFlags.NonPublic)!,
        m => m.Name.Contains("AfterObtained") && m.ReturnType == typeof(bool));

    internal static void Apply(CardModel __0, ref bool __result)
    {
        if (__0 is SmallWave && CharacterScope.IsConstruct(__0))
            __result = __0.IsRemovable && __0.IsTransformable;
    }
}
