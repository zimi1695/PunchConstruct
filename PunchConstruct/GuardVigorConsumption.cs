using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands.Builders;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace PunchConstruct;

// Native Vigor keeps an AttackCommand until the power disappears. A blocked
// Thorns hit can replenish it *inside* that command, so it survives consumption
// and the stale command otherwise prevents every subsequent consumption.
internal static class GuardVigorConsumption
{
    private static readonly Type DataType = AccessTools.Inner(typeof(VigorPower), "Data");
    private static readonly MethodInfo GetData = AccessTools.Method(typeof(PowerModel), "GetInternalData").MakeGenericMethod(DataType);
    private static readonly FieldInfo Command = AccessTools.Field(DataType, "commandToModify");
    private static readonly FieldInfo StartingAmount = AccessTools.Field(DataType, "amountWhenAttackStarted");

    internal static void Apply(VigorPower __instance, AttackCommand command, ref Task __result)
    {
        if (command.Attacker != __instance.Owner || !CharacterScope.IsConstruct(__instance.Owner.Player)) return;
        var player = __instance.Owner.Player!;
        if (!player.Relics.Any(r => r is GuardStance)) return;
        object data = GetData.Invoke(__instance, null)!;
        if (!ReferenceEquals(Command.GetValue(data), command)) return;
        __result = Complete(__result, __instance, command, data);
    }

    private static async Task Complete(Task original, VigorPower power, AttackCommand command, object data)
    {
        int starting = (int)StartingAmount.GetValue(data)!;
        await original;
        if (!ReferenceEquals(Command.GetValue(data), command)) return;
        Command.SetValue(data, null);
        StartingAmount.SetValue(data, 0);
        Diagnostics.Write("info", "guard.vigor.consumed", new { starting, remaining = power.Amount, source = command.ModelSource?.Id.ToString() });
    }
}
