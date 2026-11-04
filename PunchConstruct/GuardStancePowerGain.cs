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
[HarmonyPatch]
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

    private static IEnumerable<MethodBase> TargetMethods()
    {
        // These two native commands contain the initial-application and stacking
        // pauses respectively. Resolve their state machines via metadata.
        foreach (var method in typeof(PowerCmd).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(m => !m.IsGenericMethod && m.Name is nameof(PowerCmd.Apply) or nameof(PowerCmd.ModifyAmount)))
        {
            var state = method.GetCustomAttribute<AsyncStateMachineAttribute>()?.StateMachineType;
            if (state != null) yield return AccessTools.Method(state, "MoveNext");
        }
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var wait = AccessTools.Method(typeof(Cmd), nameof(Cmd.CustomScaledWait));
        var power = AccessTools.Field(__originalMethod.DeclaringType, "power");
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(wait))
            {
                var load = new CodeInstruction(OpCodes.Ldarg_0);
                load.labels.AddRange(instruction.labels);
                load.blocks.AddRange(instruction.blocks);
                yield return load;
                yield return new CodeInstruction(OpCodes.Ldfld, power);
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GuardStancePowerGain), nameof(Wait)));
                replaced++;
            }
            else yield return instruction;
        }
        if (replaced != 1) throw new InvalidOperationException($"Unexpected native power wait count: {__originalMethod}: {replaced}");
    }

    private static Task Wait(float fastSeconds, float standardSeconds, bool ignoreCombatEnd,
        CancellationToken cancellationToken, PowerModel power)
    {
        var grant = Current.Value;
        if (grant != null && power.Owner == grant.Target && power.GetType() == grant.PowerType &&
            fastSeconds == 0.1f && standardSeconds == 0.25f)
            return Task.CompletedTask;
        return Cmd.CustomScaledWait(fastSeconds, standardSeconds, ignoreCombatEnd, cancellationToken);
    }
}
