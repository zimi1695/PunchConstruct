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
    private static readonly SoftRef.Ref<FieldInfo> IconField =
        SoftRef.Field("NCharacterSelectButton._icon", typeof(NCharacterSelectButton), "_icon");
    private static readonly SoftRef.Ref<FieldInfo> IconAddField =
        SoftRef.Field("NCharacterSelectButton._iconAdd", typeof(NCharacterSelectButton), "_iconAdd");

    internal static void CharacterPortrait(NCharacterSelectButton button, CharacterModel character)
    {
        if (!CharacterScope.IsConstruct(character)) return;
        var portrait = ResourceLoader.Load<Texture2D>("res://PunchConstruct/art/character_select_icon.ctex");
        foreach (var field in new[] { IconField.Value, IconAddField.Value })
        {
            if (field?.GetValue(button) is not TextureRect icon) continue;
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
        yield return AccessTools.Method(typeof(NCharacterSelectScreen), "DoRandomCharacterChange");
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
        const string site = "disconnected map drawing";
        var sendDef = typeof(INetGameService).GetMethods()
            .FirstOrDefault(m => m.Name == nameof(INetGameService.SendMessage) && m.IsGenericMethodDefinition && m.GetParameters().Length == 1);
        if (sendDef == null) { IlBridge.Skip(site, "SendMessage generic definition not found"); return original; }
        var send = sendDef.MakeGenericMethod(typeof(MapDrawingModeChangedMessage));
        var players = IlBridge.RequireField(typeof(NMapDrawings), "_playerCollection", typeof(IPlayerCollection), site);
        if (players == null) return original;
        return IlBridge.ReplaceCall(original, send, call =>
            new CodeInstruction[]
            {
                IlBridge.Inherit(call, new CodeInstruction(OpCodes.Ldarg_0)),
                new CodeInstruction(OpCodes.Ldfld, players),
                new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(PresentationAdapter), nameof(MapDrawingSend)))
            }, site);
    }

    private static void MapDrawingSend(INetGameService net, MapDrawingModeChangedMessage message, IPlayerCollection players)
    {
        // The injected field can be null before Initialize or after teardown; fall
        // back to the native send rather than dereferencing null inside the method.
        if (players != null && !net.IsConnected && CharacterScope.AnyConstruct(players.Players))
        {
            Diagnostics.Write("info", "network.map_mode.local_only",
                new { mode = message.drawingMode.ToString(), reason = "disconnected" });
            return;
        }
        net.SendMessage(message);
    }

    private static readonly SoftRef.Ref<FieldInfo> Connections =
        SoftRef.Register<FieldInfo>("ENetHost._connectedPeers", () => AccessTools.Field(typeof(ENetHost), "_connectedPeers"));
    private static readonly SoftRef.Ref<FieldInfo> Peer =
        SoftRef.Register<FieldInfo>("ENetHost.ClientConnection.peer", () =>
            AccessTools.Inner(typeof(ENetHost), "ClientConnection") is { } connection ? AccessTools.Field(connection, "peer") : null);
    private static readonly SoftRef.Ref<MethodInfo> HandleDisconnect =
        SoftRef.Register<MethodInfo>("ENetHost.HandleClientDisconnection", () => AccessTools.Method(typeof(ENetHost), "HandleClientDisconnection"));

    internal static void DisconnectedPeers(ENetHost host)
    {
        var connections = Connections.Value;
        var peerField = Peer.Value;
        var handleDisconnect = HandleDisconnect.Value;
        if (connections == null || peerField == null || handleDisconnect == null) return;
        var manager = RunManager.Instance;
        if (manager == null || !host.IsConnected || manager.NetService is not INetHostGameService service ||
            !ReferenceEquals(service.NetHost, host) ||
            !CharacterScope.AnyConstruct(manager.DebugOnlyGetState()?.Players)) return;
        if (connections.GetValue(host) is not System.Collections.IEnumerable peers) return;
        foreach (var connection in peers.Cast<object>().ToArray())
        {
            if (peerField.GetValue(connection) is not ENetPacketPeer peer) continue;
            if (peer.GetState() is not (ENetPacketPeer.PeerState.Disconnected or ENetPacketPeer.PeerState.Zombie)) continue;
            Diagnostics.Write("warn", "network.transport.peer_closed", new { reason = "terminal_enet_peer" });
            try { handleDisconnect.Invoke(host, [connection, NetError.UnknownNetworkError, true]); }
            catch (Exception error)
            {
                Diagnostics.Write("warn", "network.transport.disconnect_failed", new { error = error.ToString() });
            }
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

    private static readonly SoftRef.Ref<AccessTools.FieldRef<NRelic, NRelic.IconSize>> IconSize =
        SoftRef.FieldRef<NRelic, NRelic.IconSize>("NRelic._iconSize", "_iconSize");
    private static readonly SoftRef.Ref<AccessTools.FieldRef<NRelic, RelicModel?>> IconModel =
        SoftRef.FieldRef<NRelic, RelicModel?>("NRelic._model", "_model");
    private static readonly SoftRef.Ref<MethodInfo> PackedOutline =
        SoftRef.Register<MethodInfo>("RelicModel.PackedIconOutlinePath", () => AccessTools.PropertyGetter(typeof(RelicModel), "PackedIconOutlinePath"));

    internal static void RelicControlReload(NRelic control)
    {
        var iconSize = IconSize.Value;
        var iconModel = IconModel.Value;
        var packedOutline = PackedOutline.Value;
        if (iconSize == null || iconModel == null || packedOutline == null) return;
        if (!control.IsNodeReady() || control.Icon == null || iconSize(control) != NRelic.IconSize.Small) return;
        var relic = iconModel(control);
        if (relic is not (RippleBasin or ConstructRippleBasin or ConstructDart or HelicalDart or ConstructHelmet or RuinedHelmet)) return;
        var icon = ResourceLoader.Load<Texture2D>(relic.PackedIconPath);
        control.Icon.Texture = RelicIcons.Adapt(relic, icon);
        var outline = ResourceLoader.Load<Texture2D>((string)packedOutline.Invoke(relic, null)!);
        control.Outline.Texture = RelicIcons.Adapt(relic, outline, outline: true);
    }

    private static readonly SoftRef.Ref<AccessTools.FieldRef<MerchantEntry, MegaCrit.Sts2.Core.Entities.Players.Player>> EntryPlayer =
        SoftRef.FieldRef<MerchantEntry, MegaCrit.Sts2.Core.Entities.Players.Player>("MerchantEntry._player", "_player");

    internal static void MerchantRelicPreview(MerchantRelicEntry entry)
    {
        var entryPlayer = EntryPlayer.Value;
        if (entryPlayer == null) return;
        RelicPreviewContext.Register(entry.Model, entryPlayer(entry));
    }

    internal static void RewardRelicPreview(RelicReward reward) =>
        RelicPreviewContext.Register(reward.Relic, reward.Player);

    internal static List<CodeInstruction> GuardDamageTranspiler(List<CodeInstruction> original, MethodBase originalMethod) =>
        GuardDamagePresentation.Transpile(original, originalMethod);

    internal static bool WaveFacingRedirect(PlayerChoiceContext context, IEnumerable<DamageTargetData>? targets, decimal amount, ValueProp props,
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

    private static readonly SoftRef.Ref<MethodInfo> MaxAscensionSetter =
        SoftRef.Register<MethodInfo>("StartRunLobby.MaxAscension", () => AccessTools.PropertySetter(typeof(StartRunLobby), nameof(StartRunLobby.MaxAscension)));
    private static readonly SoftRef.Ref<MethodInfo> UpdateMaxSharedAscension =
        SoftRef.Register<MethodInfo>("StartRunLobby.UpdateMaxSharedAscension", () => AccessTools.Method(typeof(StartRunLobby), "UpdateMaxSharedAscension"));

    internal static bool DifficultyMultiplayer(StartRunLobby lobby)
    {
        if (!lobby.Players.Any(p => CharacterScope.IsConstruct(p))) return true;
        var setter = MaxAscensionSetter.Value;
        if (setter == null) return true;
        int maximum = ConstructDifficulty.LobbyMaximum(lobby);
        if (maximum != lobby.MaxAscension)
        {
            try { setter.Invoke(lobby, [maximum]); }
            catch (Exception error)
            {
                Diagnostics.Write("warn", "lobby.max_ascension.failed", new { error = error.ToString() });
                return true;
            }
            lobby.LobbyListener.MaxAscensionChanged();
            if (lobby.Ascension > maximum && lobby.NetService.Type == NetGameType.Host)
                lobby.SyncAscensionChange(maximum);
        }
        return false;
    }

    internal static void DifficultyChanged(StartRunLobby lobby)
    {
        if (lobby.NetService.Type is not (NetGameType.Host or NetGameType.Client)) return;
        var update = UpdateMaxSharedAscension.Value;
        if (update == null) return;
        try { update.Invoke(lobby, [true]); }
        catch (Exception error)
        {
            Diagnostics.Write("warn", "lobby.max_ascension_update.failed", new { error = error.ToString() });
        }
    }
}
