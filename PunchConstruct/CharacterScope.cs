using System;
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
    // RelicModel.Owner calls AssertMutable and throws for reward/shop previews,
    // so read the backing field directly. A future game update that renames the
    // field must degrade to the preview-owner fallback instead of breaking the
    // static initializer for the whole mod.
    private static readonly AccessTools.FieldRef<RelicModel, Player?>? RelicOwner = CreateRelicOwner();

    private static AccessTools.FieldRef<RelicModel, Player?>? CreateRelicOwner()
    {
        try
        {
            return AccessTools.FieldRefAccess<RelicModel, Player?>("_owner");
        }
        catch (Exception error)
        {
            Diagnostics.Write("warn", "character_scope.owner_accessor", new
            {
                error = error.ToString()
            });
            return null;
        }
    }

    private static Player? Owner(RelicModel relic) => RelicOwner is null ? null : RelicOwner(relic);

    internal static bool IsConstruct(CharacterModel? character) => character is ConstructCharacter;
    internal static bool IsConstruct(Player? player) => IsConstruct(player?.Character);
    internal static bool IsConstruct(Creature? creature) => player(creature) is { } owner && IsConstruct(owner);
    internal static bool IsConstruct(RelicModel? relic) => relic != null &&
        (Owner(relic) ?? RelicPreviewContext.Owner(relic)) is { } owner && IsConstruct(owner);
    // A relic shown in a reward, shop or chest preview has no live owner yet.
    // When the backing field is unavailable, ownership is unknown rather than
    // "unowned", so callers keep their conservative native path.
    internal static bool IsUnowned(RelicModel relic) => RelicOwner is not null && Owner(relic) is null;
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
