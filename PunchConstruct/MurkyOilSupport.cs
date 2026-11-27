using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;

namespace PunchConstruct;

// Stretch is a post-0.113 power. Resolve it by name so a 0.111/0.112 runtime
// loads the potion and falls back to Vigor instead of failing at JIT time.
internal static class MurkyOilSupport
{
    private const string StretchTypeName = "MegaCrit.Sts2.Core.Models.Powers.StretchPower";
    private static readonly Type? StretchType = typeof(VigorPower).Assembly.GetType(StretchTypeName);
    private static bool forceMissing;

    internal static bool HasStretch => !forceMissing && StretchType != null;

    internal static PowerModel? StretchModel()
    {
        if (!HasStretch) return null;
        try
        {
            return ModelDb.GetByIdOrNull<PowerModel>(ModelDb.GetId(StretchType!));
        }
        catch (Exception error)
        {
            Diagnostics.Write("warn", "murky_oil.stretch_lookup_failed", new { error = error.ToString() });
            return null;
        }
    }

    internal static async Task Apply(PlayerChoiceContext context, Creature target, decimal amount, Creature applier)
    {
        var model = StretchModel();
        if (model == null) return;
        await PowerCmd.Apply(context, model.ToMutable(), target, amount, applier, null);
    }

    internal static decimal AmountOn(Creature creature)
    {
        var model = StretchModel();
        return model == null ? 0m : creature.GetPower(model.Id)?.Amount ?? 0m;
    }

    internal static async Task Remove(Creature creature)
    {
        var model = StretchModel();
        if (model == null) return;
        await PowerCmd.Remove(creature.GetPower(model.Id));
    }

    // QA seam: exercise the 0.111/0.112 path on a runtime that still has the
    // type. Never set in normal play.
    internal static void ForceMissingForTest(bool value) => forceMissing = value;
}
