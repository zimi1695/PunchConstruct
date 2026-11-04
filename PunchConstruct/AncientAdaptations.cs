using BaseLib.Abstracts;
using BaseLib.Utils;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

[Pool(typeof(ConstructRelicPool))]
public sealed class AncientGuardStance : CustomRelicModel
{
    private int NextAmount { get => CombatRelicState.AncientNext[this]; set => CombatRelicState.AncientNext[this] = value; }
    public override int DisplayAmount => NextAmount;
    public override Task BeforeCombatStart() { ResetGrowth(); return Task.CompletedTask; }
    public override Task AfterCombatEnd(MegaCrit.Sts2.Core.Rooms.CombatRoom room) { ResetGrowth(); return Task.CompletedTask; }
    private void ResetGrowth() { NextAmount = 1; InvokeDisplayAmountChanged(); }
    protected override string IconBaseName => "anchor";
    public override RelicRarity Rarity => RelicRarity.Ancient;
    public override string PackedIconPath => "res://PunchConstruct/art/ancient_guard_stance.ctex";
    protected override string PackedIconOutlinePath => "res://PunchConstruct/art/ancient_guard_stance_outline.ctex";
    protected override string BigIconPath => "res://PunchConstruct/art/ancient_guard_stance_big.ctex";
    public override List<(string, string)> Localization => new RelicLoc("古铜之拳", "每次完全格挡伤害，下回合获得1点[gold]临时力量[/gold]。此效果本场战斗中每次触发后+1。", "站稳，一拳破阵。");
    public override async Task AfterDamageReceived(PlayerChoiceContext context, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? card)
    {
        if (target != Owner.Creature || !result.WasFullyBlocked) return;
        int amount = NextAmount++;
        InvokeDisplayAmountChanged();
        Flash();
        await GuardStancePowerGain.Apply<GuardStrengthNextTurnPower>(context, Owner.Creature, amount);
    }
}

[HarmonyPatch]
internal static class ScopedAncientTooth
{
    [ThreadStatic] internal static bool ConstructSetup;
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => [
        AccessTools.Method(typeof(ArchaicTooth), nameof(ArchaicTooth.SetupForPlayer)),
        AccessTools.Method(typeof(ArchaicTooth), "GetTranscendenceStarterCard")
    ];
    private static void Prefix(Player player, out bool __state)
    {
        __state = ConstructSetup; ConstructSetup = player.Character is ConstructCharacter;
    }
    private static void Finalizer(bool __state) => ConstructSetup = __state;
}

// The native relic recomputes its replacement again when actually acquired.
// Scope that second lookup too; configuring the preview alone is insufficient.
[HarmonyPatch(typeof(ArchaicTooth), "GetTranscendenceTransformedCard")]
internal static class ScopedAncientToothTransformation
{
    private static bool Prefix(ArchaicTooth __instance, CardModel __0, ref CardModel __result)
    {
        if (__0 is not IronWave || !RegentAdaptations.Owns(__0)) return true;
        __result = WaveEventRules.ScorchingReplacement(__0, 3);
        return false;
    }
}

[HarmonyPatch(typeof(ArchaicTooth), "TranscendenceUpgrades", MethodType.Getter)]
internal static class AncientWaveMapping
{
    private static void Postfix(ref Dictionary<ModelId, CardModel> __result)
    {
        if (ScopedAncientTooth.ConstructSetup)
            __result = new Dictionary<ModelId, CardModel>(__result) { [ModelDb.GetId<IronWave>()] = ModelDb.Card<ScorchingIronWave>() };
    }
}
