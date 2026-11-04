using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Nodes;

namespace PunchConstruct;

// Only explicit QA uses this barrier. Normal gameplay and native preloading
// retain their own scheduling; the existing task is observed, never restarted.
[HarmonyPatch(typeof(PreloadManager), "LoadAssets")]
internal static class QaStartupPreload
{
    private static AssetLoadingSession? _pending;
    private static void Postfix(string name, AssetLoadingSession __result)
    {
        if (name == "Common" && OS.GetCmdlineArgs().Any(a => a.StartsWith("--punchconstruct-"))) _pending = __result;
    }

    internal static async Task Wait(SceneTree tree)
    {
        await NGame.Instance!.GameStartupComplete;
        for (int frame = 0; _pending == null && frame < 1800; frame++)
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        var preload = _pending ?? throw new TimeoutException("Native deferred startup preload was not observed");
        await preload.WaitForCompletion().WaitAsync(TimeSpan.FromSeconds(30));
        GD.Print("[Construct QA PRELOAD] Native deferred common preload completed before QA and exit");
    }
}
