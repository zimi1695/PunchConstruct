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
    public override List<(string, string)> Localization => Loc.Relic("古铜之拳", "每次完全格挡伤害，下回合获得1点[gold]临时力量[/gold]。此效果本场战斗中每次触发后+1。", "这拳头有力气");
    public override async Task AfterDamageReceived(PlayerChoiceContext context, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? card)
    {
        if (target != Owner.Creature || !result.WasFullyBlocked) return;
        int amount = NextAmount++;
        InvokeDisplayAmountChanged();
        Flash();
        await GuardStancePowerGain.Apply<GuardStrengthNextTurnPower>(context, Owner.Creature, amount);
    }
}

internal static class ScopedAncientTooth
{
    [ThreadStatic] internal static bool ConstructSetup;
    internal static IEnumerable<System.Reflection.MethodBase> Targets() => [
        AccessTools.Method(typeof(ArchaicTooth), nameof(ArchaicTooth.SetupForPlayer)),
        AccessTools.Method(typeof(ArchaicTooth), "GetTranscendenceStarterCard")
    ];
    internal static void Apply(Player player, out bool __state)
    {
        __state = ConstructSetup; ConstructSetup = CharacterScope.IsConstruct(player);
    }
    internal static void Restore(bool __state) => ConstructSetup = __state;
}

// The native relic recomputes its replacement again when actually acquired.
// Scope that second lookup too; configuring the preview alone is insufficient.
internal static class ScopedAncientToothTransformation
{
    internal static bool Apply(ArchaicTooth __instance, CardModel __0, ref CardModel __result)
    {
        if (__0 is not IronWave || !RegentAdaptations.Owns(__0)) return true;
        __result = WaveEventRules.ScorchingReplacement(__0, 3);
        return false;
    }
}

internal static class AncientWaveMapping
{
    internal static void Apply(ref Dictionary<ModelId, CardModel> __result)
    {
        if (ScopedAncientTooth.ConstructSetup)
            __result = new Dictionary<ModelId, CardModel>(__result) { [ModelDb.GetId<IronWave>()] = ModelDb.Card<ScorchingIronWave>() };
    }
}
