using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

// No fabricated victories, no selected-difficulty override and no global
// multiplayer progression unlock. Existing and fresh profiles use one path.
internal static class ConstructDifficulty
{
    internal static int Maximum => MaximumFor(GameMode.Standard);

    // Each mode caps ascension differently: Blitz only allows five levels and
    // maps them onto a fixed subset, while Standard and Dread allow ten. Using
    // the standard ten in Blitz would make AscensionManager reject the run.
    internal static int MaximumFor(GameMode gameMode) => AscensionManager.GetMaxAllowed(gameMode);

    internal static int LobbyMaximum(StartRunLobby lobby) => LobbyMaximum(lobby.Players, lobby.GameMode);

    internal static int LobbyMaximum(IEnumerable<StartRunLobbyPlayer> players, GameMode gameMode = GameMode.Standard) => players.Min(p =>
    {
        int native = gameMode == GameMode.Blitz ? p.maxBlitzAscension : p.maxStandardAscension;
        return CharacterScope.IsConstruct(p) ? Math.Max(native, MaximumFor(gameMode)) : native;
    });
}
