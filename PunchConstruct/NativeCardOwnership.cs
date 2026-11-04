using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace PunchConstruct;

// The game finds a card's native pool by taking the first matching pool. Our reward list
// reuses vanilla cards, so keep their original ownership (and hence artwork/localization).
// This never changes a vanilla pool or model definition.
[HarmonyPatch(typeof(CardModel), nameof(CardModel.Pool), MethodType.Getter)]
internal static class NativeCardOwnership
{
    private static void Postfix(CardModel __instance, ref CardPoolModel __result, ref CardPoolModel ____pool)
    {
        if (__result is not ConstructCardPool || __instance.GetType().Assembly == typeof(ConstructCharacter).Assembly) return;
        var native = ModelDb.AllCardPools.FirstOrDefault(p => p is not ConstructCardPool && p.AllCardIds.Contains(__instance.Id));
        if (native == null) return;
        __result = native;
        ____pool = native;
    }
}
