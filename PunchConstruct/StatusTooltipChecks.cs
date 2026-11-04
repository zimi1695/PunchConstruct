using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static string StatusText(PowerModel power) => power.HoverTips.OfType<HoverTip>().First().Description;
    private static async Task StatusTooltipChecks(Player player, Creature enemy)
    {
        var checkedTypes = new HashSet<Type>();
        async Task Inspect<T>(int amount, Creature owner, params string[] required) where T : PowerModel, new()
        {
            var power = await PowerCmd.Apply<T>(Context, owner, amount, player.Creature, null);
            // Divinity is a hidden stance in the source mod. Its explanation
            // remains available to card hover tips, not the bottom status row.
            bool hiddenStance = typeof(T) == typeof(ObserverDivinity);
            var text = hiddenStance ? power!.GetDumbHoverTip(null).Description : StatusText(power!);
            if (hiddenStance) Check(!power!.IsVisible && !power.HoverTips.Any(),
                "Original hidden Divinity has no bottom status icon or status hover tip");
            Check(required.All(text.Contains) && !text.Contains('{') && !text.Contains('}'),
                "Actual status tooltip resolves all values and conditions: " + typeof(T).Name + " => " + text);
            if (typeof(T).Assembly == typeof(ModEntry).Assembly) checkedTypes.Add(typeof(T));
            await PowerCmd.Remove(power!);
        }
        await Inspect<ForgeWorkshopPower>(6, player.Creature, "6", "锻造", "格挡");
        await Inspect<RoyalBearingPower>(6, player.Creature, "6", "回合结束", "手牌", "君王之剑");
        await Inspect<StrikeWhileHotPower>(17, player.Creature, "17", "大于等于10", "回合结束");
        await Inspect<FinalJudgmentPower>(1, player.Creature, "灾厄", "大于等于生命值", "无法");
        await Inspect<UnarmToArmsPower>(1, player.Creature, "本回合", "等量召唤", "已有格挡保留");
        await Inspect<BoxingChampionFormPower>(5, player.Creature, "5", "力量", "敏捷", "回合开始");
        await Inspect<ObserverMantra>(9, player.Creature, "10", "9", "消耗", "神格");
        await Inspect<ObserverDivinity>(1, player.Creature, "3", "三倍", "回合结束");
        await Inspect<ObserverEnergyDown>(2, player.Creature, "2", "每回合开始", "能量");
        await Inspect<ObserverBlockReturn>(4, enemy, "4", "攻击者", "格挡");
        await Inspect<ObserverColdObservationPower>(3, player.Creature, "3张攻击牌", "每段", "等量格挡");
        await Inspect<ConstructSicEmPower>(7, enemy, "本回合", "7", "召唤");
        await Inspect<HelicalDartPower>(3, player.Creature, "3", "敏捷");
        await PowerCmd.Remove<DexterityPower>(player.Creature);
        await Inspect<GuardStrengthNextTurnPower>(11, player.Creature, "下回合", "11", "临时力量");
        var expiry = await PowerCmd.Apply<GuardTemporaryStrengthPower>(Context, player.Creature, 4, player.Creature, null);
        Check(expiry is { IsVisible: false }, "Internal temporary-Strength expiry helper deliberately has no second visible status or tooltip");
        checkedTypes.Add(typeof(GuardTemporaryStrengthPower));
        await PowerCmd.Remove(expiry!);
        await PowerCmd.Remove<StrengthPower>(player.Creature);
        await Play<NeverBackDown>(player);
        var never = player.Creature.GetPower<NeverBackDownPower>()!;
        Check(StatusText(never).Contains("1张[gold]拳斗[/gold]") && !StatusText(never).Contains("拳斗+") && !StatusText(never).Contains("其中"),
            "Normal Never Back Down status describes one normal fist without zero-upgrade boilerplate");
        await PowerCmd.Remove(never);
        await Play<NeverBackDown>(player, upgraded: true);
        never = player.Creature.GetPower<NeverBackDownPower>()!;
        Check(StatusText(never).Contains("1张[gold]拳斗[/gold]") && !StatusText(never).Contains("拳斗+"),
            "Upgraded Never Back Down status also describes the normal fist");
        await Play<NeverBackDown>(player);
        Check(StatusText(never).Contains("2张[gold]拳斗[/gold]") && StatusText(never).Contains("2张[gold]灼烧[/gold]"),
            "Mixed Never Back Down status reports combined normal fist and Burn counts");
        checkedTypes.Add(typeof(NeverBackDownPower));
        await PowerCmd.Remove(never);
        await Play<TonyWave>(player);
        await Play<TonyWave>(player, upgraded: true);
        var delay = player.Creature.GetPower<DelayedWavePower>()!;
        Check(StatusText(delay).Contains("2段") && StatusText(delay).Contains("随机") && StatusText(delay).Contains("7伤害、7格挡") && StatusText(delay).Contains("9伤害、9格挡"),
            "Mixed delayed waves report their count, random targeting and each separate damage/block pair");
        checkedTypes.Add(typeof(DelayedWavePower));
        await PowerCmd.Remove(delay);
        var form = await PowerCmd.Apply<IronWaveFormPower>(Context, player.Creature, 3, player.Creature, null);
        Check(StatusText(form!).Contains("前3次") && StatusText(form!).Contains("未被格挡") && StatusText(form!).Contains("还可触发3次"),
            "Iron Wave Form tooltip includes its cap, unblocked-damage rule and remaining uses");
        await CreatureCmd.Damage(Context, enemy, 1, ValueProp.Unpowered, player.Creature);
        Check(StatusText(form!).Contains("还可触发2次"), "Actual damage updates the remaining-use tooltip");
        checkedTypes.Add(typeof(IronWaveFormPower));
        await PowerCmd.Remove(form!);
        var declared = typeof(ModEntry).Assembly.GetTypes().Where(t => !t.IsAbstract && typeof(PowerModel).IsAssignableFrom(t)).ToHashSet();
        Check(checkedTypes.SetEquals(declared), "Every concrete custom status is included in the actual tooltip audit: " + declared.Count);
        foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
    }
}
