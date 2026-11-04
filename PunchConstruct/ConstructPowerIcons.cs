namespace PunchConstruct;

internal static class ConstructPowerIcons
{
    private static readonly Dictionary<Type, string> Names = new()
    {
        [typeof(DelayedWavePower)] = "delayed_wave",
        [typeof(IronWaveFormPower)] = "iron_wave_form",
        [typeof(ForgeWorkshopPower)] = "forge_workshop",
        [typeof(RoyalBearingPower)] = "royal_bearing",
        [typeof(StrikeWhileHotPower)] = "strike_while_hot",
        [typeof(FinalJudgmentPower)] = "final_judgment",
        [typeof(UnarmToArmsPower)] = "unarm_to_arms",
        [typeof(NeverBackDownPower)] = "never_back_down",
        [typeof(BoxingChampionFormPower)] = "boxing_champion_form",
        [typeof(GuardStrengthNextTurnPower)] = "brass_nut",
    };

    internal static string Packed(Type power) => $"res://PunchConstruct/art/power_{Names[power]}.ctex";
    internal static string Big(Type power) => $"res://PunchConstruct/art/power_{Names[power]}_big.ctex";
}
