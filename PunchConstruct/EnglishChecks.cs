using System.Text.RegularExpressions;
using BaseLib.Abstracts;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace PunchConstruct;

// Switch the real game to English and prove that every player-facing source is
// translated: compiled card/relic/power/character text and all JSON tables.
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class EnglishChecks
{
    private static readonly Regex Cjk = new(@"[\u4e00-\u9fff]", RegexOptions.Compiled);
    // Every shipped non-Chinese game locale must fall back to the English table
    // rather than showing the authored Chinese text.
    private static readonly string[] FallbackLanguages =
        ["deu", "esp", "fra", "ind", "ita", "jpn", "kor", "pol", "ptb", "rus", "spa", "tha", "tur"];
    private static int _checks;

    private static void Postfix(NMainMenu __instance)
    {
        if (!OS.GetCmdlineArgs().Contains("--punchconstruct-englishcheck")) return;
        Callable.From(() => { _ = Run(__instance.GetTree()); }).CallDeferred();
    }

    private static async Task Run(SceneTree tree)
    {
        try
        {
            await QaStartupPreload.Wait(tree);
            var manager = LocManager.Instance;
            Check(manager != null, "LocManager is available");
            manager!.SetLanguage("eng");
            for (int i = 0; i < 5; i++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            Check(manager.Language == "eng", "game language is English");
            Check(Loc.English, "Loc helper follows the game language");
            Check(EnglishText.Translations.Count >= 220, "compiled English table is populated: " + EnglishText.Translations.Count);
            Check(EnglishText.Translations.Values.All(value => !Cjk.IsMatch(value)), "compiled English table contains no Chinese");

            foreach (var card in ModelDb.CardPool<ConstructCardPool>().AllCards)
                if (card is CustomCardModel custom) Localization(custom.Localization ?? [], "card " + card.Id);
            foreach (var relic in ModelDb.RelicPool<ConstructRelicPool>().AllRelics)
                if (relic is CustomRelicModel custom) Localization(custom.Localization ?? [], "relic " + relic.Id);
            foreach (var power in Powers())
                if (power is CustomPowerModel custom) Localization(custom.Localization ?? [], "power " + power.Id);
            Localization(ModelDb.Character<ConstructCharacter>().Localization, "character");

            Json("res://PunchConstruct/localization/eng/ancients.json");
            Json("res://PunchConstruct/localization/eng/events.json");
            Json("res://PunchConstruct/localization/eng/relics.json");
            Json("res://PunchConstruct/localization/eng/cards.json");

            foreach (var language in FallbackLanguages)
            {
                await Switch(tree, manager, language);
                Check(Loc.English && !Loc.Chinese, language + " uses the English fallback");
                foreach (var table in new[] { "ancients", "events", "relics", "cards" })
                    Json("res://PunchConstruct/localization/" + language + "/" + table + ".json");
            }
            foreach (var table in new[] { "ancients", "events", "relics", "cards" })
                Json("res://PunchConstruct/localization/zht/" + table + ".json", false);

            await Switch(tree, manager, "zht");
            Check(Loc.Chinese && !Loc.English, "traditional Chinese keeps the Chinese table");
            await Switch(tree, manager, "zhs");
            Check(Loc.Chinese && !Loc.English, "simplified Chinese keeps the Chinese table");
            await Switch(tree, manager, "eng");
            Check(Loc.English && !Loc.Chinese, "English remains selectable after locale probing");

            GD.Print($"[Construct ENGLISH PASS] {_checks} checks; English plus thirteen fallback locales and four tables");
            tree.Quit();
        }
        catch (Exception error)
        {
            GD.Print("[Construct QA FAIL] " + error);
            GD.PrintErr(error.ToString());
            tree.Quit(1);
        }
    }

    private static async Task Switch(SceneTree tree, LocManager manager, string language)
    {
        manager.SetLanguage(language);
        for (int i = 0; i < 3; i++) await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
        Check(manager.Language == language, "language switch accepted: " + language);
    }

    private static PowerModel[] Powers() =>
    [
        ModelDb.Power<DelayedWavePower>(), ModelDb.Power<IronWaveFormPower>(),
        ModelDb.Power<ForgeWorkshopPower>(), ModelDb.Power<RoyalBearingPower>(), ModelDb.Power<StrikeWhileHotPower>(),
        ModelDb.Power<FinalJudgmentPower>(), ModelDb.Power<UnarmToArmsPower>(), ModelDb.Power<NeverBackDownPower>(),
        ModelDb.Power<BoxingChampionFormPower>(), ModelDb.Power<GuardStrengthNextTurnPower>(),
        ModelDb.Power<ConstructSicEmPower>(), ModelDb.Power<ObserverMantra>(), ModelDb.Power<ObserverDivinity>(),
        ModelDb.Power<ObserverBlockReturn>(), ModelDb.Power<ObserverColdObservationPower>(), ModelDb.Power<ObserverEnergyDown>()
    ];

    private static void Localization(List<(string, string)> entries, string label)
    {
        foreach (var (key, text) in entries)
            Check(!Cjk.IsMatch(text), label + " has no Chinese in " + key + ": " + text);
    }

    private static void Json(string path, bool english = true)
    {
        Check(Godot.FileAccess.FileExists(path), "localization table exists: " + path);
        var parsed = Godot.Json.ParseString(Godot.FileAccess.GetFileAsString(path));
        Check(parsed.VariantType == Variant.Type.Dictionary, "localization table parses: " + path);
        var table = parsed.AsGodotDictionary();
        Check(table.Count() > 0, "localization table is not empty: " + path);
        if (!english) return;
        foreach (var key in table.Keys)
        {
            var value = table[key];
            if (value.VariantType == Variant.Type.String)
                Check(!Cjk.IsMatch(value.AsString()), "localization table value has no Chinese: " + path + " / " + key.AsString());
        }
    }

    private static void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException(description);
        GD.Print($"[Construct QA {_checks++:D3}] {description}");
    }
}
