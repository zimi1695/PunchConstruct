namespace PunchConstruct;

// The per-hook fuse only covers an adapter's synchronous segment. These bridges
// hand __result an async task whose continuation runs after Guard returns; an
// exception there would otherwise escape into the native caller without fusing
// the hook. Wrap the continuation so a failure is recorded and the hook disabled.
// The exception is rethrown (never swallowed) and Task<T> values are never
// fabricated, so native error handling still sees the real failure.
internal static class AsyncGuard
{
    internal static Task Wrap(string adapter, string hook, Task task) =>
        AdaptationRegistry.Enabled(adapter, hook) ? Guarded(adapter, hook, task) : task;

    internal static Task<T> Wrap<T>(string adapter, string hook, Task<T> task) =>
        AdaptationRegistry.Enabled(adapter, hook) ? Guarded(adapter, hook, task) : task;

    private static async Task Guarded(string adapter, string hook, Task task)
    {
        try { await task; }
        catch (Exception error) { AdaptationRegistry.Fail(adapter, hook, error); throw; }
    }

    private static async Task<T> Guarded<T>(string adapter, string hook, Task<T> task)
    {
        try { return await task; }
        catch (Exception error) { AdaptationRegistry.Fail(adapter, hook, error); throw; }
    }
}