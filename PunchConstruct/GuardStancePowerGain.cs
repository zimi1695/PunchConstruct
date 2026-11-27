using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace PunchConstruct;

// A blocked hit already has its own damage animation. Keep the native power
// command (including hooks, history and visible feedback), without adding a
// second presentation pause for each segment of an enemy's multi-hit attack.
internal static class GuardStancePowerGain
{
    private sealed record Grant(Creature Target, Type PowerType);
    private static readonly AsyncLocal<Grant?> Current = new();

    internal static async Task Apply<T>(PlayerChoiceContext context, Creature target, int amount)
        where T : PowerModel, new()
    {
        var previous = Current.Value;
        Current.Value = new Grant(target, typeof(T));
        var timer = System.Diagnostics.Stopwatch.StartNew();
        Diagnostics.Write("info", "guard.gain.begin", new { power = typeof(T).Name, grant = amount, before = target.GetPowerAmount<T>() });
        try
        {
            await PowerCmd.Apply<T>(context, target, amount, target, null);
            Diagnostics.Write("info", "guard.gain.end", new { power = typeof(T).Name, grant = amount, after = target.GetPowerAmount<T>(), elapsedMs = timer.ElapsedMilliseconds });
        }
        catch (Exception ex) { Diagnostics.Error("guard.gain.failed", ex); throw; }
        finally { Current.Value = previous; }
    }

    internal static IEnumerable<MethodBase> Targets()
    {
        // These two native commands contain the initial-application and stacking
        // pauses respectively. Resolve their state machines via metadata.
        var targets = new List<MethodBase>();
        foreach (var method in typeof(PowerCmd).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => !m.IsGenericMethod && m.Name is nameof(PowerCmd.Apply) or nameof(PowerCmd.ModifyAmount)))
        {
            var state = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
            if (state == null) continue;
            var move = AccessTools.Method(state, "MoveNext");
            if (move != null) targets.Add(move);
        }
        // The !IsGenericMethod filter is load-bearing: it selects the overloads that
        // hoist a `power` parameter. If a future version makes them generic the hook
        // would silently vanish, so surface the resolved count instead of assuming.
        if (targets.Count < 2)
            Diagnostics.Write("warn", "compat.targets_missing", new { site = "guard stance power gain", resolved = targets.Count, expected = "non-generic PowerCmd.Apply + ModifyAmount" });
        return targets;
    }

    internal static List<CodeInstruction> Transpile(List<CodeInstruction> original, MethodBase __originalMethod)
    {
        const string site = "guard stance power gain";
        var wait = IlBridge.RequireCall(typeof(Cmd), nameof(Cmd.CustomScaledWait), site);
        // Per-target resolution: a state machine without a PowerModel power field
        // skips just this target rather than emitting a null-operand ldfld.
        var power = IlBridge.RequireField(__originalMethod.DeclaringType!, "power", typeof(PowerModel), site);
        if (wait == null || power == null) return original;
        return IlBridge.ReplaceCall(original, wait, call =>
            new CodeInstruction[]
            {
                IlBridge.Inherit(call, new CodeInstruction(OpCodes.Ldarg_0)),
                new CodeInstruction(OpCodes.Ldfld, power),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GuardStancePowerGain), nameof(Wait)))
            }, site);
    }

    private static Task Wait(float fastSeconds, float standardSeconds, bool ignoreCombatEnd,
        CancellationToken cancellationToken, PowerModel power)
    {
        var grant = Current.Value;
        if (grant != null && power != null && power.Owner == grant.Target && power.GetType() == grant.PowerType &&
            fastSeconds == 0.1f && standardSeconds == 0.25f)
            return Task.CompletedTask;
        return Cmd.CustomScaledWait(fastSeconds, standardSeconds, ignoreCombatEnd, cancellationToken);
    }
}
