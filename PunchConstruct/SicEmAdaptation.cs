using BaseLib.Abstracts;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

internal static class ConstructSicEm
{
    internal static bool Apply(SicEm __instance, PlayerChoiceContext choiceContext, CardPlay cardPlay, ref Task __result)
    {
        if (!RegentAdaptations.Owns(__instance)) return true;
        __result = Play(__instance, choiceContext, cardPlay);
        return false;
    }

    private static async Task Play(SicEm card, PlayerChoiceContext context, CardPlay play)
    {
        var target = play.Target!;
        await DamageCmd.Attack(card.DynamicVars.OstyDamage.BaseValue).FromCard(card, play).Targeting(target)
            .WithHitFx("vfx/vfx_attack_blunt", null, "blunt_attack.mp3").Execute(context);
        await PowerCmd.Apply<ConstructSicEmPower>(context, target, card.DynamicVars["SicEmPower"].BaseValue, card.Owner.Creature, card);
    }
}

// Preserve the native card ID, upgrade values and save format. Its existing
// OstyDamage variable needs the player's ordinary damage forecast in this role.
internal static class ConstructSicEmPreview
{
    internal static bool Apply(OstyDamageVar __instance, CardModel card, CardPreviewMode previewMode, Creature? target, bool runGlobalHooks)
    {
        if (card is not SicEm || !RegentAdaptations.Owns(card)) return true;
        var damage = new DamageVar(__instance.Name, __instance.BaseValue, __instance.Props);
        damage.SetOwner(card);
        damage.UpdateCardPreview(card, previewMode, target, runGlobalHooks);
        __instance.EnchantedValue = damage.EnchantedValue;
        __instance.PreviewValue = damage.PreviewValue;
        return false;
    }
}

internal static class ConstructSicEmWarning
{
    internal static bool Apply(SicEm __instance, ref bool __result)
    {
        if (!RegentAdaptations.Owns(__instance)) return true;
        __result = false;
        return false;
    }
}

internal static class ConstructSicEmTags
{
    internal static void Apply(CardModel __instance, ref IEnumerable<CardTag> __result)
    {
        if (__instance is SicEm && RegentAdaptations.Owns(__instance))
            __result = __result.Where(t => t != CardTag.OstyAttack).ToArray();
    }
}

internal static class ConstructSicEmDescription
{
    internal static void Apply(CardModel __instance, ref LocString __result)
    {
        if (__instance is not SicEm || !RegentAdaptations.Owns(__instance)) return;
        var text = new LocString("cards", "PUNCHCONSTRUCT_ADAPTED_SIC_EM.description");
        text.AddVariablesFrom(__result);
        __result = text;
    }
}

// Native Sic Em has one shared stack and one applier on an enemy. Keep the
// adapted mark separate from native Necrobinder marks, and give every construct
// its own instance so teammates cannot merge or redirect each other's summons.
public sealed class ConstructSicEmPower : CustomPowerModel
{
    public override PowerType Type => PowerType.Debuff;
    public override PowerStackType StackType => PowerStackType.Counter;
    public override PowerInstanceType InstanceType => PowerInstanceType.InstancedPerApplier;
    public override string CustomPackedIconPath => ModelDb.Power<SicEmPower>().PackedIconPath;
    public override string CustomBigIconPath => ModelDb.Power<SicEmPower>().ResolvedBigIconPath;
    public override List<(string, string)> Localization => Loc.Power("紧追不放", "本回合，你或奥斯提每次对这名敌人造成伤害时，召唤{Amount}。", "本回合，你或奥斯提每次对这名敌人造成伤害时，召唤{Amount}。");

    public override async Task AfterDamageGiven(PlayerChoiceContext context, Creature? dealer, DamageResult result, ValueProp props, Creature? target, CardModel? cardSource)
    {
        var player = Applier?.Player;
        if (player == null || target != Owner || dealer == null ||
            (dealer != player.Creature && dealer.PetOwner != player)) return;
        await OstyCmd.Summon(context, player, Amount, this);
    }
    public override async Task AfterSideTurnEnd(PlayerChoiceContext context, CombatSide side, IEnumerable<Creature> participants)
    {
        if (participants.Contains(Owner)) await PowerCmd.Remove(this);
    }
}
