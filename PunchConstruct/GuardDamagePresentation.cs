using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;

namespace PunchConstruct;

// The native damage command waits again after every hit, after all reactions
// have already completed. Shorten only that presentation tail on a single
// fully-blocked hit that triggers this character's growing starter relic.
internal static class GuardDamagePresentation
{
    internal static bool NativeTimingForQa;
    internal static MethodBase Target()
    {
        var method = typeof(CreatureCmd).GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Single(m => m.Name == nameof(CreatureCmd.Damage) && m.GetParameters().Length == 7 &&
                m.GetParameters()[1].ParameterType == typeof(IEnumerable<Creature>) && m.GetParameters()[2].ParameterType == typeof(decimal));
        return AccessTools.Method(method.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType, "MoveNext");
    }
    internal static List<CodeInstruction> Transpile(List<CodeInstruction> original, MethodBase __originalMethod)
    {
        var wait = AccessTools.Method(typeof(Cmd), nameof(Cmd.CustomScaledWait));
        var targets = AccessTools.Field(__originalMethod.DeclaringType, "<targetList>5__3");
        var results = AccessTools.Field(__originalMethod.DeclaringType, "<results>5__2");
        return IlBridge.ReplaceCall(original, wait, call =>
            new CodeInstruction[]
            {
                IlBridge.Inherit(call, new CodeInstruction(OpCodes.Ldarg_0)),
                new CodeInstruction(OpCodes.Ldfld, targets),
                new CodeInstruction(OpCodes.Ldarg_0),
                new CodeInstruction(OpCodes.Ldfld, results),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(GuardDamagePresentation), nameof(Wait)))
            },
            "guard damage presentation");
    }
    private static Task Wait(float fast, float standard, bool ignoreCombatEnd, CancellationToken cancellation,
        List<Creature> targets, List<DamageResult> results)
    {
        if (!NativeTimingForQa && fast == .1f && standard == .2f && targets.Count == 1 && results.Count == 1 &&
            results[0].WasFullyBlocked && targets[0].Player is { Character: ConstructCharacter } player &&
            player.Relics.Any(r => r is GuardStance or AncientGuardStance))
        {
            Diagnostics.Write("info", "guard.damage.presentation", new { owner = player.NetId.ToString(), nativeMs = 200, compactMs = 50 });
            return Cmd.CustomScaledWait(.03f, .05f, ignoreCombatEnd, cancellation);
        }
        return Cmd.CustomScaledWait(fast, standard, ignoreCombatEnd, cancellation);
    }
}
