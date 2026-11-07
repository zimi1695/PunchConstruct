using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Entities.Ascension;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.RestSite;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct.NativeBridge;

// Every presentation hook forwards to PresentationAdapter. No business rule is
// implemented here; a failed adapter leaves the native result in place.
[HarmonyPatch(typeof(NCharacterSelectButton), nameof(NCharacterSelectButton.Init))]
internal static class BridgeCharacterPortrait
{
    private static void Postfix(NCharacterSelectButton __instance, CharacterModel character) =>
        AdaptationRegistry.Run("presentation", "CharacterPortrait",
            () => PresentationAdapter.CharacterPortrait(__instance, character));
}

[HarmonyPatch(typeof(SfxCmd), nameof(SfxCmd.Play), [typeof(string), typeof(float)])]
internal static class BridgeSelectionCue
{
    private static bool Prefix(string sfx, float volume) =>
        AdaptationRegistry.Guard("presentation", "SelectionCue",
            () => PresentationAdapter.SelectionCue(sfx, volume));
}

[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.PlayOneShot), [typeof(string), typeof(float)])]
internal static class BridgeSelectionAudioOutput
{
    private static bool Prefix(string path, float volume) =>
        AdaptationRegistry.Guard("presentation", "SelectionAudioOutput",
            () => PresentationAdapter.SelectionAudioOutput(path, volume));
}

[HarmonyPatch]
internal static class BridgeSelectionUiAudio
{
    private static IEnumerable<MethodBase> TargetMethods() => PresentationAdapter.SelectionTargets();

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = instructions.ToList();
        return AdaptationRegistry.Run("presentation", "SelectionUiAudio", original,
            () => PresentationAdapter.SelectionUiTranspiler(original));
    }
}

[HarmonyPatch(typeof(NFakeMerchant), "StartCharacterAnimation")]
internal static class BridgeFakeMerchantAnimation
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = instructions.ToList();
        return AdaptationRegistry.Run("presentation", "FakeMerchantAnimation", original,
            () => PresentationAdapter.FakeMerchantAnimation(original));
    }
}

[HarmonyPatch(typeof(NMerchantCharacter), nameof(NMerchantCharacter.PlayAnimation))]
internal static class BridgeMerchantAnimation
{
    private static void Prefix(NMerchantCharacter __instance, ref string __0)
    {
        var trigger = __0;
        AdaptationRegistry.Run("presentation", "MerchantAnimation",
            () => PresentationAdapter.MerchantAnimationPrefix(__instance, ref trigger));
        __0 = trigger;
    }
}

[HarmonyPatch(typeof(NRestSiteCharacter), nameof(NRestSiteCharacter._Ready))]
internal static class BridgeCampAnimation
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = instructions.ToList();
        return AdaptationRegistry.Run("presentation", "CampAnimation", original,
            () => PresentationAdapter.CampAnimation(original));
    }
}

[HarmonyPatch]
internal static class BridgeCardPresentation
{
    private static MethodBase TargetMethod() => AccessTools.Method(
        AccessTools.Method(typeof(CardModel), nameof(CardModel.OnPlayWrapper))
            .GetCustomAttribute<AsyncStateMachineAttribute>()!.StateMachineType, "MoveNext");

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> source)
    {
        var original = source.ToList();
        return AdaptationRegistry.Run("presentation", "CardPresentation", original,
            () => PresentationAdapter.CardPresentationTranspiler(original));
    }
}

[HarmonyPatch(typeof(AttackCommand), nameof(AttackCommand.Execute))]
internal static class BridgeAttackPresentation
{
    private static void Prefix(AttackCommand __instance, int ____hitCount, ref string? ____attackerSfx, ref string? ____tmpAttackerSfx)
    {
        var attackerSfx = ____attackerSfx;
        var temporarySfx = ____tmpAttackerSfx;
        AdaptationRegistry.Run("presentation", "AttackPresentation",
            () => PresentationAdapter.AttackPresentation(__instance, ____hitCount, ref attackerSfx, ref temporarySfx));
        ____attackerSfx = attackerSfx;
        ____tmpAttackerSfx = temporarySfx;
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.TriggerAnim))]
internal static class BridgeAnimationSound
{
    private static bool Prefix(Creature creature, ref string triggerName, ref Task __result)
    {
        var trigger = triggerName;
        var task = __result;
        var runNative = AdaptationRegistry.Guard("presentation", "AnimationSound",
            () => PresentationAdapter.AnimationSound(creature, ref trigger, ref task));
        triggerName = trigger;
        __result = task;
        return runNative;
    }
}

[HarmonyPatch(typeof(NCardTransformShineVfx), "UpdateCard")]
internal static class BridgeTransformRevealSound
{
    private static void Postfix(NCard __0, CardModel __1) =>
        AdaptationRegistry.Run("presentation", "TransformReveal",
            () => PresentationAdapter.TransformReveal(__0, __1));
}

[HarmonyPatch(typeof(NMapDrawings), nameof(NMapDrawings.SetDrawingModeLocal))]
internal static class BridgeMapDrawing
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = instructions.ToList();
        return AdaptationRegistry.Run("presentation", "MapDrawing", original,
            () => PresentationAdapter.MapDrawingTranspiler(original));
    }
}

[HarmonyPatch(typeof(ENetHost), nameof(ENetHost.Update))]
internal static class BridgeDisconnectedPeers
{
    private static void Postfix(ENetHost __instance) =>
        AdaptationRegistry.Run("presentation", "DisconnectedPeers",
            () => PresentationAdapter.DisconnectedPeers(__instance));
}

[HarmonyPatch(typeof(MonsterModel), "get_VisualsPath")]
internal static class BridgeVisualsPath
{
    private static void Postfix(MonsterModel __instance, ref string __result)
    {
        var path = __result;
        AdaptationRegistry.Run("presentation", "VisualsPath",
            () => path = PresentationAdapter.VisualsPath(__instance, path));
        __result = path;
    }
}

[HarmonyPatch]
internal static class BridgeIronWavePortrait
{
    private static IEnumerable<MethodBase> TargetMethods() =>
        new[] { nameof(CardModel.PortraitPath), nameof(CardModel.BetaPortraitPath) }
            .Select(name => AccessTools.PropertyGetter(typeof(CardModel), name));

    private static void Postfix(CardModel __instance, ref string __result)
    {
        var path = __result;
        AdaptationRegistry.Run("presentation", "IronWavePortrait",
            () => path = PresentationAdapter.IronWavePortrait(__instance, path));
        __result = path;
    }
}

[HarmonyPatch]
internal static class BridgeRelicIcons
{
    private static IEnumerable<MethodBase> TargetMethods() => new[] { "Icon", "IconOutline", "BigIcon" }
        .Select(name => AccessTools.PropertyGetter(typeof(RelicModel), name));

    private static void Postfix(RelicModel __instance, MethodBase __originalMethod, ref Texture2D? __result)
    {
        var texture = __result;
        AdaptationRegistry.Run("presentation", "RelicIcon", () => PresentationAdapter.RelicIcon(__instance, __originalMethod, ref texture));
        __result = texture;
    }
}

[HarmonyPatch(typeof(NRelic), "Reload")]
internal static class BridgeRelicControlIcons
{
    private static void Postfix(NRelic __instance) =>
        AdaptationRegistry.Run("presentation", "RelicControlIcons",
            () => PresentationAdapter.RelicControlReload(__instance));
}

[HarmonyPatch(typeof(MerchantRelicEntry), "SetModel")]
internal static class BridgeMerchantRelicPreview
{
    private static void Postfix(MerchantRelicEntry __instance) =>
        AdaptationRegistry.Run("presentation", "MerchantRelicPreview",
            () => PresentationAdapter.MerchantRelicPreview(__instance));
}

[HarmonyPatch(typeof(RelicReward), nameof(RelicReward.Populate))]
internal static class BridgeRewardRelicPreview
{
    private static void Postfix(RelicReward __instance) =>
        AdaptationRegistry.Run("presentation", "RewardRelicPreview",
            () => PresentationAdapter.RewardRelicPreview(__instance));
}

[HarmonyPatch]
internal static class BridgeGuardDamagePresentation
{
    private static MethodBase TargetMethod() => GuardDamagePresentation.Target();

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var original = instructions.ToList();
        return AdaptationRegistry.Run("presentation", "GuardDamagePresentation", original,
            () => PresentationAdapter.GuardDamageTranspiler(original, __originalMethod));
    }
}

[HarmonyPatch(typeof(CreatureCmd), nameof(CreatureCmd.Damage), [typeof(PlayerChoiceContext), typeof(IEnumerable<Creature>),
    typeof(decimal), typeof(ValueProp), typeof(Creature), typeof(CardModel), typeof(CardPlay)])]
internal static class BridgeWaveFacing
{
    private static bool Prefix(PlayerChoiceContext choiceContext, IEnumerable<Creature> targets, decimal amount, ValueProp props,
        Creature? dealer, CardModel? cardSource, CardPlay? cardPlay, ref Task<IEnumerable<DamageResult>> __result)
    {
        var redirected = __result;
        var runNative = AdaptationRegistry.Guard("presentation", "WaveFacing",
            () => PresentationAdapter.WaveFacingRedirect(choiceContext, targets, amount, props, dealer, cardSource, cardPlay, ref redirected));
        __result = redirected;
        return runNative;
    }
}

[HarmonyPatch(typeof(ProgressState), nameof(ProgressState.GetOrCreateCharacterStats))]
internal static class BridgeDifficultyStats
{
    private static void Postfix(ModelId __0, CharacterStats __result) =>
        AdaptationRegistry.Run("presentation", "DifficultyStats",
            () => PresentationAdapter.DifficultyStats(__0, __result));
}

[HarmonyPatch(typeof(StartRunLobby), "SetSingleplayerAscensionAfterCharacterChanged")]
internal static class BridgeDifficultySingleplayer
{
    private static void Prefix(ModelId __0) =>
        AdaptationRegistry.Run("presentation", "DifficultySingleplayer",
            () => PresentationAdapter.DifficultySingleplayer(__0));
}

[HarmonyPatch(typeof(StartRunLobby), "UpdateMaxMultiplayerAscension")]
internal static class BridgeDifficultyMultiplayer
{
    private static bool Prefix(StartRunLobby __instance) =>
        AdaptationRegistry.Guard("presentation", "DifficultyMultiplayer",
            () => PresentationAdapter.DifficultyMultiplayer(__instance));
}

[HarmonyPatch(typeof(StartRunLobby), "ChangeCharacter")]
internal static class BridgeDifficultyChange
{
    private static void Postfix(StartRunLobby __instance) =>
        AdaptationRegistry.Run("presentation", "DifficultyChange",
            () => PresentationAdapter.DifficultyChanged(__instance));
}
