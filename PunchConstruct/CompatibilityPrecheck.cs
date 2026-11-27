using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands.Builders;

namespace PunchConstruct;

// One startup pass over the soft-reflection registry. The check count is derived
// from SoftRef, never hardcoded, so the "one pass over the soft reflection
// assumptions" claim matches what actually runs. Diagnostic only (F3): no version
// gate, no character hiding, no popup — DEGRADED just means a soft ref did not
// resolve and that hook keeps its conservative native fallback.
internal static class CompatibilityPrecheck
{
    internal static bool Degraded { get; private set; }
    internal static int Unresolved { get; private set; }
    internal static int Total { get; private set; }

    internal static void Run()
    {
        // Force the consuming types' static init so their lazy descriptors are
        // registered before probing. Constructing a SoftRef.Ref only stores a
        // resolver lambda, so this is safe even when a native member is gone.
        // A type whose own initializer fails is degradation, not a silent skip.
        bool failed = false;
        var consumers = new[]
        {
            typeof(RegentAdaptations), typeof(GuardVigorConsumption), typeof(PresentationAdapter),
            typeof(WaveEventRules), typeof(ConstructBugslayer), typeof(ConstructAmalgamation),
            typeof(PhilosopherColors), typeof(PhilosopherRandomRewards), typeof(ConstructDustyTome),
            typeof(DelayedWaveFacing), typeof(ConstructRelicRolls), typeof(RelicWeights),
            typeof(AncientShopCards), typeof(AncientShopPrice), typeof(ConstructTransformPool),
            typeof(ConstructAncientDialogues)
        };
        foreach (var type in consumers)
        {
            try { RuntimeHelpers.RunClassConstructor(type.TypeHandle); }
            catch (Exception error)
            {
                failed = true;
                Diagnostics.Write("error", "compat.type_init_failed", new { type = type.FullName, error = error.ToString() });
            }
        }
        // RelicWeights keeps its native ModelId lookup behind Lazy so a removed
        // or unregistered relic cannot poison the type; resolve it once here so
        // the health line sees the fallback.
        try { _ = RelicWeights.Preferred; }
        catch (Exception error)
        {
            failed = true;
            Diagnostics.Write("error", "compat.relic_weights_failed", new { error = error.ToString() });
        }

        // Standalone probes for soft refs resolved inline at their use site.
        foreach (var field in new[] { "_hitCount", "_attackerSfx", "_tmpAttackerSfx" })
            SoftRef.Register<FieldInfo>("AttackCommand." + field, () => AccessTools.Field(typeof(AttackCommand), field));

        try
        {
            var missing = SoftRef.ProbeAll();
            Total = SoftRef.Count;
            Unresolved = missing.Count;
            Degraded = failed || missing.Count > 0 || RelicWeights.Degraded;
            if (missing.Count > 0)
                Diagnostics.Write("warn", "compat.reflection_missing", new { missing, total = Total, unresolved = Unresolved });
            else
                Diagnostics.Write("info", "compat.reflection_ok", new { checks = Total });
        }
        catch (Exception error)
        {
            Degraded = true;
            Diagnostics.Write("error", "compat.probe_failed", new { error = error.ToString() });
        }
    }
}
