using System.Collections;
using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

// Native ENetHost.Update consumes transport disconnect events without removing
// the corresponding application peer. Reconcile terminal peers before native
// QoS runs, through the native removal/callback path, only in this role's runs.
[HarmonyPatch(typeof(ENetHost), nameof(ENetHost.Update))]
internal static class DisconnectedPeers
{
    private static readonly FieldInfo Connections = AccessTools.Field(typeof(ENetHost), "_connectedPeers");
    private static readonly FieldInfo Peer = AccessTools.Field(AccessTools.Inner(typeof(ENetHost), "ClientConnection"), "peer");
    private static readonly MethodInfo Handle = AccessTools.Method(typeof(ENetHost), "HandleClientDisconnection");
    private static void Postfix(ENetHost __instance)
    {
        var manager = RunManager.Instance;
        if (!__instance.IsConnected || manager.NetService is not INetHostGameService host ||
            !ReferenceEquals(host.NetHost, __instance) ||
            manager.DebugOnlyGetState()?.Players.Any(p => p.Character is ConstructCharacter) != true) return;
        foreach (var connection in ((IEnumerable)Connections.GetValue(__instance)!).Cast<object>().ToArray())
        {
            var peer = (ENetPacketPeer)Peer.GetValue(connection)!;
            if (peer.GetState() is not (ENetPacketPeer.PeerState.Disconnected or ENetPacketPeer.PeerState.Zombie)) continue;
            Diagnostics.Write("warn", "network.transport.peer_closed", new { reason = "terminal_enet_peer" });
            Handle.Invoke(__instance, [connection, NetError.UnknownNetworkError, true]);
        }
    }
}
