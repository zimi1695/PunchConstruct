using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

// Native VisualShader stores an fma-based cached program that can be submitted
// before its graph regenerates for GLES3. Keep a separate, equivalent shader and
// scene path so neither the game's shared shader nor native scene cache changes.
[HarmonyPatch(typeof(MonsterModel), "get_VisualsPath")]
internal static class GraphicsCompatibility
{
    internal static bool Applies(bool compatibilityRenderer, bool hasConstruct) => compatibilityRenderer && hasConstruct;
    private static void Postfix(MonsterModel __instance, ref string __result)
    {
        if (__instance is not DecimillipedeSegmentFront and not DecimillipedeSegmentMiddle and not DecimillipedeSegmentBack) return;
        if (!Applies(RenderingServer.GetCurrentRenderingMethod() == "gl_compatibility",
            RunManager.Instance.DebugOnlyGetState()?.Players.Any(p => p.Character is ConstructCharacter) == true)) return;
        __result = "res://PunchConstruct/native_compat/" + System.IO.Path.GetFileName(__result);
    }
}
