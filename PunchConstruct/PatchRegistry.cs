using System.Reflection;
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
    private static int appliedCount;
    private static int failedCount;

    internal static int Applied => appliedCount;
    internal static int Failed => failedCount;
    internal static IReadOnlyList<string> RequiredFailures => Failures;
    internal static IReadOnlyList<string> AppliedNames => Results.Where(r => r.Installed).Select(r => r.Name).ToArray();
    internal static IReadOnlyList<string> FailedNames => Results.Where(r => !r.Installed).Select(r => r.Name).ToArray();

    internal static void ApplyAll()
    {
        // Another loader or a repeated initializer call must not install every
        // Harmony class twice.
        if (appliedOnce)
        {
            Diagnostics.Write("warn", "patch.duplicate_apply", new { applied = appliedCount, failed = failedCount });
            return;
        }
        appliedOnce = true;
        var harmony = new Harmony("IronWave.PunchConstruct");
        int applied = 0, failed = 0;
        foreach (var type in Assembly.GetExecutingAssembly().GetTypes())
        {
            if (!HasPatch(type)) continue;
            bool required = type.GetCustomAttribute<PatchPolicyAttribute>()?.Required ?? false;
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                applied++;
                Results.Add(new Result(type.Name, required, true, null));
            }
            catch (Exception error)
            {
                failed++;
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
        Audit(harmony, applied, failed);
    }

    // Prints one readable line per required hook plus every failure, so a log
    // shows whether the reload/Neow/relic hooks were installed on this machine.
    private static void Audit(Harmony harmony, int applied, int failed)
    {
        int patchedMethods = harmony.GetPatchedMethods().Count();
        Diagnostics.Write("info", "patch.summary", new
        {
            applied,
            failed,
            patchedMethods,
            required = Results.Where(r => r.Required).Select(r => new { r.Name, r.Installed }),
            failedNames = FailedNames,
            requiredFailures = Failures
        });
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
