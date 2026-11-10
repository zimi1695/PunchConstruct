using System.Reflection;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;

namespace PunchConstruct;

// Native Tezcatara event option predicate stays authoritative; only the
// construct's own Small Wave becomes a valid soup ingredient.
internal static class ConstructSoupOffer
{
    internal static MethodBase Predicate => typeof(Tezcatara).GetNestedTypes(BindingFlags.NonPublic)
        .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
        .Single(m => m.Name.StartsWith("<GenerateInitialOptions>") && m.ReturnType == typeof(bool) &&
            m.GetParameters() is [{ ParameterType: var p }] && p == typeof(CardModel));
    internal static MethodBase Target() => Predicate;
    internal static bool Apply(CardModel __0, ref bool __result)
    {
        if (!RegentAdaptations.Owns(__0)) return true;
        __result = __0 is SmallWave && __0.Enchantment == null;
        return false;
    }
}
