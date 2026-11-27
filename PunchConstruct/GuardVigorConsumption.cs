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
    // Lazy soft refs: a GetInternalData/Data rename must not poison the type at
    // static init (MakeGenericMethod on null throws). Resolve on first use.
    private static readonly SoftRef.Ref<MethodInfo> GetData = SoftRef.Register<MethodInfo>("VigorPower.GetInternalData<Data>", () =>
    {
        var data = AccessTools.Inner(typeof(VigorPower), "Data");
        var method = AccessTools.Method(typeof(PowerModel), "GetInternalData");
        return data == null || method == null ? null : method.MakeGenericMethod(data);
    });
    private static readonly SoftRef.Ref<FieldInfo> Command = SoftRef.Register<FieldInfo>("VigorPower.Data.commandToModify", () =>
    {
        var data = AccessTools.Inner(typeof(VigorPower), "Data");
        return data == null ? null : AccessTools.Field(data, "commandToModify");
    });
    private static readonly SoftRef.Ref<FieldInfo> StartingAmount = SoftRef.Register<FieldInfo>("VigorPower.Data.amountWhenAttackStarted", () =>
    {
        var data = AccessTools.Inner(typeof(VigorPower), "Data");
        return data == null ? null : AccessTools.Field(data, "amountWhenAttackStarted");
    });

    internal static void Apply(VigorPower __instance, AttackCommand command, ref Task __result)
    {
        if (command.Attacker != __instance.Owner || !CharacterScope.IsConstruct(__instance.Owner.Player)) return;
        var player = __instance.Owner.Player!;
        if (!player.Relics.Any(r => r is GuardStance)) return;
        var getData = GetData.Value;
        var commandField = Command.Value;
        if (getData == null || commandField == null) return;
        object? data = getData.Invoke(__instance, null);
        if (data == null || !ReferenceEquals(commandField.GetValue(data), command)) return;
        __result = Complete(__result, __instance, command, data);
    }

    private static async Task Complete(Task original, VigorPower power, AttackCommand command, object data)
    {
        var commandField = Command.Value;
        var startingField = StartingAmount.Value;
        if (commandField == null || startingField == null) { await original; return; }
        int starting = (int)startingField.GetValue(data)!;
        await original;
        if (!ReferenceEquals(commandField.GetValue(data), command)) return;
        commandField.SetValue(data, null);
        startingField.SetValue(data, 0);
        Diagnostics.Write("info", "guard.vigor.consumed", new { starting, remaining = power.Amount, source = command.ModelSource?.Id.ToString() });
    }
}
