using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    // Covers native enchantments that can actually be applied to Small Wave.
    // This explicit QA mode speeds up previews only; it never traverses the map.
    private static async Task SoupChecks()
    {
        double previousSpeed = Engine.TimeScale;
        try
        {
            Engine.TimeScale = 8;
            var run = await ReliabilityRun("SOUPMATRIX");
            var player = run.Players.Single();
            await MegaCrit.Sts2.Core.Runs.RunManager.Instance.EnterRoomDebug(RoomType.Event,
                model: ModelDb.Event<DollRoom>(), showTransition: false);
            var eligible = ModelDb.DebugEnchantments
                .Where(e => e.GetType().Namespace == "MegaCrit.Sts2.Core.Models.Enchantments" &&
                    e.GetType().Name != "DeprecatedEnchantment" && e.CanEnchant(run.CreateCard<SmallWave>(player)))
                .OrderBy(e => e.Id.Entry).ToArray();
            Check(eligible.Length > 5, "Soup matrix expands beyond the five previously checked native enchantments");
            var expected = new List<(CardModel Original, string Name, bool Upgraded,
                ModelId Enchantment, int Amount, HashSet<CardKeyword> Keywords, decimal Damage, decimal Block, int Cost)>();
            foreach (var enchantment in eligible)
            foreach (bool upgraded in new[] { false, true })
            {
                var card = run.CreateCard<SmallWave>(player);
                if (upgraded) CardCmd.Upgrade(card);
                CardCmd.Enchant(enchantment.ToMutable(), card, 5);
                // The upgraded half also exercises Soup's forced replacement of protected cards.
                if (upgraded) PersistentCardKeywords.MakeEternal(card);
                await CardPileCmd.Add(card, PileType.Deck, skipVisuals: true);
                expected.Add((card, enchantment.Id.Entry, upgraded,
                    card.Enchantment!.Id, card.Enchantment.Amount, card.Keywords.ToHashSet(),
                    card.Enchantment.EnchantDamageAdditive(0, ValueProp.Move), card.Enchantment.EnchantBlockAdditive(0), card.EnergyCost.GetWithModifiers(CostModifiers.None)));
            }
            var scorching = run.CreateCard<ScorchingIronWave>(player);
            CardCmd.Upgrade(scorching); CardCmd.Upgrade(scorching);
            await CardPileCmd.Add(scorching, PileType.Deck, skipVisuals: true);
            var unenchantedPlus = run.CreateCard<SmallWave>(player);
            CardCmd.Upgrade(unenchantedPlus);
            await CardPileCmd.Add(unenchantedPlus, PileType.Deck, skipVisuals: true);
            var eternalSmall = run.CreateCard<SmallWave>(player);
            eternalSmall.AddKeyword(CardKeyword.Eternal);
            await CardPileCmd.Add(eternalSmall, PileType.Deck, skipVisuals: true);
            var unenchanted = player.Deck.Cards.Where(c => c is SmallWave && c.Enchantment == null).ToArray();
            var unchanged = player.Deck.Cards.Where(c => c is not SmallWave).ToArray();
            int before = player.Deck.Cards.Count;
            var soup = await RelicCmd.Obtain<NutritiousSoup>(player);
            var tips = (IEnumerable<MegaCrit.Sts2.Core.HoverTips.IHoverTip>)HarmonyLib.AccessTools.PropertyGetter(typeof(NutritiousSoup), "ExtraHoverTips").Invoke(soup, [])!;
            Check(tips.Count() == 2 && soup.DynamicDescription.GetFormattedText().Contains("拾起时") && soup.DynamicDescription.GetFormattedText().Contains("小斩波"),
                "Adapted soup uses the native pickup sentence style and shows Eternal/Iron Wave tips instead of native extra-damage enchantment");
            Check(player.Deck.Cards.Count == before && player.Deck.Cards.OfType<SmallWave>().Count() == expected.Count &&
                unenchanted.All(c => !player.Deck.Cards.Contains(c)) &&
                unchanged.All(player.Deck.Cards.Contains) && SoupWavePatch.Transforming.Count == 0,
                "Soup replaces only unenchanted Small Waves, skips all enchanted waves and clears its exception");
            Check(player.Deck.Cards.Contains(scorching) && scorching.CurrentUpgradeLevel == 2 &&
                scorching.DynamicVars.Damage.BaseValue == 10 && scorching.DynamicVars.Block.BaseValue == 10 &&
                scorching.EnergyCost.GetWithModifiers(CostModifiers.None) == 1 && !scorching.Keywords.Contains(CardKeyword.Eternal),
                "Soup leaves existing Scorching Iron Wave unchanged, including upgrades and one-cost non-Eternal state");
            var replacements = player.Deck.Cards.OfType<IronWave>().Where(c => PersistentCardKeywords.SoupEternal[c]).ToArray();
            Check(replacements.Length == unenchanted.Length && replacements.All(c => c.Enchantment == null &&
                c.Keywords.Contains(CardKeyword.Eternal) && c.EnergyCost.GetWithModifiers(CostModifiers.None) == 0 &&
                !c.IsRemovable && !c.IsTransformable) && replacements.Count(c => c.IsUpgraded) == 1,
                "Unenchanted normal, upgraded and already-Eternal sources become zero-cost Eternal Iron Waves with upgrades preserved");
            foreach (var row in expected)
            {
                var card = row.Original;
                Check(player.Deck.Cards.Contains(card), "Soup leaves this exact enchanted source in its deck: " + row.Name);
                var clone = run.CloneCard(card);
                var restored = run.LoadCard(card.ToSerializable(), player);
                foreach (var (candidate, path) in new[] { (card, "original"), (clone, "clone"), (restored, "native serialization") })
                {
                    // Native Eternal removal restrictions apply in the permanent deck.
                    if (candidate.Pile == null) await CardPileCmd.Add(candidate, PileType.Deck, skipVisuals: true);
                    Check(candidate is SmallWave && candidate.IsUpgraded == row.Upgraded &&
                        candidate.Enchantment?.Id == row.Enchantment && candidate.Enchantment.Amount == row.Amount &&
                        candidate.Keywords.SetEquals(row.Keywords) &&
                        candidate.EnergyCost.GetWithModifiers(CostModifiers.None) == row.Cost &&
                        candidate.Enchantment.EnchantDamageAdditive(0, ValueProp.Move) == row.Damage &&
                        candidate.Enchantment.EnchantBlockAdditive(0) == row.Block &&
                        (ReferenceEquals(candidate, row.Original) || !ReferenceEquals(candidate.Enchantment, row.Original.Enchantment)),
                        $"Soup skips the enchanted Small Wave and preserves its existing state through {path}: {row.Name}, upgraded={row.Upgraded}");
                }
            }
            Check(SoupWavePatch.Transforming.Count == 0 && expected.All(row => player.Deck.Cards.Contains(row.Original)),
                "Every already-enchanted Small Wave remains in place, including Spiral and existing Eternal keywords");
            GD.Print($"[Construct SOUP COVERAGE] {eligible.Length} enchantments, {expected.Count} normal/upgraded cases; upgraded sources were already Eternal");
            await NGame.Instance!.ReturnToMainMenu();
            SaveManager.Instance.DeleteCurrentRun();
        }
        finally { Engine.TimeScale = previousSpeed; }
    }
}
