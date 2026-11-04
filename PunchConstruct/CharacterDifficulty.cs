using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Saves;

namespace PunchConstruct;

// No fabricated victories, no selected-difficulty override and no global
// multiplayer progression unlock. Existing and fresh profiles use one path.
[HarmonyPatch(typeof(ProgressState), nameof(ProgressState.GetOrCreateCharacterStats))]
internal static class ConstructDifficulty
{
    internal static int Maximum => AscensionManager.maxAscensionAllowed;

    private static void Postfix(ModelId __0, CharacterStats __result)
    {
        if (__0 == ModelDb.GetId<ConstructCharacter>())
            __result.MaxAscension = Math.Max(__result.MaxAscension, Maximum);
    }

    internal static int LobbyMaximum(IEnumerable<StartRunLobbyPlayer> players) => players.Min(p =>
        p.character is ConstructCharacter ? Math.Max(p.maxMultiplayerAscensionUnlocked, Maximum) : p.maxMultiplayerAscensionUnlocked);
}

[HarmonyPatch(typeof(StartRunLobby), "SetSingleplayerAscensionAfterCharacterChanged")]
internal static class ConstructSingleplayerDifficulty
{
    private static void Prefix(ModelId __0)
    {
        if (__0 == ModelDb.GetId<ConstructCharacter>())
        {
            var stats = SaveManager.Instance.Progress.GetOrCreateCharacterStats(__0);
            stats.MaxAscension = Math.Max(stats.MaxAscension, ConstructDifficulty.Maximum);
        }
    }
}

[HarmonyPatch(typeof(StartRunLobby), "UpdateMaxMultiplayerAscension")]
internal static class ConstructMultiplayerDifficulty
{
    private static bool Prefix(StartRunLobby __instance)
    {
        if (!__instance.Players.Any(p => p.character is ConstructCharacter)) return true;
        int maximum = ConstructDifficulty.LobbyMaximum(__instance.Players);
        if (maximum != __instance.MaxAscension)
        {
            AccessTools.PropertySetter(typeof(StartRunLobby), nameof(StartRunLobby.MaxAscension)).Invoke(__instance, [maximum]);
            __instance.LobbyListener.MaxAscensionChanged();
            if (__instance.Ascension > maximum && __instance.NetService.Type == NetGameType.Host)
                __instance.SyncAscensionChange(maximum);
        }
        return false;
    }
}

// Native multiplayer progression is account-wide, so changing characters does
// not normally recompute it. Our character-specific availability must do so,
// including restoring native limits after the last Construct switches away.
[HarmonyPatch(typeof(StartRunLobby), "ChangeCharacter")]
internal static class ConstructDifficultyCharacterChange
{
    private static void Postfix(StartRunLobby __instance)
    {
        if (__instance.NetService.Type is NetGameType.Host or NetGameType.Client)
            AccessTools.Method(typeof(StartRunLobby), "UpdateMaxMultiplayerAscension").Invoke(__instance, []);
    }
}
