using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Localization;

namespace PunchConstruct;

// Language-aware wrapper for BaseLib's hardcoded localization records. JSON
// tables already resolve per language; this covers card/power/relic/character
// text that is compiled into the assembly.
internal static class Loc
{
    // Chinese locales keep the authored Chinese text; every other game language
    // falls back to the English table instead of showing Chinese to non-Chinese
    // players. A missing LocManager keeps the previous Chinese default.
    internal static bool Chinese => LocManager.Instance?.Language is "zhs" or "zht";
    internal static bool English => LocManager.Instance?.Language is { } && !Chinese;

    internal static string Pick(string chinese) =>
        English ? EnglishText.Translations.GetValueOrDefault(chinese, chinese) : chinese;

    internal static CardLoc Card(string title, string description, params (string, string)[] extra) =>
        new(Pick(title), Pick(description), extra.Select(entry => (entry.Item1, Pick(entry.Item2))).ToArray());

    internal static PowerLoc Power(string title, string description, string smartDescription, params (string, string)[] extra) =>
        new(Pick(title), Pick(description), Pick(smartDescription), extra.Select(entry => (entry.Item1, Pick(entry.Item2))).ToArray());

    internal static RelicLoc Relic(string title, string description, string flavor, params (string, string)[] extra) =>
        new(Pick(title), Pick(description), Pick(flavor), extra.Select(entry => (entry.Item1, Pick(entry.Item2))).ToArray());
}
