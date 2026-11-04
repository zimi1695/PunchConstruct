using System.Reflection.Emit;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace PunchConstruct;

[HarmonyPatch(typeof(NFakeMerchant), "StartCharacterAnimation")]
internal static class ConstructFakeMerchantAnimation
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.opcode == OpCodes.Ldstr && Equals(instruction.operand, "relaxed_loop"))
            {
                yield return new CodeInstruction(OpCodes.Ldarg_1);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ConstructFakeMerchantAnimation), nameof(Animation)));
            }
        }
    }
    private static string Animation(string original, NCreatureVisuals visuals) => visuals.HasMeta("punch_construct") ? "idle_loop" : original;
}

[HarmonyPatch(typeof(NMerchantCharacter), nameof(NMerchantCharacter.PlayAnimation))]
internal static class ConstructMerchantAnimation
{
    private static void Prefix(NMerchantCharacter __instance, ref string __0)
    {
        if (__instance.HasMeta("punch_construct")) __0 = "idle_loop";
    }
}

[HarmonyPatch(typeof(NRestSiteCharacter), nameof(NRestSiteCharacter._Ready))]
internal static class ConstructCampAnimation
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        foreach (var instruction in instructions)
        {
            yield return instruction;
            if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string name && name is "overgrowth_loop" or "hive_loop" or "glory_loop")
            {
                yield return new CodeInstruction(OpCodes.Ldarg_0);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(ConstructCampAnimation), nameof(Animation)));
            }
        }
    }
    private static string Animation(string original, NRestSiteCharacter character) => character.HasMeta("punch_construct") ? "idle_loop" : original;
}
