using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

// Presentation owns every visual/audio/lobby hook. Native behavior runs first
// or remains untouched; each method only replaces its own cosmetic decision.
internal static class PresentationAdapter
{
    internal static void CharacterPortrait(NCharacterSelectButton button, CharacterModel character)
    {
        if (!CharacterScope.IsConstruct(character)) return;
        var portrait = ResourceLoader.Load<Texture2D>("res://PunchConstruct/art/character_select_icon.ctex");
        foreach (var field in new[] { "_icon", "_iconAdd" })
        {
            var icon = (TextureRect)AccessTools.Field(typeof(NCharacterSelectButton), field).GetValue(button)!;
            icon.Texture = portrait;
            icon.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
            icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            icon.StretchMode = TextureRect.StretchModeEnum.Scale;
        }
    }

    internal static bool SelectionCue(string sfx, float volume)
    {
        if (sfx != CharacterSelectAudio.Cue) return true;
        CharacterSelectAudio.Play(volume);
        return false;
    }

    internal static bool SelectionAudioOutput(string path, float volume)
    {
        if (path != CharacterSelectAudio.Cue) return true;
        CharacterSelectAudio.Play(volume);
        return false;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    internal static void SelectionUiPlay(string sfx, float volume)
    {
        if (sfx == CharacterSelectAudio.Cue) CharacterSelectAudio.Play(volume);
        else SfxCmd.Play(sfx, volume);
    }

    internal static List<CodeInstruction> SelectionUiTranspiler(List<CodeInstruction> original)
    {
        var native = AccessTools.Method(typeof(SfxCmd), nameof(SfxCmd.Play), [typeof(string), typeof(float)]);
        var replacement = AccessTools.Method(typeof(PresentationAdapter), nameof(SelectionUiPlay));
        foreach (var instruction in original)
            if (instruction.Calls(native))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
            }
        return original;
    }

    internal static IEnumerable<MethodBase> SelectionTargets()
    {
        yield return AccessTools.Method(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter));
        yield return AccessTools.Method(typeof(NCharacterSelectScreen), "OnLocalCharacterChangedForRandom");
        yield return AccessTools.Method(typeof(NCustomRunScreen), nameof(NCustomRunScreen.SelectCharacter));
    }

    internal static List<CodeInstruction> FakeMerchantAnimation(List<CodeInstruction> original)
    {
        var rewritten = new List<CodeInstruction>(original.Count);
        foreach (var instruction in original)
        {
            rewritten.Add(instruction);
            if (instruction.opcode == OpCodes.Ldstr && Equals(instruction.operand, "relaxed_loop"))
            {
                rewritten.Add(new CodeInstruction(OpCodes.Ldarg_1));
                rewritten.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PresentationAdapter), nameof(FakeMerchantAnimationName))));
            }
        }
        return rewritten;
    }

    private static string FakeMerchantAnimationName(string original, NCreatureVisuals visuals) =>
        visuals.HasMeta("punch_construct") ? "idle_loop" : original;

    internal static void MerchantAnimationPrefix(NMerchantCharacter character, ref string trigger)
    {
        if (character.HasMeta("punch_construct")) trigger = "idle_loop";
    }

    internal static List<CodeInstruction> CampAnimation(List<CodeInstruction> original)
    {
        var rewritten = new List<CodeInstruction>(original.Count);
        foreach (var instruction in original)
        {
            rewritten.Add(instruction);
            if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string name &&
                name is "overgrowth_loop" or "hive_loop" or "glory_loop")
            {
                rewritten.Add(new CodeInstruction(OpCodes.Ldarg_0));
                rewritten.Add(new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PresentationAdapter), nameof(CampAnimationName))));
            }
        }
        return rewritten;
    }

    private static string CampAnimationName(string original, NRestSiteCharacter character) =>
        character.HasMeta("punch_construct") ? "idle_loop" : original;

    internal static void AttackPresentation(AttackCommand command, int hitCount, ref string? attackerSfx, ref string? tmpAttackerSfx)
    {
        if (command.ModelSource is not CardModel card || !CharacterScope.IsConstruct(card.Owner) ||
            command.Attacker != card.Owner.Creature) return;
        attackerSfx = null;
        tmpAttackerSfx = null;
        if (card.Type != CardType.Attack || !ConstructPresentation.ClaimAttack(command.CardPlay))
        {
            command.WithNoAttackerAnim();
            return;
        }
        string trigger = ConstructPresentation.NextAttack(command.Attacker, hitCount);
        command.WithAttackerAnim(trigger, trigger == "DoubleAttack" ? 0.2f : 0.25f).OnlyPlayAnimOnce();
        Diagnostics.Observe(() => Diagnostics.Write("info", "character.animation.attack", new
        {
            owner = Diagnostics.Slot(card.Owner.NetId),
            card = card.Id.ToString(),
            hits = hitCount,
            trigger
        }));
    }

    // Runs before the public OnPlayWrapper stub (not the async state machine),
    // so a third-party rewrite of CardModel.OnPlayWrapper.MoveNext cannot break
    // it. Skill and power cards get the shared cast pose and character SFX.
    internal static void CardPlayCast(CardModel card)
    {
        if (card.Type is not (CardType.Skill or CardType.Power) || !CharacterScope.IsConstruct(card)) return;
        if (card.Owner?.Creature is not { } creature) return;
        if (creature.IsDead || creature.GetCreatureNode() is not { } node) return;
        SfxCmd.Play(ConstructPresentation.GuardSfx);
        node.SetAnimationTrigger("Cast");
    }

    internal static bool AnimationSound(Creature creature, ref string trigger, ref Task result)
    {
        if (!CharacterScope.IsConstruct(creature)) return true;
        // The wrapper prefix owns every construct skill/power cast, so native
        // cards that also call TriggerAnim("Cast"/"PowerUp") must not play a
        // second pose or sound.
        if (trigger is "Cast" or "PowerUp")
        {
            result = Task.CompletedTask;
            return false;
        }
        if (trigger == "DoubleAttack" && !creature.IsDead && creature.GetCreatureNode() != null)
            SfxCmd.Play(ConstructPresentation.DoubleSfx);
        return true;
    }

    internal static void TransformReveal(NCard card, CardModel replacement) => HandTransformAudio.Reveal(card, replacement);

    internal static List<CodeInstruction> MapDrawingTranspiler(List<CodeInstruction> original)
    {
        var send = typeof(INetGameService).GetMethods()
            .Single(m => m.Name == nameof(INetGameService.SendMessage) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1)
            .MakeGenericMethod(typeof(MapDrawingModeChangedMessage));
        return IlBridge.ReplaceCall(original, send, call =>
            new CodeInstruction[]
            {
                IlBridge.Inherit(call, new CodeInstruction(OpCodes.Ldarg_0)),
                new CodeInstruction(OpCodes.Ldfld, AccessTools.Field(typeof(NMapDrawings), "_playerCollection")),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PresentationAdapter), nameof(MapDrawingSend)))
            },
            "disconnected map drawing");
    }

    private static void MapDrawingSend(INetGameService net, MapDrawingModeChangedMessage message, IPlayerCollection players)
    {
        if (!net.IsConnected && CharacterScope.AnyConstruct(players.Players))
        {
            Diagnostics.Write("info", "network.map_mode.local_only",
                new { mode = message.drawingMode.ToString(), reason = "disconnected" });
            return;
        }
        net.SendMessage(message);
    }

    private static readonly FieldInfo Connections = AccessTools.Field(typeof(ENetHost), "_connectedPeers");
    private static readonly FieldInfo Peer = AccessTools.Field(AccessTools.Inner(typeof(ENetHost), "ClientConnection"), "peer");
    private static readonly MethodInfo HandleDisconnect = AccessTools.Method(typeof(ENetHost), "HandleClientDisconnection");

    internal static void DisconnectedPeers(ENetHost host)
    {
        var manager = RunManager.Instance;
        if (!host.IsConnected || manager.NetService is not INetHostGameService service ||
            !ReferenceEquals(service.NetHost, host) ||
            !CharacterScope.AnyConstruct(manager.DebugOnlyGetState()?.Players)) return;
        foreach (var connection in ((System.Collections.IEnumerable)Connections.GetValue(host)!).Cast<object>().ToArray())
        {
            var peer = (ENetPacketPeer)Peer.GetValue(connection)!;
            if (peer.GetState() is not (ENetPacketPeer.PeerState.Disconnected or ENetPacketPeer.PeerState.Zombie)) continue;
            Diagnostics.Write("warn", "network.transport.peer_closed", new { reason = "terminal_enet_peer" });
            HandleDisconnect.Invoke(host, [connection, NetError.UnknownNetworkError, true]);
        }
    }

    internal static string VisualsPath(MonsterModel monster, string original)
    {
        if (monster is not DecimillipedeSegmentFront and not DecimillipedeSegmentMiddle and not DecimillipedeSegmentBack) return original;
        if (!GraphicsCompatibility.Applies(RenderingServer.GetCurrentRenderingMethod() == "gl_compatibility",
                CharacterScope.AnyConstruct(RunManager.Instance.DebugOnlyGetState()?.Players))) return original;
        return "res://PunchConstruct/native_compat/" + System.IO.Path.GetFileName(original);
    }

    internal static string IronWavePortrait(CardModel card, string original) =>
        card is IronWave && RegentAdaptations.Owns(card) ? "res://PunchConstruct/art/iron_wave.ctex" : original;

    internal static void RelicIcon(RelicModel relic, MethodBase originalMethod, ref Texture2D? result)
    {
        if (result != null) result = RelicIcons.Adapt(relic, result, originalMethod.Name == "get_IconOutline");
    }

    private static readonly AccessTools.FieldRef<NRelic, NRelic.IconSize> IconSize =
        AccessTools.FieldRefAccess<NRelic, NRelic.IconSize>("_iconSize");
    private static readonly AccessTools.FieldRef<NRelic, RelicModel?> IconModel =
        AccessTools.FieldRefAccess<NRelic, RelicModel?>("_model");
    private static readonly MethodInfo PackedOutline = AccessTools.PropertyGetter(typeof(RelicModel), "PackedIconOutlinePath");

    internal static void RelicControlReload(NRelic control)
    {
        if (!control.IsNodeReady() || control.Icon == null || IconSize(control) != NRelic.IconSize.Small) return;
        var relic = IconModel(control);
        if (relic is not (RippleBasin or ConstructRippleBasin or ConstructDart or HelicalDart or ConstructHelmet or RuinedHelmet)) return;
        var icon = ResourceLoader.Load<Texture2D>(relic.PackedIconPath);
        control.Icon.Texture = RelicIcons.Adapt(relic, icon);
        var outline = ResourceLoader.Load<Texture2D>((string)PackedOutline.Invoke(relic, null)!);
        control.Outline.Texture = RelicIcons.Adapt(relic, outline, outline: true);
    }

    private static readonly AccessTools.FieldRef<MerchantEntry, MegaCrit.Sts2.Core.Entities.Players.Player> EntryPlayer =
        AccessTools.FieldRefAccess<MerchantEntry, MegaCrit.Sts2.Core.Entities.Players.Player>("_player");

    internal static void MerchantRelicPreview(MerchantRelicEntry entry) =>
        RelicPreviewContext.Register(entry.Model, EntryPlayer(entry));

    internal static void RewardRelicPreview(RelicReward reward) =>
        RelicPreviewContext.Register(reward.Relic, reward.Player);

    internal static List<CodeInstruction> GuardDamageTranspiler(List<CodeInstruction> original, MethodBase originalMethod) =>
        GuardDamagePresentation.Transpile(original, originalMethod);

    internal static bool WaveFacingRedirect(PlayerChoiceContext context, IEnumerable<Creature> targets, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource, CardPlay? cardPlay, ref Task<IEnumerable<DamageResult>> result) =>
        DelayedWaveFacing.Redirect(context, targets, amount, props, dealer, cardSource, cardPlay, ref result);

    internal static void DifficultyStats(ModelId id, CharacterStats stats)
    {
        if (CharacterScope.IsConstructId(id))
            stats.MaxAscension = Math.Max(stats.MaxAscension, ConstructDifficulty.Maximum);
    }

    internal static void DifficultySingleplayer(ModelId id)
    {
        if (!CharacterScope.IsConstructId(id)) return;
        var stats = SaveManager.Instance.Progress.GetOrCreateCharacterStats(id);
        stats.MaxAscension = Math.Max(stats.MaxAscension, ConstructDifficulty.Maximum);
    }

    internal static bool DifficultyMultiplayer(StartRunLobby lobby)
    {
        if (!lobby.Players.Any(p => CharacterScope.IsConstruct(p))) return true;
        int maximum = ConstructDifficulty.LobbyMaximum(lobby.Players);
        if (maximum != lobby.MaxAscension)
        {
            AccessTools.PropertySetter(typeof(StartRunLobby), nameof(StartRunLobby.MaxAscension)).Invoke(lobby, [maximum]);
            lobby.LobbyListener.MaxAscensionChanged();
            if (lobby.Ascension > maximum && lobby.NetService.Type == NetGameType.Host)
                lobby.SyncAscensionChange(maximum);
        }
        return false;
    }

    internal static void DifficultyChanged(StartRunLobby lobby)
    {
        if (lobby.NetService.Type is NetGameType.Host or NetGameType.Client)
            AccessTools.Method(typeof(StartRunLobby), "UpdateMaxMultiplayerAscension").Invoke(lobby, []);
    }
}
