using System.Runtime.CompilerServices;
using BaseLib.Abstracts;
using BaseLib.Utils;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Powers;

namespace PunchConstruct;

// Card-preserving rule shared by the soup variant and the native fallback:
// Soup replaces every Small Wave, including an Eternal one. The transform
// command rechecks removability after constructing the replacement, so only
// this exact source card is permitted until that command completes.
internal static class SoupWavePatch
{
    internal static readonly HashSet<CardModel> Transforming = [];
}

internal static class SoupExplicitReplacement
{
    internal static void Apply(CardModel __instance, ref bool __result)
    {
        if (SoupWavePatch.Transforming.Contains(__instance) && __instance is SmallWave && CharacterScope.IsConstruct(__instance))
            __result = true;
    }
}

internal static class HelmetMirror
{
    private sealed class State { public bool Applying; }
    private static readonly ConditionalWeakTable<RelicModel, State> States = new();
    private static bool Used(RelicModel relic) => relic is ConstructHelmet helmet && CombatRelicState.HelmetUsed[helmet];
    private static void SetUsed(RelicModel relic, bool value)
    {
        if (relic is ConstructHelmet helmet) CombatRelicState.HelmetUsed[helmet] = value;
    }
    internal static void Reset(RelicModel relic) { States.Remove(relic); SetUsed(relic, false); }
    internal static async Task Apply(RelicModel relic, PlayerChoiceContext context, PowerModel power, decimal change)
    {
        if (power.Owner != relic.Owner.Creature || change <= 0 || power is not (StrengthPower or DexterityPower)) return;
        var state = States.GetOrCreateValue(relic);
        if (state.Applying || Used(relic)) return;
        bool strength = power is StrengthPower;
        SetUsed(relic, true);
        state.Applying = true;
        try
        {
            // A mirrored grant never reflects back into this same relic.
            if (strength) await PowerCmd.Apply<DexterityPower>(context, relic.Owner.Creature, change, relic.Owner.Creature, null);
            else await PowerCmd.Apply<StrengthPower>(context, relic.Owner.Creature, change, relic.Owner.Creature, null);
        }
        finally { state.Applying = false; }
    }
}

[Pool(typeof(ConstructRelicPool))]
public sealed class ConstructHelmet : CustomRelicModel
{
    protected override string IconBaseName => "ruined_helmet";
    public override RelicRarity Rarity => ModelDb.Relic<RuinedHelmet>().Rarity;
    public override string PackedIconPath => ModelDb.Relic<RuinedHelmet>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/ruined_helmet.tres";
    public override List<(string, string)> Localization => Loc.Relic("损毁头盔", "每场战斗首次获得[gold]力量[/gold]或[gold]敏捷[/gold]时，获得等量的另一项属性。整场共触发一次。", "买到盗版了");
    public override Task BeforeCombatStart() { HelmetMirror.Reset(this); return Task.CompletedTask; }
    public override Task AfterPowerAmountChanged(PlayerChoiceContext context, PowerModel power, decimal amount, Creature? applier, CardModel? card) => HelmetMirror.Apply(this, context, power, amount);
}

[Pool(typeof(ConstructRelicPool))]
public sealed class ConstructDart : CustomRelicModel
{
    protected override string IconBaseName => "helical_dart";
    public override RelicRarity Rarity => ModelDb.Relic<HelicalDart>().Rarity;
    public override string PackedIconPath => ModelDb.Relic<HelicalDart>().PackedIconPath;
    protected override string PackedIconOutlinePath => "res://images/atlases/relic_outline_atlas.sprites/helical_dart.tres";
    public override List<(string, string)> Localization => Loc.Relic("螺线飞镖", "每打出一张攻击牌，本回合获得1点[gold]敏捷[/gold]。", "怎么灰不拉几的颜色");
    public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay play)
    {
        if (play.Player == Owner && play.Card.Type == CardType.Attack)
            await PowerCmd.Apply<HelicalDartPower>(context, Owner.Creature, 1, Owner.Creature, null);
    }
}

internal static class AdaptedRelicDescriptions
{
    internal static IEnumerable<System.Reflection.MethodBase> Targets() => [
        HarmonyLib.AccessTools.PropertyGetter(typeof(RelicModel), "Description"),
        HarmonyLib.AccessTools.PropertyGetter(typeof(RelicModel), "EventDescription")
    ];
    internal static void Apply(RelicModel __instance, ref MegaCrit.Sts2.Core.Localization.LocString __result)
    {
        // A reward/shop/chest preview has no owner. Use the local player's
        // character so the pre-pickup hover text matches the owned relic text.
        bool construct = CharacterScope.IsConstruct(__instance);
        if (!construct && CharacterScope.IsUnowned(__instance))
            construct = CharacterScope.IsConstruct(MegaCrit.Sts2.Core.Context.LocalContext.GetMe(MegaCrit.Sts2.Core.Runs.RunManager.Instance.DebugOnlyGetState()));
        if (!construct) return;
        string? key = __instance switch {
            StrikeDummy => "STRIKE_DUMMY", FakeStrikeDummy => "FAKE_STRIKE_DUMMY", GhostSeed => "GHOST_SEED",
            RippleBasin => "RIPPLE_BASIN", NutritiousSoup => "NUTRITIOUS_SOUP", RuinedHelmet => "RUINED_HELMET", HelicalDart => "HELICAL_DART",
            LeafyPoultice => "LEAFY_POULTICE", SeaGlass => "SEA_GLASS", DingyRug => "DINGY_RUG", LargeCapsule => "LARGE_CAPSULE", PrismaticGem => "PRISMATIC_GEM", ArchaicTooth => "ARCHAIC_TOOTH", NeowsTalisman => "NEOWS_TALISMAN", PandorasBox => "PANDORAS_BOX", PaelsClaw => "PAELS_CLAW", _ => null
        };
        if (key != null) __result = new MegaCrit.Sts2.Core.Localization.LocString("relics", "PUNCHCONSTRUCT_ADAPTED_" + key + ".description");
    }
}
