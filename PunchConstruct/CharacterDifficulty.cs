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
internal static class ConstructDifficulty
{
    internal static int Maximum => AscensionManager.maxAscensionAllowed;

    internal static int LobbyMaximum(IEnumerable<StartRunLobbyPlayer> players) => players.Min(p =>
        CharacterScope.IsConstruct(p) ? Math.Max(p.maxMultiplayerAscensionUnlocked, Maximum) : p.maxMultiplayerAscensionUnlocked);
}
