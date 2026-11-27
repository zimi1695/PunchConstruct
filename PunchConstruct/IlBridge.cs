using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace PunchConstruct;

// The only IL rewrite entry. A call site that another mod already moved or
// removed is left untouched: native behavior is always the fallback. Require*
// validate operands at transpile time so a renamed field/method returns the
// original IL instead of emitting a null-operand instruction that only fails at
// JIT, deep inside a native method no Run/Guard can catch.
internal static class IlBridge
{
    private static int skippedCount;
    internal static int Skipped => skippedCount;

    internal static void Skip(string site, string reason)
    {
        skippedCount++;
        Diagnostics.Write("warn", "transpiler.skipped", new { site, reason });
    }

    // Resolve a field operand. Returns null (and logs) when missing or mistyped so
    // the caller returns the original IL. expectedType uses assignability so an
    // interface-typed slot accepts a concrete implementation.
    internal static FieldInfo? RequireField(Type owner, string name, Type expectedType, string site)
    {
        var field = AccessTools.Field(owner, name);
        if (field == null) { Skip(site, "field unresolved: " + owner.Name + "." + name); return null; }
        if (!expectedType.IsAssignableFrom(field.FieldType))
        {
            Skip(site, "field type changed: " + owner.Name + "." + name + " is " + field.FieldType.Name + ", expected " + expectedType.Name);
            return null;
        }
        return field;
    }

    internal static MethodInfo? RequireCall(Type owner, string name, string site, Type[]? args = null)
    {
        var method = args == null ? AccessTools.Method(owner, name) : AccessTools.Method(owner, name, args);
        if (method == null) Skip(site, "call unresolved: " + owner.Name + "." + name);
        return method;
    }

    internal static List<CodeInstruction> ReplaceCall(
        List<CodeInstruction> original,
        MethodInfo target,
        Func<CodeInstruction, CodeInstruction[]> build,
        string site,
        int expectedCount = 1)
    {
        var rewritten = new List<CodeInstruction>(original.Count);
        int replaced = 0;
        foreach (var instruction in original)
        {
            if (!instruction.Calls(target))
            {
                rewritten.Add(instruction);
                continue;
            }
            try { rewritten.AddRange(build(instruction)); }
            catch (Exception error)
            {
                // A malformed rewrite from this mod (or a call shape changed by
                // another mod) must never stop the native method from running.
                Skip(site, "rewrite failed: " + error);
                return original;
            }
            replaced++;
        }
        if (replaced == expectedCount) return rewritten;
        Skip(site, $"expected {expectedCount} call(s), replaced {replaced}");
        return original;
    }

    // Replace every matching call (weight/affinity hooks span several call sites).
    // Zero matches is a silent no-op upstream, so log it and return the original.
    internal static List<CodeInstruction> ReplaceAll(
        List<CodeInstruction> original,
        MethodInfo target,
        Func<CodeInstruction, CodeInstruction[]> build,
        string site)
    {
        var rewritten = new List<CodeInstruction>(original.Count);
        int replaced = 0;
        foreach (var instruction in original)
        {
            if (!instruction.Calls(target))
            {
                rewritten.Add(instruction);
                continue;
            }
            try { rewritten.AddRange(build(instruction)); }
            catch (Exception error)
            {
                Skip(site, "rewrite failed: " + error);
                return original;
            }
            replaced++;
        }
        if (replaced == 0) { Skip(site, "no call matched"); return original; }
        return rewritten;
    }

    // Predicate variant for hooks that match several overloads (e.g. every
    // Rng.NextItem<CardModel> call regardless of parameter shape).
    internal static List<CodeInstruction> ReplaceAll(
        List<CodeInstruction> original,
        Func<CodeInstruction, bool> match,
        Func<CodeInstruction, CodeInstruction[]> build,
        string site)
    {
        var rewritten = new List<CodeInstruction>(original.Count);
        int replaced = 0;
        foreach (var instruction in original)
        {
            if (!match(instruction))
            {
                rewritten.Add(instruction);
                continue;
            }
            try { rewritten.AddRange(build(instruction)); }
            catch (Exception error)
            {
                Skip(site, "rewrite failed: " + error);
                return original;
            }
            replaced++;
        }
        if (replaced == 0) { Skip(site, "no call matched"); return original; }
        return rewritten;
    }

    internal static CodeInstruction Inherit(CodeInstruction source, CodeInstruction replacement)
    {
        replacement.labels.AddRange(source.labels);
        replacement.blocks.AddRange(source.blocks);
        return replacement;
    }

    internal static List<CodeInstruction> Guard(string site, List<CodeInstruction> original, Func<List<CodeInstruction>> build)
    {
        try { return build(); }
        catch (Exception error)
        {
            Skip(site, "transpiler failed: " + error);
            return original;
        }
    }
}