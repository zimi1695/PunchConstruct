using System;
using System.Collections.Generic;
using System.Linq;

namespace PunchConstruct;

// One startup health snapshot shared by the COMPAT line and the QA gate. Every
// degradation source that keeps the mod playable on a native fallback is listed
// here, so a top-level abort or a lazily built pool cannot leave the printed
// status at PASS while a feature is already offline.
internal static class Health
{
    private static int startupSkipped;
    private static bool captured;
    private static bool precheckFailed;

    internal static void CaptureStartup()
    {
        startupSkipped = IlBridge.Skipped;
        captured = true;
    }

    internal static void MarkPrecheckFailed() => precheckFailed = true;

    // QA's transpiler-degradation fixture provokes a known number of skips on
    // purpose. Advance the baseline by exactly that count so those tests stay
    // green while any other post-startup skip still fails the health gate.
    internal static void IgnoreDeliberateSkips(int count)
    {
        if (count > 0) startupSkipped += count;
    }

    // QA deliberately exercises IlBridge.Skip; only skips after the startup
    // baseline are unexpected runtime degradation.
    internal static int UnexpectedSkipped => captured
        ? Math.Max(0, IlBridge.Skipped - startupSkipped)
        : IlBridge.Skipped;

    internal static IReadOnlyList<string> Issues()
    {
        var issues = new List<string>();
        if (precheckFailed) issues.Add("precheck failed");
        if (CompatibilityPrecheck.Degraded)
            issues.Add($"soft-ref {CompatibilityPrecheck.Unresolved}/{CompatibilityPrecheck.Total}");
        if (PatchRegistry.RequiredFailure)
            issues.Add("required patch failed: " + string.Join(", ", PatchRegistry.RequiredFailures));
        if (PatchRegistry.Failed > 0)
            issues.Add($"patch failed {PatchRegistry.Failed}");
        if (UnexpectedSkipped > 0)
            issues.Add($"il skipped {UnexpectedSkipped}");
        var disabled = AdaptationRegistry.DisabledProductionHooks;
        if (disabled.Count > 0)
            issues.Add("adapter disabled: " + string.Join(", ", disabled));
        if (ModEntry.CombatRelicStateDegraded) issues.Add("combat-relic-state");
        if (ConstructCardPool.PoolDegraded)
            issues.Add("pool missing: " + string.Join(", ", ConstructCardPool.MissingCards));
        if (RelicWeights.Degraded) issues.Add("relic weights missing native ids");
        return issues;
    }

    internal static bool Degraded => Issues().Count > 0;
}
