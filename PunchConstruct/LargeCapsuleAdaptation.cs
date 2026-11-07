using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;

namespace PunchConstruct;

// This character replaces both starter functions with a combined attack/block
// card. Keep native relic rewards and card-add commands, adapting only lookup.
internal static class ConstructCapsuleStarters
{
    internal static IEnumerable<System.Reflection.MethodBase> Targets() => [
        AccessTools.Method(typeof(LargeCapsule), "GetStrikeForCharacter"),
        AccessTools.Method(typeof(LargeCapsule), "GetDefendForCharacter")
    ];
    internal static bool Apply(CharacterModel character, ref CardModel __result)
    {
        if (!CharacterScope.IsConstruct(character)) return true;
        __result = ModelDb.Card<SmallWave>();
        return false;
    }
}
