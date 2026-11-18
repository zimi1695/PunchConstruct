using Godot;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    // Exercises the debug-only recording helper directly. The helper bypasses
    // relic acquisition effects on purpose, so the counts must stay exactly at
    // the selected set even when Large Capsule / Pandora's Box are included.
    private static async Task ShowcaseChecks(Player player)
    {
        var command = new ShowcaseConsoleCmd();

        // The recording script is typed into the in-game console, so verify the
        // mod command is actually registered there rather than only callable
        // through a direct reference.
        var console = new DevConsole(shouldAllowDebugCommands: true);
        var help = console.ProcessCommand("help pcdeck");
        Check(help.success && help.msg.Contains("pcdeck"), "Showcase command is registered in the dev console");

        var changes = command.Process(player, ["changes"]);
        Check(changes.success && changes.task != null, "Showcase changes command is accepted");
        await changes.task!;
        Check(player.Deck.Cards.Count == 22 && player.Relics.Count == 7 && player.Potions.Count() == 3,
            "Showcase changes sets twenty-two cards, seven relics and three potions");
        Check(player.Deck.Cards.Any(card => card is LeftHook) &&
              player.Relics.Any(relic => relic is ConstructClockworkSouvenir),
            "Showcase changes includes Left Hook and Clockwork Souvenir");
        Check(player.Deck.Cards.Any(card => card is HobblingShot) && player.Deck.Cards.Any(card => card is CoveringFire),
            "Showcase changes includes the Fool imports");
        Check(player.Potions.All(potion => ModelDb.PotionPool<ConstructPotionPool>().AllPotions.Any(p => p.Id == potion.Id)),
            "Showcase changes replaces potions with the construct potions");
        Check(player.Deck.Cards.All(card => !card.IsUpgraded),
            "Showcase changes does not apply acquisition upgrades");

        int allCards = ModelDb.CardPool<ConstructCardPool>().AllCards.DistinctBy(card => card.Id).Count();
        int allRelics = ModelDb.RelicPool<ConstructRelicPool>().AllRelics.DistinctBy(relic => relic.Id).Count();
        int allPotions = ModelDb.PotionPool<ConstructPotionPool>().AllPotions.DistinctBy(potion => potion.Id).Count();
        GD.Print($"[Construct SHOWCASE] all cards={allCards}; all relics={allRelics}; all potions={allPotions}");

        var all = command.Process(player, ["all"]);
        Check(all.success && all.task != null, "Showcase all command is accepted");
        await all.task!;
        Check(player.Deck.Cards.Count == allCards && player.Relics.Count == allRelics && player.Potions.Count() == allPotions,
            "Showcase all matches the construct pool counts");
        Check(player.Deck.Cards.All(card => !card.IsUpgraded),
            "Showcase all bypasses Neow's Talisman upgrades");

        // The inventory hides a freshly obtained icon until a pickup animation fades
        // it in; pcdeck plays no animation, so it must restore opacity itself. Without
        // the fix every relic icon stays at Modulate alpha 0 (invisible/black).
        var inventory = NRun.Instance?.GlobalUi.RelicInventory;
        Check(inventory != null && inventory.RelicNodes.Count == allRelics &&
              inventory.RelicNodes.All(holder => holder.Relic?.Icon != null && holder.Relic.Icon.Modulate.A >= 1f),
            "Showcase all leaves every relic icon fully opaque in the live inventory");

        // "mod" showcases only mod-authored content: the 26 original-design cards
        // (custom art/ portraits minus Core Surge / Panacea; Iron Wave is vanilla and
        // stays out), the seven original relics plus the two silver recolours (Ruined
        // Helmet, Helical Dart), and the three character potions. Other shared-relic
        // variants, observer imports, Fool imports and vanilla-art cards stay out.
        var mod = command.Process(player, ["mod"]);
        Check(mod.success && mod.task != null, "Showcase mod command is accepted");
        await mod.task!;
        GD.Print($"[Construct SHOWCASE] mod cards={player.Deck.Cards.Count}; mod relics={player.Relics.Count}; mod potions={player.Potions.Count()}");
        Check(player.Deck.Cards.Count == 26 && player.Relics.Count == 9 && player.Potions.Count() == 3,
            "Showcase mod sets twenty-six original cards, nine relics and three potions");
        Check(player.Relics.All(relic => relic.PackedIconPath.StartsWith("res://PunchConstruct/art/") || relic is ConstructHelmet or ConstructDart),
            "Showcase mod relics are original art or the two silver recolours");
        Check(player.Relics.Any(relic => relic is GuardStance) && player.Relics.Any(relic => relic is AncientGuardStance) &&
              player.Relics.Any(relic => relic is SpinningScrew) && player.Relics.Any(relic => relic is ConstructClockworkSouvenir) &&
              player.Relics.Any(relic => relic is ConstructHelmet) && player.Relics.Any(relic => relic is ConstructDart),
            "Showcase mod includes the original relics and the helmet/dart recolours");
        Check(player.Relics.All(relic => relic is not ConstructRippleBasin and not ConstructLargeCapsule and
              not ConstructLeafyPoultice and not ConstructSeaGlass),
            "Showcase mod excludes the other shared-relic variants");
        Check(player.Deck.Cards.Any(card => card is SmallWave) && player.Deck.Cards.Any(card => card is ScorchingIronWave) &&
              player.Deck.Cards.Any(card => card is LeftHook),
            "Showcase mod includes Small Wave, Scorching Iron Wave and Left Hook");
        Check(player.Deck.Cards.All(card => card is not IronWave and not ConstructCoreSurge and not ConstructPanacea and
              not SmallShockwave and not ObserverSashWhip and not HobblingShot),
            "Showcase mod excludes Iron Wave, Core Surge, Panacea, vanilla-art, observer and Fool cards");
        Check(player.Deck.Cards.All(card => card.PortraitPath.StartsWith("res://PunchConstruct/art/")),
            "Showcase mod cards all resolve to original mod art when owned");
        Check(player.Potions.All(potion => ModelDb.PotionPool<ConstructPotionPool>().AllPotions.Any(p => p.Id == potion.Id)),
            "Showcase mod potions are the construct potions");
        Check(player.Deck.Cards.All(card => !card.IsUpgraded),
            "Showcase mod does not apply acquisition upgrades");
    }
}
