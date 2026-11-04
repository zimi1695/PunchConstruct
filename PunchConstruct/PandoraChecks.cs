using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task PandoraChecks()
    {
        foreach (bool protectedWave in new[] { false, true })
        {
            var run = await ReliabilityRun("PANDORA" + protectedWave);
            var player = run.Players.Single();
            var original = player.Deck.Cards.ToArray();
            var waves = original.OfType<SmallWave>().ToArray();
            Check(waves.Length == 5, "Pandora fixture uses the real five-wave starting deck");
            if (protectedWave) PersistentCardKeywords.MakeEternal(waves[0]);
            var targets = waves.Where(c => c.IsTransformable && c.IsRemovable).ToArray();
            var expectedPreserved = original.Except(targets).ToArray();
            var relic = await RelicCmd.Obtain<PandorasBox>(player);
            var replacements = player.Deck.Cards.Except(original).ToArray();
            Check(targets.Length == (protectedWave ? 4 : 5) && targets.All(c => !player.Deck.Cards.Contains(c)) &&
                replacements.Length == targets.Length && player.Deck.Cards.Count == 8,
                "Actually obtaining native Pandora transforms exactly the eligible starting Small Waves; protected=" + protectedWave);
            Check(expectedPreserved.All(c => player.Deck.Cards.Contains(c)),
                "Pandora preserves Iron Wave, Tony, Shock and any protected Small Wave");
            Check(replacements.All(c => CardAcquisition.Eligible(player, c) && c.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare) &&
                replacements.All(c => !BoxingGeneration.Restricted(c)), "Pandora replacements obey the Construct pool and fixed-source exclusions");
            Check(relic.DynamicDescription.GetFormattedText().Contains("小斩波"), "Pandora's actual inventory description includes the character's starting card");
            var preview = Descendants(_tree.Root).OfType<NCardsViewScreen>().FirstOrDefault(s => s.IsVisibleInTree());
            Check(preview != null, "Native Pandora result preview still opens");
            AccessTools.Method(typeof(NCardsViewScreen), "OnReturnButtonPressed").Invoke(preview, [null]);
            await Frames(15);
            await NGame.Instance!.ReturnToMainMenu();
            SaveManager.Instance.DeleteCurrentRun();
        }
        var nativeRun = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "NATIVEPANDORA", GameMode.Standard);
        var nativePlayer = nativeRun.Players.Single();
        var nativeOriginal = nativePlayer.Deck.Cards.ToArray();
        var nativeTargets = nativeOriginal.Where(c => c.IsBasicStrikeOrDefend && c.IsRemovable).ToArray();
        var nativeSmall = nativeRun.CreateCard<SmallWave>(nativePlayer);
        await CardPileCmd.Add(nativeSmall, PileType.Deck, skipVisuals: true);
        await RelicCmd.Obtain<PandorasBox>(nativePlayer);
        Check(nativeTargets.Length > 0 && nativeTargets.All(c => !nativePlayer.Deck.Cards.Contains(c)) && nativePlayer.Deck.Cards.Contains(nativeSmall),
            "Native Ironclad Pandora keeps transforming Strike/Defend and does not gain the Construct's Small Wave rule");
        var nativePreview = Descendants(_tree.Root).OfType<NCardsViewScreen>().FirstOrDefault(s => s.IsVisibleInTree());
        Check(nativePreview != null, "Native character keeps Pandora's original result preview");
        AccessTools.Method(typeof(NCardsViewScreen), "OnReturnButtonPressed").Invoke(nativePreview, [null]);
        await Frames(15);
        await NGame.Instance.ReturnToMainMenu();
        SaveManager.Instance.DeleteCurrentRun();
    }
}
