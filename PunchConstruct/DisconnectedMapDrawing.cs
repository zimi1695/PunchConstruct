using System.Reflection.Emit;
using HarmonyLib;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

// Native map teardown updates the local cursor, then tries to broadcast that
// cosmetic mode after the client has disconnected. Keep all native UI cleanup;
// only omit this one packet when the map belongs to a construct-containing run.
[HarmonyPatch(typeof(NMapDrawings), nameof(NMapDrawings.SetDrawingModeLocal))]
internal static class DisconnectedMapDrawing
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var send = typeof(INetGameService).GetMethods()
            .Single(m => m.Name == nameof(INetGameService.SendMessage) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .MakeGenericMethod(typeof(MapDrawingModeChangedMessage));
        int replaced = 0;
        foreach (var instruction in instructions)
        {
            if (!instruction.Calls(send)) { yield return instruction; continue; }
            var load = new CodeInstruction(OpCodes.Ldarg_0);
            load.labels.AddRange(instruction.labels); load.blocks.AddRange(instruction.blocks);
            yield return load;
            yield return new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(NMapDrawings), "_playerCollection"));
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DisconnectedMapDrawing), nameof(Send)));
            replaced++;
        }
        if (replaced != 1) throw new InvalidOperationException("Unexpected native map mode send count: " + replaced);
    }

    private static void Send(INetGameService net, MapDrawingModeChangedMessage message, IPlayerCollection players)
    {
        if (!net.IsConnected && players.Players.Any(p => p.Character is ConstructCharacter))
        {
            Diagnostics.Write("info", "network.map_mode.local_only", new { mode = message.drawingMode.ToString(), reason = "disconnected" });
            return;
        }
        net.SendMessage(message);
    }
}
