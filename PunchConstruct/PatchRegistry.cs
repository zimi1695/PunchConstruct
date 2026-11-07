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
// other patch in the mod.
internal static class PatchRegistry
{
    private static readonly List<string> Failures = [];
    private static bool appliedOnce;
    private static int appliedCount;
    private static int failedCount;

    internal static int Applied => appliedCount;
    internal static int Failed => failedCount;
    internal static IReadOnlyList<string> RequiredFailures => Failures;

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
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                applied++;
            }
            catch (Exception error)
            {
                failed++;
                bool required = type.GetCustomAttribute<PatchPolicyAttribute>()?.Required ?? false;
                if (required) Failures.Add(type.FullName ?? type.Name);
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
        Diagnostics.Write("info", "patch.summary", new
        {
            applied,
            failed,
            requiredFailures = Failures
        });
    }

    private static bool HasPatch(Type type) =>
        type.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0 ||
        type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            .Any(method => method.GetCustomAttributes(typeof(HarmonyPatch), true).Length > 0);
}
