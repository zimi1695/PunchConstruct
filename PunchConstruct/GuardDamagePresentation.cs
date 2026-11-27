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
            .Single(m => m.Name == nameof(CreatureCmd.Damage) && m.GetParameters().Length == 8 &&
                m.GetCustomAttribute<AsyncStateMachineAttribute>() != null &&
                m.GetParameters()[1].ParameterType.IsGenericType &&
                m.GetParameters()[1].ParameterType.GetGenericTypeDefinition() == typeof(IEnumerable<>) &&
                m.GetParameters()[2].ParameterType == typeof(decimal));
        return AccessTools.Method(method.GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType, "MoveNext");
    }
    internal static List<CodeInstruction> Transpile(List<CodeInstruction> original, MethodBase __originalMethod)
    {
        const string site = "guard damage presentation";
        var wait = IlBridge.RequireCall(typeof(Cmd), nameof(Cmd.CustomScaledWait), site);
        if (wait == null) return original;
        int callIndex = original.FindIndex(i => i.Calls(wait));
        if (callIndex < 0) { IlBridge.Skip(site, "wait call not found"); return original; }
        // Anchor the returned results list by IL position + type, not by the
        // compiler-generated ordinal name. Two List<DamageResult> locals exist
        // (results + damageResults), so a by-type search alone is ambiguous; the
        // first ldfld of a List<DamageResult> after the wait is the returned list.
        FieldInfo? results = null;
        for (int i = callIndex + 1; i < original.Count; i++)
            if (original[i].opcode == OpCodes.Ldfld && original[i].operand is FieldInfo f && f.FieldType == typeof(List<DamageResult>))
            { results = f; break; }
        if (results == null) { IlBridge.Skip(site, "results field not found after wait"); return original; }
        var waitMethod = AccessTools.Method(typeof(GuardDamagePresentation), nameof(Wait));
        var rewritten = new List<CodeInstruction>(original.Count);
        for (int i = 0; i < original.Count; i++)
        {
            if (i != callIndex) { rewritten.Add(original[i]); continue; }
            rewritten.Add(IlBridge.Inherit(original[i], new CodeInstruction(OpCodes.Ldarg_0)));
            rewritten.Add(new CodeInstruction(OpCodes.Ldfld, results));
            rewritten.Add(new CodeInstruction(OpCodes.Call, waitMethod));
        }
        return rewritten;
    }
    private static Task Wait(float fast, float standard, bool ignoreCombatEnd, CancellationToken cancellation,
        List<DamageResult> results)
    {
        // OriginalTarget is the creature originally aimed at (== the old targetList
        // entry), so the targets field dependency is gone entirely. A shape change
        // upstream only makes this conservatively keep native timing, never bad IL.
        if (!NativeTimingForQa && fast == .1f && standard == .2f && results is { Count: 1 } &&
            results[0].WasFullyBlocked && results[0].OriginalTarget.Player is { Character: ConstructCharacter } player &&
            player.Relics.Any(r => r is GuardStance or AncientGuardStance))
        {
            Diagnostics.Write("info", "guard.damage.presentation", new { owner = player.NetId.ToString(), nativeMs = 200, compactMs = 50 });
            return Cmd.CustomScaledWait(.03f, .05f, ignoreCombatEnd, cancellation);
        }
        return Cmd.CustomScaledWait(fast, standard, ignoreCombatEnd, cancellation);
    }
}
