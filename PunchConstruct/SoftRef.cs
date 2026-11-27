using System.Reflection;
using HarmonyLib;

namespace PunchConstruct;

// Single entry for soft reflection. Every descriptor resolves lazily on first
// use — never inside a static initializer — so a renamed native member degrades
// to null plus one diagnostic instead of poisoning the whole type with
// TypeInitializationException. CompatibilityPrecheck probes the registry at
// startup; the check count is derived from the registry, never hardcoded.
internal static class SoftRef
{
    private static readonly List<Descriptor> Registry = [];
    private static readonly object Gate = new();
    private static int unresolved;

    internal sealed class Descriptor(string name, Func<object?> resolve)
    {
        private object? _value;
        private bool _resolved;
        internal string Name => name;
        internal object? Value
        {
            get
            {
                if (_resolved) return _value;
                _resolved = true;
                try { _value = resolve(); }
                catch (Exception error)
                {
                    _value = null;
                    Diagnostics.Write("warn", "compat.reflection_missing", new { name, error = error.ToString() });
                }
                if (_value == null)
                {
                    Interlocked.Increment(ref unresolved);
                    Diagnostics.Write("warn", "compat.reflection_missing", new { name });
                }
                return _value;
            }
        }
    }

    // Lazy holder: constructing it only stores a resolver lambda, so a static
    // readonly field of this type is safe even when the native member is gone.
    internal sealed class Ref<T> where T : class
    {
        private readonly Descriptor _descriptor;
        internal Ref(string name, Func<T?> resolve) => _descriptor = RegisterDescriptor(name, () => resolve());
        internal T? Value => (T?)_descriptor.Value;
    }

    private static Descriptor RegisterDescriptor(string name, Func<object?> resolve)
    {
        var descriptor = new Descriptor(name, resolve);
        lock (Gate) Registry.Add(descriptor);
        return descriptor;
    }

    internal static Ref<T> Register<T>(string name, Func<T?> resolve) where T : class => new(name, resolve);

    internal static Ref<AccessTools.FieldRef<TOwner, TValue>> FieldRef<TOwner, TValue>(string name, string field) where TOwner : class
        => new(name, () => AccessTools.FieldRefAccess<TOwner, TValue>(field));

    internal static Ref<MethodInfo> Method(string name, Type owner, string method, Type[]? args = null)
        => new(name, () => args == null ? AccessTools.Method(owner, method) : AccessTools.Method(owner, method, args));

    internal static Ref<FieldInfo> Field(string name, Type owner, string field)
        => new(name, () => AccessTools.Field(owner, field));

    // Probe every registered descriptor once; returns the unresolved names.
    internal static IReadOnlyList<string> ProbeAll()
    {
        List<Descriptor> snapshot;
        lock (Gate) snapshot = [.. Registry];
        var missing = new List<string>();
        foreach (var descriptor in snapshot)
            if (descriptor.Value == null) missing.Add(descriptor.Name);
        return missing;
    }

    internal static int Count
    {
        get { lock (Gate) return Registry.Count; }
    }
    internal static int Unresolved => unresolved;
}