using HarmonyLib;
using MegaCrit.Sts2.Core.Models;

namespace PunchConstruct;

// The game finds a card's native pool by taking the first matching pool. Our reward list
// reuses vanilla cards, so keep their original ownership (and hence artwork/localization).
// This never changes a vanilla pool or model definition.
internal static class NativeCardOwnership
{
    internal static void Apply(CardModel __instance, ref CardPoolModel __result, ref CardPoolModel ____pool)
    {
        if (__result is not ConstructCardPool || __instance.GetType().Assembly == typeof(ConstructCharacter).Assembly) return;
        // If the backing field already holds this card's native pool, keep it.
        // Re-resolving by enumeration can pick a different native pool that also
        // lists the card, changing ownership for no reason.
        var current = ____pool;
        if (current is not null && current is not ConstructCardPool &&
            current.GetType().Assembly == typeof(CardModel).Assembly && current.AllCardIds.Contains(__instance.Id))
        {
            __result = current;
            return;
        }
        // Resolve among native game-assembly pools only. A third-party "all cards"
        // or extension pool that also lists this card must not win by enumeration
        // order (wrong frame/rarity/codex ownership).
        var nativeAssembly = typeof(CardModel).Assembly;
        var native = ModelDb.AllCardPools.FirstOrDefault(p => p is not ConstructCardPool &&
            p.GetType().Assembly == nativeAssembly && p.AllCardIds.Contains(__instance.Id));
        if (native == null) return;
        __result = native;
        ____pool = native;
    }
}
