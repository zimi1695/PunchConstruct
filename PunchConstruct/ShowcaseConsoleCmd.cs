using BaseLib.Abstracts;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

// Debug-only helper for recording. The deck and relic inventory are replaced
// directly; acquisition effects (Pandora's Box, Large Capsule, Leafy Poultice,
// Neow's Talisman and so on) are deliberately skipped so the showcase set
// stays exactly as listed. Run it at the start of a run, outside combat.
public sealed class ShowcaseConsoleCmd : AbstractConsoleCmd
{
    public override string CmdName => "pcdeck";
    public override string Args => "<all|changes|mod>";
    public override string Description =>
        "Replaces the construct's deck, relics and potions for recording: all mod content, 0.4.28 to 0.5.2 changes, or only the mod-original cards/relics/potions. Use at the start of a run, outside combat.";
    public override bool IsNetworked => true;

    public override CmdResult Process(Player? issuingPlayer, string[] args)
    {
        if (issuingPlayer == null || !RunManager.Instance.IsInProgress)
            return new CmdResult(false, "A run is currently not in progress!");
        if (CombatManager.Instance.IsInProgress)
            return new CmdResult(false, "Run pcdeck outside combat, at the start of a run.");
        if (args.Length != 1)
            return new CmdResult(false, "Usage: pcdeck <all|changes|mod>");

        List<CardModel> cards;
        List<RelicModel> relics;
        List<PotionModel> potions;
        switch (args[0].ToLowerInvariant())
        {
            case "all":
                cards = AllCards();
                relics = AllRelics();
                potions = AllPotions();
                break;
            case "changes":
                cards = ChangedCards();
                relics = ChangedRelics();
                potions = ChangedPotions();
                break;
            case "mod":
                cards = ModCards();
                relics = ModRelics();
                potions = ModPotions();
                break;
            default:
                return new CmdResult(false, "Usage: pcdeck <all|changes|mod>");
        }

        return new CmdResult(Replace(issuingPlayer, cards, relics, potions), true,
            $"Replacing deck with {cards.Count} cards, relics with {relics.Count} relics and potions with {potions.Count} potions...");
    }

    private static List<CardModel> AllCards() =>
        ModelDb.CardPool<ConstructCardPool>().AllCards.DistinctBy(card => card.Id).ToList();

    private static List<RelicModel> AllRelics() =>
        ModelDb.RelicPool<ConstructRelicPool>().AllRelics.DistinctBy(relic => relic.Id).ToList();

    private static List<PotionModel> AllPotions() =>
        ModelDb.PotionPool<ConstructPotionPool>().AllPotions.DistinctBy(potion => potion.Id).ToList();

    // "Mod-original" showcases content the mod authored itself: cards with their own
    // commissioned portrait under res://PunchConstruct/art/ (observer imports live
    // under observer/ and reused vanilla cards keep their native path), the original
    // relics, and the character potions. Per the author, Iron Wave (a vanilla card
    // re-skinned for the construct) and the gen-1 reuses Core Surge / Panacea are not
    // original designs and stay out; the two silver-recoloured relics are kept.
    private const string ModArtPrefix = "res://PunchConstruct/art/";

    // The 26 original-design cards: every custom card with an art/ portrait except
    // Core Surge and Panacea. Iron Wave is a vanilla card and is not included.
    private static List<CardModel> ModCards() =>
        AllCards().Where(card => card is CustomCardModel custom &&
            custom.CustomPortraitPath?.StartsWith(ModArtPrefix) == true &&
            card is not ConstructCoreSurge and not ConstructPanacea).ToList();

    // The seven original relics (Guard Stance, Ancient Guard Stance, Spinning Screw,
    // Open-End Wrench, Spare Knuckles, Emergency Oil Can, Clockwork Souvenir) plus the
    // two silver recolours the author counts as mod art: Ruined Helmet (ConstructHelmet)
    // and Helical Dart (ConstructDart). Other shared-relic variants are excluded.
    private static List<RelicModel> ModRelics() =>
        AllRelics().Where(relic => relic.PackedIconPath.StartsWith(ModArtPrefix) ||
            relic is ConstructHelmet or ConstructDart).ToList();

    // The character potion pool holds only the three mod-original potions.
    private static List<PotionModel> ModPotions() => AllPotions();

    // Cards whose definitions or construct behaviour changed between 0.4.28 and
    // 0.5.1, plus the six cards added in 0.5.1.
    private static List<CardModel> ChangedCards() =>
    [
        ModelDb.Card<FightMe>(),
        ModelDb.Card<NeverBackDown>(),
        ModelDb.Card<ObserverSashWhip>(),
        ModelDb.Card<GammaBlast>(),
        ModelDb.Card<Comet>(),
        ModelDb.Card<LeftHook>(),
        ModelDb.Card<ConstructClockworkMechanism>(),
        ModelDb.Card<ConstructMechanicalAscension>(),
        ModelDb.Card<ConstructBurnOut>(),
        ModelDb.Card<ConstructDomineering>(),
        ModelDb.Card<ConstructCoreSurge>(),
        ModelDb.Card<ConstructPanacea>(),
        ModelDb.Card<ConstructChoBarbs>(), ModelDb.Card<ConstructChoReady>(), ModelDb.Card<WahCho>(),
        ModelDb.Card<ConstructWahChoKi>(),
        ModelDb.Card<HobblingShot>(), ModelDb.Card<BlossomShot>(), ModelDb.Card<WhistlingArrows>(),
        ModelDb.Card<Nail>(), ModelDb.Card<Enrapture>(), ModelDb.Card<CoveringFire>()
    ];

    // Relics whose definitions changed between 0.4.28 and 0.5.1, plus the new
    // Clockwork Souvenir. Native-relic adaptations such as Archaic Tooth are not
    // in the construct relic pool and are intentionally not part of this set.
    private static List<RelicModel> ChangedRelics() =>
    [
        ModelDb.Relic<AncientGuardStance>(),
        ModelDb.Relic<EmergencyOilCan>(),
        ModelDb.Relic<ConstructHelmet>(),
        ModelDb.Relic<ConstructRippleBasin>(),
        ModelDb.Relic<ConstructLeafyPoultice>(),
        ModelDb.Relic<ConstructLargeCapsule>(),
        ModelDb.Relic<ConstructClockworkSouvenir>()
    ];

    // All three character potions were introduced in 0.5.1.
    private static List<PotionModel> ChangedPotions() => AllPotions();

    private static async Task Replace(Player player, IReadOnlyList<CardModel> templates, IReadOnlyList<RelicModel> relicTemplates,
        IReadOnlyList<PotionModel> potionTemplates)
    {
        player.Deck.Clear(silent: false);

        ICardScope scope = RunManager.Instance.DebugOnlyGetState()!;
        var cards = templates.Select(template => scope.CreateCard(template, player)).ToList();
        if (cards.Count > 0)
            await CardPileCmd.Add(cards, PileType.Deck, CardPilePosition.Bottom, null, skipVisuals: true);

        foreach (var relic in player.Relics.ToArray())
            player.RemoveRelicInternal(relic, silent: false);

        foreach (var template in relicTemplates)
        {
            var relic = template.ToMutable();
            relic.FloorAddedToDeck = player.RunState.TotalFloor;
            player.AddRelicInternal(relic, -1, silent: false);
        }

        // AddRelicInternal fires RelicObtained, which the inventory answers by hiding
        // the new icon (Modulate alpha 0) until a pickup animation fades it back in.
        // pcdeck plays no pickup animation, so restore full opacity directly; without
        // this every showcased relic stays invisible.
        if (LocalContext.IsMe(player) && NRun.Instance?.GlobalUi.RelicInventory is { } inventory)
        {
            foreach (var holder in inventory.RelicNodes)
            {
                if (holder.Relic?.Icon is not { } icon) continue;
                var modulate = icon.Modulate;
                icon.Modulate = new Color(modulate.R, modulate.G, modulate.B, 1f);
            }
        }

        foreach (var potion in player.Potions.ToArray())
            player.DiscardPotionInternal(potion, silent: false);

        foreach (var template in potionTemplates)
            player.AddPotionInternal(template.ToMutable(), -1, silent: false);
    }
}
