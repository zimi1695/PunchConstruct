using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

// The only ownership guard in the mod. Every hook, adapter and QA helper asks
// this class instead of testing the concrete character type itself.
internal static class CharacterScope
{
    private static readonly AccessTools.FieldRef<RelicModel, Player?> RelicOwner =
        AccessTools.FieldRefAccess<RelicModel, Player?>("_owner");

    internal static bool IsConstruct(CharacterModel? character) => character is ConstructCharacter;
    internal static bool IsConstruct(Player? player) => IsConstruct(player?.Character);
    internal static bool IsConstruct(Creature? creature) => player(creature) is { } owner && IsConstruct(owner);
    internal static bool IsConstruct(RelicModel? relic) => relic != null &&
        (RelicOwner(relic) ?? RelicPreviewContext.Owner(relic)) is { } owner && IsConstruct(owner);
    internal static bool IsConstruct(CardModel? card) => card?.Owner is { } owner && IsConstruct(owner);
    internal static bool IsConstruct(CardPoolModel? pool) => pool is ConstructCardPool;
    internal static bool IsConstruct(EventModel? eventModel) => eventModel?.Owner is { } owner && IsConstruct(owner);
    internal static bool IsConstruct(StartRunLobbyPlayer? player) => IsConstruct(player?.character);
    internal static bool IsConstruct(IPlayerCollection? players) => AnyConstruct(players?.Players);
    internal static bool AnyConstruct(IEnumerable<Player>? players) => players?.Any(IsConstruct) == true;
    // ModelId/string comparisons happen while patches receive ids before a model
    // instance exists; keep the identity rule here as well.
    internal static bool IsConstructId(ModelId? id) => id == ModelDb.GetId<ConstructCharacter>();
    internal static bool IsConstructId(string? id) => id == ModelDb.GetId<ConstructCharacter>().Entry;
    internal static bool IsWave(CardModel? card) => card is SmallWave or TonyWave or IronWave or ScorchingIronWave;

    private static Player? player(Creature? creature) => creature?.Player;
}
