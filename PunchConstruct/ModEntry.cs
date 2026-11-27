using MegaCrit.Sts2.Core.Modding;

namespace PunchConstruct;

[ModInitializer(nameof(Initialize))]
public static class ModEntry
{
    private static bool initialized;

    // Diagnostic flag set when CombatRelicState registration fails (e.g. a BaseLib
    // API change). Surfaced in the startup line below; the per-hook guards fall back
    // to native behavior independently of this flag.
    internal static bool CombatRelicStateDegraded { get; private set; }

    public static void Initialize()
    {
        // A loader that calls the initializer twice must not reinstall patches
        // or reset combat-scoped state.
        if (initialized) return;
        initialized = true;
        Diagnostics.Initialize();
        try
        {
            CombatRelicState.Initialize();
        }
        catch (Exception error)
        {
            CombatRelicStateDegraded = true;
            Diagnostics.Write("error", "combat_relic_state.init_failed", new { error = error.ToString() });
        }
        try
        {
            CompatibilityPrecheck.Run();
        }
        catch (Exception error)
        {
            Health.MarkPrecheckFailed();
            Diagnostics.Write("warn", "compat.precheck_failed", new { error = error.ToString() });
        }
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
        // ReleaseInfoManager.Instance is lazy but its constructor reads a config
        // file; a read/parse failure must not abort init after patches installed.
        string gameVersion;
        try
        {
            gameVersion = MegaCrit.Sts2.Core.Debug.ReleaseInfoManager.Instance.ReleaseInfo?.Version
                ?? typeof(MegaCrit.Sts2.Core.Runs.RunManager).Assembly.GetName().Version?.ToString() ?? "unknown";
        }
        catch (Exception error)
        {
            gameVersion = typeof(MegaCrit.Sts2.Core.Runs.RunManager).Assembly.GetName().Version?.ToString() ?? "unknown";
            Diagnostics.Write("warn", "compat.version_read_failed", new { error = error.ToString() });
        }
        string baseLibVersion = typeof(BaseLib.Abstracts.CustomRelicModel).Assembly.GetName().Version?.ToString() ?? "unknown";
        Godot.GD.Print($"[PunchConstruct] v{typeof(ModEntry).Assembly.GetName().Version} loaded (game {gameVersion} / BaseLib {baseLibVersion}; per-hook patches + relic variants{(CombatRelicStateDegraded ? "; combat-relic-state DEGRADED" : "")})");
        // F3(a)(b): diagnostic only — max-tested metadata + PASS/DEGRADED. No version
        // gate, no character hiding, no popup; DEGRADED means a soft ref or optional
        // bridge degraded to its native fallback, the character stays selectable.
        Health.CaptureStartup();
        var issues = Health.Issues();
        bool degraded = issues.Count > 0;
        Godot.GD.Print($"[PunchConstruct COMPAT] {(degraded ? "DEGRADED" : "PASS")} " +
            $"(soft-ref {CompatibilityPrecheck.Unresolved}/{CompatibilityPrecheck.Total} unresolved, patch failed {PatchRegistry.Failed}, " +
            $"required failed {(PatchRegistry.RequiredFailure ? PatchRegistry.RequiredFailures.Count : 0)}, " +
            $"il skipped {Health.UnexpectedSkipped}, adapter disabled {AdaptationRegistry.DisabledProductionHooks.Count}; " +
            $"max tested game 0.114.0 / BaseLib 3.4.8)" +
            (degraded ? " reasons: " + string.Join("; ", issues) : ""));
        Diagnostics.Write("info", "compat.startup", new
        {
            status = degraded ? "DEGRADED" : "PASS",
            softRefUnresolved = CompatibilityPrecheck.Unresolved,
            softRefTotal = CompatibilityPrecheck.Total,
            patchFailed = PatchRegistry.Failed,
            requiredFailed = PatchRegistry.RequiredFailure ? PatchRegistry.RequiredFailures.Count : 0,
            ilSkipped = Health.UnexpectedSkipped,
            adapterDisabled = AdaptationRegistry.DisabledProductionHooks.Count,
            combatRelicStateDegraded = CombatRelicStateDegraded,
            poolMissingCards = ConstructCardPool.MissingCards,
            relicWeightsDegraded = RelicWeights.Degraded,
            maxTestedGame = "0.114.0",
            maxTestedBaseLib = "3.4.8",
            reasons = issues
        });
    }
}
