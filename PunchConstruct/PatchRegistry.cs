using System.Reflection;
using Godot;
using HarmonyLib;

namespace PunchConstruct;

// Marks a patch class whose failure disables one feature. Optional patches
// degrade silently so another mod's incompatible rewrite cannot stop the mod
// from loading.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class PatchPolicyAttribute : Attribute
{
    internal bool Required { get; }
    internal PatchPolicyAttribute(bool required = false) => Required = required;
}

// Marks a patch class that installs only under explicit QA command-line args.
// Normal play skips these probes entirely (combat hot-path observers, startup
// preload barriers) so they add no production hooks.
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class QaOnlyAttribute : Attribute { }

// Applies one Harmony class at a time and records the result. This replaces
// Assembly-wide PatchAll so a single incompatible target cannot abort every
// other patch in the mod. The audit line is the only place that tells a player
// which bridge hooks are actually live.
internal static class PatchRegistry
{
    private sealed record Result(string Name, bool Required, bool Installed, string? Error);

    private static readonly List<Result> Results = [];
    private static readonly List<string> Failures = [];
    private static bool appliedOnce;
    private static bool applying;
    private static int appliedCount;
    private static int failedCount;

    // The bridges without which the construct character must not be offered. A
    // partial type load or an aborted apply must still fail closed against this
    // roster even when the failing class never reached the per-class loop.
    private static readonly string[] ExpectedRequired =
    [
        "BridgeUnarmBlock", "BridgeConstructRelicVariants", "BridgeConstructRelicMigration",
        "BridgePhilosopherRandomRewards", "BridgeConstructColorlessShop"
    ];

    internal static int Applied => appliedCount;
    internal static int Failed => failedCount;
    internal static bool RequiredFailure { get; private set; }
    internal static IReadOnlyList<string> RequiredFailures => Failures;
    internal static IReadOnlyList<string> AppliedNames => Results.Where(r => r.Installed).Select(r => r.Name).ToArray();
    internal static IReadOnlyList<string> FailedNames => Results.Where(r => !r.Installed).Select(r => r.Name).ToArray();

    internal static void ApplyAll()
    {
        Type[] types;
        try
        {
            types = Assembly.GetExecutingAssembly().GetTypes();
        }
        catch (ReflectionTypeLoadException error)
        {
            // Keep the loadable types; the roster backstop below fails closed for
            // any Required bridge whose type did not load.
            types = error.Types.Where(type => type != null).Cast<Type>().ToArray();
            Diagnostics.Write("error", "patch.types_partial", new { error = error.ToString(), loaded = types.Length });
        }
        catch (Exception error)
        {
            // Cannot enumerate any patch class: fail closed rather than leave the
            // character visible with no bridges installed.
            Diagnostics.Write("error", "patch.types_failed", new { error = error.ToString() });
            RequiredFailure = true;
            return;
        }
        ApplyAll(types);
    }

    // Seam: QA can pass a synthetic type list (e.g. missing a Required bridge) to
    // exercise the fail-closed path without touching the live assembly.
    internal static void ApplyAll(IEnumerable<Type> types)
    {
        // Another loader or a repeated initializer call must not install every
        // Harmony class twice.
        if (appliedOnce || applying)
        {
            Diagnostics.Write("warn", "patch.duplicate_apply", new
            {
                applied = appliedCount,
                failed = failedCount,
                applying
            });
            return;
        }
        applying = true;
        try
        {
            // Read the QA flag defensively: a cmdline failure must not abort the
            // whole apply and leave RequiredFailure unset.
            bool qaMode;
            try { qaMode = OS.GetCmdlineArgs().Any(a => a.StartsWith("--punchconstruct-")); }
            catch (Exception error)
            {
                qaMode = false;
                Diagnostics.Write("warn", "patch.cmdline_failed", new { error = error.ToString() });
            }

            // Each class gets its own id so a partially applied class can be
            // rolled back without touching the other bridges. The audit unions
            // the methods patched under those ids.
            var patched = new HashSet<MethodBase>();
            int applied = 0, failed = 0, qaSkipped = 0;
            foreach (var type in types)
            {
                // Read the policy BEFORE inspecting for patches: an attribute or
                // inspect failure on a Required class must still fail closed.
                bool required;
                try
                {
                    required = type.GetCustomAttribute<PatchPolicyAttribute>()?.Required ?? false;
                }
                catch (Exception error)
                {
                    failed++;
                    Failures.Add(type.FullName ?? type.Name);
                    Results.Add(new Result(type.Name, true, false, "policy read failed: " + error.Message));
                    Diagnostics.Write("error", "patch.policy_failed", new { patch = type.FullName, error = error.ToString() });
                    continue;
                }
                bool hasPatch;
                try
                {
                    hasPatch = HasPatch(type);
                }
                catch (Exception error)
                {
                    failed++;
                    if (required) Failures.Add(type.FullName ?? type.Name);
                    Results.Add(new Result(type.Name, required, false, error.GetType().Name + ": " + error.Message));
                    Diagnostics.Write(required ? "error" : "warn", "patch.inspect_failed", new
                    {
                        patch = type.FullName,
                        required,
                        error = error.ToString()
                    });
                    continue;
                }
                if (!hasPatch) continue;
                if (!qaMode && type.GetCustomAttribute<QaOnlyAttribute>() != null)
                {
                    qaSkipped++;
                    continue;
                }

                string id = "IronWave.PunchConstruct." + (type.FullName ?? type.Name);
                var harmony = new Harmony(id);
                try
                {
                    harmony.CreateClassProcessor(type).Patch();
                    var methods = harmony.GetPatchedMethods().ToArray();
                    patched.UnionWith(methods);
                    // A name-filtered TargetMethods can match nothing after a native
                    // rename: Patch() succeeds but installs no hook. Treat zero targets
                    // as a failure so Required bridges fail closed instead of silently.
                    if (methods.Length == 0)
                    {
                        failed++;
                        if (required) Failures.Add(type.FullName ?? type.Name);
                        Results.Add(new Result(type.Name, required, false, "no target methods matched"));
                        Diagnostics.Write(required ? "error" : "warn", "patch.empty_targets", new { patch = type.FullName, required });
                        continue;
                    }
                    applied++;
                    Results.Add(new Result(type.Name, required, true, null));
                }
                catch (Exception error)
                {
                    failed++;
                    try
                    {
                        harmony.UnpatchAll();
                    }
                    catch (Exception rollback)
                    {
                        Diagnostics.Write("warn", "patch.rollback_failed", new
                        {
                            patch = type.FullName,
                            error = rollback.ToString()
                        });
                    }
                    if (required) Failures.Add(type.FullName ?? type.Name);
                    Results.Add(new Result(type.Name, required, false, error.GetType().Name + ": " + error.Message));
                    Diagnostics.Write(required ? "error" : "warn", "patch.failed", new
                    {
                        patch = type.FullName,
                        required,
                        error = error.ToString()
                    });
                }
            }
            appliedCount = applied;
            failedCount = failed;
            // Roster backstop: a Required bridge absent from the loaded types
            // (partial load) or never installed fails closed even if no per-class
            // error fired.
            foreach (var name in MissingRequired(Results.Where(r => r.Installed).Select(r => r.Name)))
                Failures.Add(name + " (required bridge missing)");
            RequiredFailure = Failures.Count > 0;
            if (qaSkipped > 0) Diagnostics.Write("info", "patch.qa_skipped", new { count = qaSkipped });
            Audit(patched, applied, failed);
        }
        catch (Exception error)
        {
            // Top-level abort: fail closed instead of leaving RequiredFailure=false
            // with only part of the bridges installed.
            RequiredFailure = true;
            Diagnostics.Write("error", "patch.apply_aborted", new { error = error.ToString() });
        }
        finally
        {
            applying = false;
            appliedOnce = true;
        }
    }

    // Pure roster check so QA can prove fail-closed on a synthetic installed set
    // without mutating live patch state.
    internal static IReadOnlyList<string> MissingRequired(IEnumerable<string> installedNames)
    {
        var installed = new HashSet<string>(installedNames, StringComparer.Ordinal);
        return ExpectedRequired.Where(name => !installed.Contains(name)).ToArray();
    }

    // Prints one readable line per required hook plus every failure, so a log
    // shows whether the reload/Neow/relic hooks were installed on this machine.
    private static void Audit(HashSet<MethodBase> patched, int applied, int failed)
    {
        int patchedMethods = patched.Count;
        Diagnostics.Write("info", "patch.summary", new
        {
            applied,
            failed,
            patchedMethods,
            required = Results.Where(r => r.Required).Select(r => new { r.Name, r.Installed }),
            failedNames = FailedNames,
            requiredFailures = Failures
        });
        // Cross-mod coexistence as fact, not assumption: report foreign owners on
        // our patched methods (Harmony.GetPatchInfo). Diagnostic only, try/catch so
        // a reflection failure never aborts the audit.
        try
        {
            var foreign = new HashSet<string>(StringComparer.Ordinal);
            foreach (var method in patched)
            {
                var info = Harmony.GetPatchInfo(method);
                if (info == null) continue;
                foreach (var owner in info.Owners)
                    if (!owner.StartsWith("IronWave.PunchConstruct", StringComparison.Ordinal)) foreign.Add(owner);
            }
            if (foreign.Count > 0)
                Diagnostics.Write("info", "patch.foreign_owners", new { owners = foreign.OrderBy(o => o).ToArray(), count = foreign.Count });
        }
        catch (Exception error)
        {
            Diagnostics.Write("warn", "patch.foreign_owners_failed", new { error = error.ToString() });
        }
        foreach (var result in Results.Where(r => r.Required || !r.Installed))
            Godot.GD.Print($"[PunchConstruct PATCH] {result.Name}: " +
                (result.Installed ? "installed" : "FAILED " + result.Error));
        Godot.GD.Print($"[PunchConstruct PATCH] {applied}/{applied + failed} classes installed, " +
            $"{patchedMethods} native methods patched, required failures: {(Failures.Count == 0 ? "none" : string.Join(", ", Failures))}");
    }

    private static bool HasPatch(Type type) =>
        type.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0 ||
        type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(method => method.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0);
}