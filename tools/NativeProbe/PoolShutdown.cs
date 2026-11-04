using System.Collections;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Pooling;

// Linked into both QA harnesses. Call only once every checked gameplay/UI task
// has finished, immediately before process exit. Never used in normal gameplay.
internal static class PoolShutdown
{
    internal static int CardCount()
    {
        var pools = (IDictionary)AccessTools.Field(typeof(NodePool), "_pools").GetValue(null)!;
        if (!pools.Contains(typeof(NCard))) return 0;
        var pool = pools[typeof(NCard)]!;
        var free = (IList)pool.GetType().GetProperty("DebugFreeObjects")!.GetValue(pool)!;
        var used = AccessTools.Field(pool.GetType(), "_usedObjects").GetValue(pool)!;
        return free.Count + (int)used.GetType().GetProperty("Count")!.GetValue(used)!;
    }

    internal static int ReleaseCards()
    {
        var pools = (IDictionary)AccessTools.Field(typeof(NodePool), "_pools").GetValue(null)!;
        if (!pools.Contains(typeof(NCard))) return 0;
        var pool = pools[typeof(NCard)]!;
        var free = (IList)pool.GetType().GetProperty("DebugFreeObjects")!.GetValue(pool)!;
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
