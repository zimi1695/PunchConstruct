using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;

namespace PunchConstruct;

// Native cards keep their identity, upgrade/save rules and art for other characters.
[HarmonyPatch]
internal static class ConstructIronWavePortrait
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods() =>
        new[] { nameof(CardModel.PortraitPath), nameof(CardModel.BetaPortraitPath) }
            .Select(name => AccessTools.PropertyGetter(typeof(CardModel), name));

    private static void Postfix(CardModel __instance, ref string __result)
    {
        if (__instance is IronWave && RegentAdaptations.Owns(__instance))
            __result = "res://PunchConstruct/art/iron_wave.ctex";
    }
}
