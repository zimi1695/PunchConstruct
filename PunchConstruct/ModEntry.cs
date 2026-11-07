using MegaCrit.Sts2.Core.Modding;

namespace PunchConstruct;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    private static bool initialized;

    public static void Initialize()
    {
        // A loader that calls the initializer twice must not reinstall patches
        // or reset combat-scoped state.
        if (initialized) return;
        initialized = true;
        Diagnostics.Initialize();
        CombatRelicState.Initialize();
        PatchRegistry.ApplyAll();
        Godot.GD.Print("[PunchConstruct] v0.4.26 loaded (game 0.111.0 / BaseLib 3.4.7; native bridge + adapters)");
    }
}
