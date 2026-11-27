using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Pooling;

// Linked into both QA harnesses. Call only once every checked gameplay/UI task
// has finished, immediately before process exit. Never used in normal gameplay.
// Every native lookup is soft: a renamed field/property degrades to 0 and logs,
// because this helper is compiled into the public DLL and must never throw from
// a shutdown path.
internal static class PoolShutdown
{
    private static bool warned;

    private static void Warn(string reason)
    {
        if (warned) return;
        warned = true;
        GD.Print("[QA POOL SHUTDOWN] unavailable: " + reason);
    }

    private static IDictionary? Pools()
    {
        var field = AccessTools.Field(typeof(NodePool), "_pools");
        if (field?.GetValue(null) is not IDictionary pools)
        {
            Warn("NodePool._pools unresolved");
            return null;
        }
        return pools;
    }

    private static IList? FreeObjects(object pool)
    {
        var property = pool.GetType().GetProperty("DebugFreeObjects");
        if (property?.GetValue(pool) is not IList free)
        {
            Warn(pool.GetType().Name + ".DebugFreeObjects unresolved");
            return null;
        }
        return free;
    }

    internal static int CardCount()
    {
        var pools = Pools();
        if (pools == null || !pools.Contains(typeof(NCard))) return 0;
        var pool = pools[typeof(NCard)];
        if (pool == null) return 0;
        var free = FreeObjects(pool);
        var field = AccessTools.Field(pool.GetType(), "_usedObjects");
        var used = field?.GetValue(pool);
        var count = used?.GetType().GetProperty("Count")?.GetValue(used);
        if (free == null || count is not int usedCount)
        {
            Warn(pool.GetType().Name + "._usedObjects unresolved");
            return 0;
        }
        return free.Count + usedCount;
    }

    internal static int ReleaseCards()
    {
        var pools = Pools();
        if (pools == null || !pools.Contains(typeof(NCard))) return 0;
        var pool = pools[typeof(NCard)];
        if (pool == null) return 0;
        var free = FreeObjects(pool);
        if (free == null) return 0;
        var nodes = free.Cast<Node>().ToArray();
        // A cached card must be outside the tree. Never free an active card.
        if (nodes.Any(n => GodotObject.IsInstanceValid(n) && (n.IsInsideTree() || n.GetParent() != null)))
            throw new InvalidOperationException("Cached card unexpectedly belongs to an active scene");
        free.Clear();
        foreach (var node in nodes)
            if (GodotObject.IsInstanceValid(node)) node.Free();
        if (nodes.Any(GodotObject.IsInstanceValid)) throw new InvalidOperationException("Idle card controls were not actually freed");
        GD.Print("[QA POOL SHUTDOWN] Freed idle native card controls: " + nodes.Length);
        return nodes.Length;
    }
}
