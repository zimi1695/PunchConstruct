using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;

namespace PunchConstruct;

[HarmonyPatch]
internal static class ConstructPunchOffText
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => [
        AccessTools.Method(typeof(EventModel), "L10NLookup"),
        AccessTools.Method(typeof(EventModel), nameof(EventModel.GetOptionTitle)),
        AccessTools.Method(typeof(EventModel), nameof(EventModel.GetOptionDescription))
    ];
    private static void Postfix(EventModel __instance, ref LocString __result)
    {
        if (__instance is not (PunchOff or SpiralingWhirlpool or Amalgamator or Bugslayer) || __instance.Owner?.Character is not ConstructCharacter) return;
        var key = "PUNCHCONSTRUCT_" + __result.LocEntryKey;
        if (LocString.Exists("events", key))
        {
            var text = new LocString("events", key);
            text.AddVariablesFrom(__result);
            __result = text;
        }
    }
}

[HarmonyPatch]
internal static class ConstructAncientDialogues
{
    // The Architect derives directly from EventModel, not AncientEventModel.
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => [
        AccessTools.PropertyGetter(typeof(AncientEventModel), nameof(AncientEventModel.DialogueSet)),
        AccessTools.PropertyGetter(typeof(TheArchitect), nameof(TheArchitect.DialogueSet))
    ];

    private static void Postfix(EventModel __instance, AncientDialogueSet __result)
    {
        string character = ModelDb.GetId<ConstructCharacter>().Entry;
        if (__result.CharacterDialogues.ContainsKey(character)) return;
        string prefix = "PUNCHCONSTRUCT_" + __instance.Id.Entry;
        if (!LocString.Exists("ancients", prefix + ".talk." + character + ".0-0r.ancient")) return;
        var dialogues = Enumerable.Range(0, 3).Select(i =>
        {
            var dialogue = new AncientDialogue(["", "", ""])
            {
                VisitIndex = __instance is TheArchitect ? i : null,
                StartAttackers = ArchitectAttackers.None,
                EndAttackers = __instance is TheArchitect ? ArchitectAttackers.Both : ArchitectAttackers.None
            };
            dialogue.PopulateLines(prefix, character, i);
            for (int j = 0; j < 2; j++)
                AccessTools.PropertySetter(typeof(AncientDialogueLine), nameof(AncientDialogueLine.NextButtonText)).Invoke(dialogue.Lines[j],
                    [new LocString("ancients", prefix + ".talk." + character + $".{i}-{j}r.next")]);
            return dialogue;
        }).ToArray();
        __result.CharacterDialogues[character] = dialogues;
    }
}

[HarmonyPatch(typeof(AncientDialogueSet), nameof(AncientDialogueSet.GetValidDialogues))]
internal static class ConstructAncientDialogueSelection
{
    // 角色对白.txt 标注的是甲:乙:丙的比例。本地化文件里的 0/1/2 并不总按甲/乙/丙排列，
    // 所以这里按实际 JSON 索引存权重，而不是照抄正文顺序。
    internal static readonly IReadOnlyDictionary<string, int[]> Weights = new Dictionary<string, int[]>
    {
        ["NEOW"] = [1, 2, 3],        // 丙:乙:甲 = 3:2:1
        ["DARV"] = [3, 2, 1],        // 甲:乙:丙
        ["NONUPEIPE"] = [3, 2, 1],   // 甲:乙:丙
        ["OROBAS"] = [1, 2, 3],      // 丙:乙:甲
        ["PAEL"] = [1, 1, 1],
        ["TANX"] = [3, 1, 2],        // 甲:丙:乙
        ["TEZCATARA"] = [1, 1, 1],
        ["VAKUU"] = [2, 3, 1],       // 乙:甲:丙
        ["THE_ARCHITECT"] = [1, 1, 1]
    };

    private static void Postfix(AncientDialogueSet __instance, ModelId __0, ref IEnumerable<AncientDialogue> __result)
    {
        if (__0 != ModelDb.GetId<ConstructCharacter>()) return;
        if (!__instance.CharacterDialogues.TryGetValue(__0.Entry, out var own) || own.Count != 3) return;
        // 建筑师沿用原版三次胜利的固定顺序，1:1:1 已由每场各出现一次实现。
        if (own.Any(d => d.VisitIndex is not null)) return;
        string? ancient = AncientEntry(own[0]);
        if (ancient == null || !Weights.TryGetValue(ancient, out var weights)) return;

        var original = __result.ToArray();
        // 首次见面会拿到通用 first-ever 模板，保留它的入场/退场编排。
        if (original.FirstOrDefault() is { } template && !original.Any(IsConstructDialogue))
            CopyStaging(template, own);

        // 调用方从返回集合里等概率抽取；按权重重复条目即可得到作者标注的比例。
        var weighted = new List<AncientDialogue>(weights.Sum());
        for (int i = 0; i < own.Count; i++)
            for (int n = 0; n < weights[i]; n++)
                weighted.Add(own[i]);
        __result = weighted;
    }

    internal static bool IsConstructDialogue(AncientDialogue dialogue) =>
        dialogue.Lines.FirstOrDefault()?.LineText?.LocEntryKey.StartsWith("PUNCHCONSTRUCT_", StringComparison.Ordinal) == true;

    internal static int Index(AncientDialogue dialogue)
    {
        if (!IsConstructDialogue(dialogue)) return -1;
        string[] parts = dialogue.Lines[0].LineText!.LocEntryKey.Split('.');
        return parts.Length > 3 && parts[3].Length > 0 ? parts[3][0] - '0' : -1;
    }

    private static string? AncientEntry(AncientDialogue dialogue)
    {
        string? key = dialogue.Lines.FirstOrDefault()?.LineText?.LocEntryKey;
        const string prefix = "PUNCHCONSTRUCT_";
        if (key?.StartsWith(prefix, StringComparison.Ordinal) != true) return null;
        int end = key.IndexOf(".talk.", prefix.Length, StringComparison.Ordinal);
        return end < 0 ? null : key[prefix.Length..end];
    }

    private static void CopyStaging(AncientDialogue template, IEnumerable<AncientDialogue> own)
    {
        foreach (var dialogue in own)
            foreach (var property in new[] { nameof(AncientDialogue.StartAttackers), nameof(AncientDialogue.EndAttackers) })
                AccessTools.PropertySetter(typeof(AncientDialogue), property).Invoke(dialogue,
                    [AccessTools.PropertyGetter(typeof(AncientDialogue), property).Invoke(template, [])]);
    }
}
