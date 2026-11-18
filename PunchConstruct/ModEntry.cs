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
        if (PatchRegistry.RequiredFailure)
        {
            // Fail closed without aborting the mod: the character stays hidden
            // from new runs, while native characters and the rest of the game
            // keep working with the bridges that did install.
            Diagnostics.Write("error", "patch.required_failure", new
            {
                failures = PatchRegistry.RequiredFailures
            });
            Godot.GD.PushError("[PunchConstruct] required bridge patches failed; the construct character is hidden for new runs: " +
                string.Join(", ", PatchRegistry.RequiredFailures));
        }
        string gameVersion = MegaCrit.Sts2.Core.Debug.ReleaseInfoManager.Instance.ReleaseInfo?.Version
            ?? typeof(MegaCrit.Sts2.Core.Runs.RunManager).Assembly.GetName().Version?.ToString() ?? "unknown";
        string baseLibVersion = typeof(BaseLib.Abstracts.CustomRelicModel).Assembly.GetName().Version?.ToString() ?? "unknown";
        Godot.GD.Print($"[PunchConstruct] v{typeof(ModEntry).Assembly.GetName().Version} loaded (game {gameVersion} / BaseLib {baseLibVersion}; per-hook patches + relic variants)");
    }
}
