using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;

namespace PunchConstruct;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    public static void Initialize()
    {
        Diagnostics.Initialize();
        CombatRelicState.Initialize();
        new Harmony("IronWave.PunchConstruct").PatchAll(Assembly.GetExecutingAssembly());
        Godot.GD.Print("[PunchConstruct] v0.4.24 loaded (game 0.111.0 / BaseLib 3.4.7; standalone observer cards)");
    }
}
