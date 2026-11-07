using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;

namespace PunchConstruct;

// The only IL rewrite entry. A call site that another mod already moved or
// removed is left untouched: native behavior is always the fallback.
internal static class IlBridge
{
    internal static List<CodeInstruction> ReplaceCall(
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
                // A malformed rewrite from this mod (or a call shape changed by
                // another mod) must never stop the native method from running.
                Diagnostics.Write("warn", "transpiler.rewrite_failed", new { site, error = error.ToString() });
                return original;
            }
            replaced++;
        }
        if (replaced == 1) return rewritten;
        Diagnostics.Write("warn", "transpiler.skipped", new { site, expected = 1, replaced });
        return original;
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
            Diagnostics.Write("warn", "transpiler.failed", new { site, error = error.ToString() });
            return original;
        }
    }
}
