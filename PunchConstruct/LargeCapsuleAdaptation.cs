using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace PunchConstruct;

// This character replaces both starter functions with a combined attack/block
// card. Keep native relic rewards and card-add commands, adapting only lookup.
[HarmonyPatch]
internal static class ConstructCapsuleStarters
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => [
        AccessTools.Method(typeof(LargeCapsule), "GetStrikeForCharacter"),
        AccessTools.Method(typeof(LargeCapsule), "GetDefendForCharacter")
    ];
    private static bool Prefix(CharacterModel character, ref CardModel __result)
    {
        if (character is not ConstructCharacter) return true;
        __result = ModelDb.Card<SmallWave>();
        return false;
    }
}
