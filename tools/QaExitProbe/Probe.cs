using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

// Temporary QA environment only. The candidate remains byte-for-byte intact.
[ModInitializer(nameof(Init))]
public static class QaExitProbe
{
    public static void Init()
    {
        if (!OS.GetCmdlineArgs().Contains("--punchconstruct-modescheck")) throw new InvalidOperationException("QA-only mode required");
        new Harmony("QaExitProbe.ModesOnly").Patch(
            AccessTools.Method(AccessTools.TypeByName("PunchConstruct.PlaytestRunner"), "ModeChecks"),
            postfix: new HarmonyMethod(typeof(QaExitProbe), nameof(Finish)));
    }
    private static void Finish(ref Task __result) => __result = Clean(__result);
    private static async Task Clean(Task checkedTask)
    {
        await checkedTask;
        PoolShutdown.ReleaseCards();
        var tree = (SceneTree)Engine.GetMainLoop();
        for (int i = 0; i < 4; i++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        GD.Print("[QA EXIT] Finished modes-only idle card cleanup; gameplay and error checks unchanged");
    }
}
