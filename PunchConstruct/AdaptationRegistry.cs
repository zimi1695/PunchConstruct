using System;
using System.Collections.Generic;

namespace PunchConstruct;

internal enum AdapterPolicy
{
    Required,
    Optional
}

// The only place that decides whether an adapter may run and what happens when
// it fails. A failing hook disables itself for the rest of the process so a
// third-party conflict cannot throw on every turn or reward roll. Sibling hooks
// of the same adapter keep running, so one broken reward hook cannot take the
// whole reward domain offline.
internal static class AdaptationRegistry
{
    private static readonly Dictionary<string, AdapterPolicy> Policies = new(StringComparer.Ordinal)
    {
        ["reward"] = AdapterPolicy.Required,
        ["relic"] = AdapterPolicy.Required,
        ["event"] = AdapterPolicy.Required,
        ["combat"] = AdapterPolicy.Required,
        ["presentation"] = AdapterPolicy.Optional
    };

    private const char Separator = '\u001f';
    private static readonly HashSet<string> Disabled = new(StringComparer.Ordinal);

    private static string Key(string adapter, string hook) => adapter + Separator + hook;

    // True when no hook of this adapter has been disabled.
    internal static bool Enabled(string adapter) => !Disabled.Any(key =>
        key.StartsWith(adapter + Separator, StringComparison.Ordinal));
    internal static bool Enabled(string adapter, string hook) => !Disabled.Contains(Key(adapter, hook));
    internal static IReadOnlyCollection<string> DisabledHooks => Disabled;
    internal static AdapterPolicy Policy(string adapter) => Policies.GetValueOrDefault(adapter, AdapterPolicy.Optional);

    internal static void Run(string adapter, string hook, Action action)
    {
        if (!Enabled(adapter, hook)) return;
        try { action(); }
        catch (Exception error) { Fail(adapter, hook, error); }
    }

    internal static T Run<T>(string adapter, string hook, T fallback, Func<T> action)
    {
        if (!Enabled(adapter, hook)) return fallback;
        try { return action(); }
        catch (Exception error) { Fail(adapter, hook, error); return fallback; }
    }

    // Prefix guard: true means "let the native method run". Any adapter error
    // falls back to the native behavior instead of interrupting the game.
    internal static bool Guard(string adapter, string hook, Func<bool> action, bool fallback = true)
    {
        if (!Enabled(adapter, hook)) return fallback;
        try { return action(); }
        catch (Exception error) { Fail(adapter, hook, error); return fallback; }
    }

    internal static void Fail(string adapter, string hook, Exception error)
    {
        Disabled.Add(Key(adapter, hook));
        var policy = Policy(adapter);
        Diagnostics.Write(policy == AdapterPolicy.Required ? "error" : "warn", "adapter.failed", new
        {
            adapter,
            hook,
            policy = policy.ToString(),
            disabled = hook
        });
    }
}
