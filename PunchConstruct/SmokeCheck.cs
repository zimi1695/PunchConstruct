using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Potions;
using MegaCrit.Sts2.Core.Models.PotionPools;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Unlocks;

namespace PunchConstruct;

// Explicit opt-in checks, executed inside the real game engine. No effect during normal play.
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class SmokeCheck
{
    private static void Postfix(NMainMenu __instance)
    {
        if (!OS.GetCmdlineArgs().Any(a => a is "--punchconstruct-smoke" or "--punchconstruct-iconcheck" or "--punchconstruct-poolcheck")) return;
        Callable.From(() => { _ = Run(__instance.GetTree()); }).CallDeferred();
    }

    private static async Task Run(SceneTree tree)
    {
        try
        {
            await QaStartupPreload.Wait(tree);
            void Check(bool condition, string description)
            {
                if (!condition) throw new InvalidOperationException(description);
                GD.Print("[PunchConstruct CHECK] " + description);
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-poolcheck"))
            {
                var construct = ModelDb.Character<ConstructCharacter>();
                var pool = construct.CardPool;
                (CardModel Card, CardModel Reference)[] origins = [
                    (ModelDb.Card<ScorchingIronWave>(), ModelDb.Card<IronWave>()),
                    (ModelDb.Card<ForgeWorkshop>(), ModelDb.Card<Bulwark>()),
                    (ModelDb.Card<RoyalBearing>(), ModelDb.Card<Bulwark>()),
                    (ModelDb.Card<StrikeWhileHot>(), ModelDb.Card<Bulwark>()),
                    (ModelDb.Card<FinalJudgment>(), ModelDb.Card<NegativePulse>()),
                    (ModelDb.Card<UnarmToArms>(), ModelDb.Card<Bodyguard>()),
                    (ModelDb.Card<DoomResonance>(), ModelDb.Card<Bodyguard>()),
                    (ModelDb.Card<IronWaveForm>(), ModelDb.Card<Fisticuffs>()),
                    (ModelDb.Card<NeverBackDown>(), ModelDb.Card<Fisticuffs>()),
                    (ModelDb.Card<FightMe>(), ModelDb.Card<Fisticuffs>()),
                    (ModelDb.Card<BattleFervor>(), ModelDb.Card<Fisticuffs>())
                ];
                foreach (var (card, reference) in origins)
                {
                    foreach (var variant in new[] { card, card.ToMutable() })
                    {
                        if (variant.IsMutable) variant.UpgradeInternal();
                        Check(variant.Pool == reference.Pool && variant.VisualCardPool == reference.VisualCardPool,
                            "Design origin retained: " + variant.Title);
                        Check(variant.FrameMaterial != null && variant.FrameMaterial == reference.FrameMaterial &&
                            variant.EnergyIcon != null && variant.EnergyIcon.ResourcePath == reference.EnergyIcon.ResourcePath,
                            "Native frame and energy icon load: " + variant.Title);
                    }
                    Check(pool.AllCardIds.Contains(card.Id) && !reference.Pool.AllCardIds.Contains(card.Id),
                        "Available in construct pool without entering original character rewards: " + card.Title);
                }
                CardModel[] ownCards = [ModelDb.Card<SmallWave>(), ModelDb.Card<TonyWave>(), ModelDb.Card<SmallShockwave>(),
                    ModelDb.Card<BoxingChampionForm>()];
                Check(ownCards.All(c => c.Pool == pool), "Four construct designs, retain their own origin");
                var allCards = pool.AllCards.ToArray();
                Check(allCards.Length == 98 && allCards.Select(c => c.Id).Distinct().Count() == 98,
                    "Acquisition pool contains 98 distinct cards including Scorching Iron Wave");
                string? auditList = OS.GetCmdlineArgs().FirstOrDefault(a => a.StartsWith("--punchconstruct-audit-list="));
                if (auditList != null)
                {
                    var specification = System.IO.File.ReadAllText(auditList.Split('=', 2)[1]);
                    var expected = System.Text.RegularExpressions.Regex.Matches(specification, @"^(\d{2})\s+([^：\n]+)：",
                        System.Text.RegularExpressions.RegexOptions.Multiline)
                        .ToDictionary(m => int.Parse(m.Groups[1].Value), m => m.Groups[2].Value);
                    var actual = allCards.Select(c => c.Title).ToArray();
                    string missing = string.Join(", ", expected.Values.Except(actual));
                    string unexpected = string.Join(", ", actual.Except(expected.Values));
                    GD.Print("[PunchConstruct AUDIT] Missing: " + missing + "; Unexpected: " + unexpected);
                    Check(expected.Count == 98 && actual.Distinct().Count() == 98 && missing.Length == 0 && unexpected.Length == 0,
                        "Exact card roster matches the independent approved 98-card document");
                    foreach (var (number, title) in expected.OrderBy(e => e.Key))
                    {
                        var card = allCards.Single(c => c.Title == title);
                        GD.Print($"[PunchConstruct AUDIT {number:D2}] {card.Title} | {card.Id} | {card.GetType().Name}");
                    }
                }
                Check(allCards.OfType<ObserverColdObservation>().Count() == 1,
                    "Cold Observation included; Empty Mind excluded");
                Check(allCards.OfType<ObserverTalkToTheHand>().Count() == 1 &&
                    allCards.OfType<ObserverSwivel>().Count() == 1,
                    "Talk to the Hand and Swivel included; Halt and Flurry of Blows excluded");
                Check(allCards.Where(c => c.GetType().Assembly != typeof(ConstructCharacter).Assembly).All(c => c.Pool is not ConstructCardPool),
                    "All imported cards retain original ownership");
                var unlocks = new UnlockState([], [], 0);
                var player = Player.CreateForNewRun(construct, unlocks, 9999);
                Check(!CardAcquisition.Eligible(player, ModelDb.Card<WhistlingArrows>()) &&
                    !CardAcquisition.Eligible(player, ModelDb.Card<Nail>()) &&
                    !CardAcquisition.Eligible(player, ModelDb.Card<ConstructChoReady>()) &&
                    CardAcquisition.Eligible(player, ModelDb.Card<HobblingShot>()) &&
                    CardAcquisition.Eligible(player, ModelDb.Card<BlossomShot>()),
                    "Fool group stays locked until Hobbling Shot or Blossom Shot is owned");
                Check(!CardAcquisition.Eligible(player, ModelDb.Card<Enrapture>()) &&
                    !CardAcquisition.Eligible(player, ModelDb.Card<CoveringFire>()),
                    "Multiplayer-only Fool cards never enter singleplayer acquisition");
                // The construct re-authors these three Fool designs so 0.114's
                // balance pass cannot overwrite the values this character shipped
                // with. Assert both faces here, next to the pool roster.
                var foolBarbs = ModelDb.Card<ConstructChoBarbs>().ToMutable();
                Check(foolBarbs.EnergyCost.Canonical == 1 && foolBarbs.DynamicVars.Block.BaseValue == 7 &&
                    foolBarbs.DynamicVars["ChoBarbsPower"].BaseValue == 3,
                    "Cho Barbs keeps its 0.113 values: one energy, seven block, three thorns");
                foolBarbs.UpgradeInternal();
                Check(foolBarbs.DynamicVars.Block.BaseValue == 9 && foolBarbs.DynamicVars["ChoBarbsPower"].BaseValue == 4,
                    "Cho Barbs+ keeps its 0.113 upgrade: nine block, four thorns");
                var foolReady = ModelDb.Card<ConstructChoReady>().ToMutable();
                Check(foolReady.EnergyCost.Canonical == 1 && foolReady.DynamicVars.Block.BaseValue == 8 &&
                    foolReady.DynamicVars["AimPower"].BaseValue == 3,
                    "Cho Ready keeps its 0.113 values: one energy, eight block, three aim");
                foolReady.UpgradeInternal();
                Check(foolReady.DynamicVars.Block.BaseValue == 11 && foolReady.DynamicVars["AimPower"].BaseValue == 5,
                    "Cho Ready+ keeps its 0.113 upgrade: eleven block, five aim");
                var foolKi = ModelDb.Card<ConstructWahChoKi>().ToMutable();
                Check(foolKi.EnergyCost.Canonical == 1 && foolKi.DynamicVars.Damage.BaseValue == 9 &&
                    foolKi.DynamicVars.Block.BaseValue == 9 && foolKi.DynamicVars.Cards.BaseValue == 1,
                    "Wah Cho Ki keeps its 0.113 values: one energy, nine damage, nine block, one draw");
                foolKi.UpgradeInternal();
                Check(foolKi.DynamicVars.Damage.BaseValue == 12 && foolKi.DynamicVars.Block.BaseValue == 12 &&
                    foolKi.DynamicVars.Cards.BaseValue == 2,
                    "Wah Cho Ki+ keeps its 0.113 upgrade: twelve damage, twelve block, two draw");
                Check(ModelDb.Card<ConstructChoBarbs>().VisualCardPool is FoolCardPool &&
                    ModelDb.Card<ConstructChoReady>().VisualCardPool is FoolCardPool &&
                    ModelDb.Card<ConstructWahChoKi>().VisualCardPool is FoolCardPool &&
                    ModelDb.CardPool<ConstructCardPool>().AllCardIds.Contains(ModelDb.Card<ConstructChoBarbs>().Id) &&
                    ModelDb.CardPool<ConstructCardPool>().AllCardIds.Contains(ModelDb.Card<ConstructChoReady>().Id) &&
                    ModelDb.CardPool<ConstructCardPool>().AllCardIds.Contains(ModelDb.Card<ConstructWahChoKi>().Id),
                    "Re-authored Fool cards use the Fool frame but stay in the construct acquisition pool");
                player.Deck.AddInternal(ModelDb.Card<HobblingShot>().ToMutable(), -1, false);
                Check(CardAcquisition.Eligible(player, ModelDb.Card<WhistlingArrows>()) &&
                    CardAcquisition.Eligible(player, ModelDb.Card<Nail>()) &&
                    CardAcquisition.Eligible(player, ModelDb.Card<ConstructChoReady>()) &&
                    CardAcquisition.Weight(player, ModelDb.Card<WhistlingArrows>()) == CardAcquisition.GroupMultiplier,
                    "Owning Hobbling Shot unlocks the Fool group and doubles its draw weight");
                var previousScope = CardFactoryScope.Player;
                try
                {
                    // Canonical card rarity must be read in the construct's factory
                    // scope, including the Bodyguard and ancient-card adaptations.
                    CardFactoryScope.Player = player;
                    foreach (var group in allCards.GroupBy(c => c.Rarity).OrderBy(g => g.Key))
                        GD.Print($"[PunchConstruct RARITY] {group.Key} | {group.Count()} | {string.Join("、", group.Select(c => c.Title))}");
                    CardFactoryScope.Player = null;
                    foreach (var nativePool in new CardPoolModel[] { ModelDb.CardPool<IroncladCardPool>(), ModelDb.CardPool<SilentCardPool>(),
                        ModelDb.CardPool<RegentCardPool>(), ModelDb.CardPool<NecrobinderCardPool>(), ModelDb.CardPool<DefectCardPool>() })
                    {
                        var regular = nativePool.AllCards.Where(c => c.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare);
                        GD.Print($"[PunchConstruct RARITY BASELINE] {nativePool.GetType().Name} | " +
                            string.Join(" | ", regular.GroupBy(c => c.Rarity).OrderBy(g => g.Key).Select(g => $"{g.Key}={g.Count()}")));
                    }
                }
                finally { CardFactoryScope.Player = previousScope; }
                var potions = PotionFactory.GetPotionOptions(player).ToArray();
                var shared = ModelDb.PotionPool<SharedPotionPool>().GetUnlockedPotions(unlocks).Select(p => p.Id).ToHashSet();
                var exclusive = construct.PotionPool.AllPotions.Select(p => p.Id).ToHashSet();
                Check(construct.PotionPool is ConstructPotionPool && exclusive.Count == 3 &&
                    exclusive.SetEquals(potions.Select(p => p.Id).Except(shared)) &&
                    shared.IsSubsetOf(potions.Select(p => p.Id)) && !potions.OfType<FocusPotion>().Any(),
                    "Actual potion factory uses shared potions plus three construct exclusives without Defect exclusives");
                Check(ModelDb.Character<Defect>().PotionPool is DefectPotionPool &&
                    ModelDb.Character<Regent>().CardPool is RegentCardPool && ModelDb.Character<Necrobinder>().CardPool is NecrobinderCardPool,
                    "Original character pool assignments remain unchanged");
                GD.Print("[PunchConstruct POOL PASS] Origins, frame resources, acquisition isolation and potion factory; no run or combat started");
                tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-iconcheck"))
            {
                PowerModel[] powers = [ModelDb.Power<DelayedWavePower>(), ModelDb.Power<IronWaveFormPower>(),
                    ModelDb.Power<ForgeWorkshopPower>(), ModelDb.Power<RoyalBearingPower>(), ModelDb.Power<StrikeWhileHotPower>(),
                    ModelDb.Power<FinalJudgmentPower>(), ModelDb.Power<UnarmToArmsPower>(), ModelDb.Power<NeverBackDownPower>(), ModelDb.Power<BoxingChampionFormPower>(),
                    ModelDb.Power<ClockworkMechanismPower>(),
                    ModelDb.Power<ConstructDomineeringPower>()];
                foreach (var power in powers)
                {
                    var icon = ResourceLoader.Load<Texture2D>(ConstructPowerIcons.Packed(power.GetType()));
                    var big = ResourceLoader.Load<Texture2D>(ConstructPowerIcons.Big(power.GetType()));
                    using var image = icon.GetImage();
                    using var bigImage = big.GetImage();
                    var used = image.GetUsedRect().Size;
                    var bigUsed = bigImage.GetUsedRect().Size;
                    int extent = power is ClockworkMechanismPower ? 60 : 62;
                    Check(power.PackedIconPath == ConstructPowerIcons.Packed(power.GetType()) && icon.GetSize() == new Vector2(64,64) && Math.Max(used.X, used.Y) == extent,
                        "Status icon loads at 64x64 with its fitted visible extent: " + power.GetType().Name);
                    Check(big.GetSize() == new Vector2(256,256) && Math.Max(bigUsed.X, bigUsed.Y) == extent * 4,
                        "Large pixel icon loads at 256x256 with matching visible extent: " + power.GetType().Name);
                }
                Check(powers.Select(p => p.PackedIconPath).Distinct().Count() == 10, "Ten powers have distinct adopted icons");
                GD.Print("[PunchConstruct ICON PASS] 23 resource checks; no run or combat started");
                tree.Quit();
                return;
            }
            var character = ModelDb.Character<ConstructCharacter>();
            Check(ModelDb.AllCharacters.Contains(character), "Character registered in selectable roster");
            var deck = character.StartingDeck.ToArray();
            Check(deck.Length == 8 && deck.Count(c => c is SmallWave) == 5 && deck.Count(c => c is IronWave) == 1 && deck.Count(c => c is TonyWave) == 1 && deck.Count(c => c is SmallShockwave) == 1, "Starting deck: 5 + 1 + 1 + 1");
            Check(character.StartingRelics.Single() is GuardStance, "Guard Stance is the starting relic");
            var small = ModelDb.Card<SmallWave>().ToMutable();
            small.UpgradeInternal();
            Check(small.DynamicVars.Damage.BaseValue == 5 && small.DynamicVars.Block.BaseValue == 5, "Small Wave upgrades to 5 damage / 5 block");
            var tony = ModelDb.Card<TonyWave>().ToMutable();
            tony.UpgradeInternal();
            Check(tony.DynamicVars.Damage.BaseValue == 9 && tony.DynamicVars.Block.BaseValue == 9, "Tony Wave upgrades to 9 / 9");
            var shockwave = ModelDb.Card<SmallShockwave>().ToMutable();
            Check(shockwave.Keywords.Contains(CardKeyword.Exhaust), "Small Shockwave initially exhausts");
            shockwave.UpgradeInternal();
            Check(shockwave.Keywords.Contains(CardKeyword.Exhaust) && shockwave.DynamicVars["Debuff"].BaseValue == 2 && shockwave.TargetType == TargetType.AllEnemies,
                "Shock+ keeps exhaust, targets all enemies and applies two stacks");
            var form = ModelDb.Card<IronWaveForm>().ToMutable();
            Check(form.DynamicVars["Amount"].BaseValue == 1, "Iron Wave Form initially grants one trigger per turn");
            form.UpgradeInternal();
            Check(form.DynamicVars["Amount"].BaseValue == 2 && !form.Keywords.Contains(CardKeyword.Retain), "Iron Wave Form upgrades to two triggers without retain");
            Check(ModelDb.Card<IronWave>().DynamicVars.Damage.BaseValue == 5, "Vanilla Iron Wave remains unchanged");
            var iron = ModelDb.Card<IronWave>().ToMutable();
            iron.UpgradeInternal();
            Check(iron.DynamicVars.Damage.BaseValue == 7 && iron.DynamicVars.Block.BaseValue == 7, "Iron Wave upgrades normally to 7 / 7");
            Check(ModelDb.Card<Shockwave>().Pool is not ConstructCardPool, "Vanilla card pool ownership remains unchanged");
            Check(character.CardPool.AllCards.Count() == 98, "All 98 roster cards are registered, including Scorching Iron Wave");
            Check(character.StartingHp == 75 && character.BaseOrbSlotCount == 0, "Confirmed 75 HP and zero starting orb slots");
            foreach (var path in character.AssetPaths.Concat(character.AssetPathsCharacterSelect).Distinct())
                Check(ResourceLoader.Exists(path), "Resource exists: " + path);
            foreach (var card in new CardModel[] { small, tony, shockwave, form })
                Check(card.Portrait != null && !card.Title.Contains(".title"), "Localized title and portrait: " + card.Title);
            foreach (var model in ModelDb.AllAncients)
            {
                var set = model.DialogueSet;
                Check(set.CharacterDialogues.TryGetValue(character.Id.Entry, out var lines) && lines.Count == 3 &&
                    lines.All(d => d.Lines.Count == 3 && d.Lines.All(l => l.LineText?.Exists() == true) &&
                        d.Lines.Take(2).All(l => l.NextButtonText?.Exists() == true)),
                    "Three localized construct exchanges have usable continue buttons: " + model.Id.Entry);
                var first = set.GetValidDialogues(character.Id, 0, 0, true).ToArray();
                Check(first.Length > 0 && first.All(ConstructAncientDialogueSelection.IsConstructDialogue),
                    "Construct first meeting shows only its own exchange: " + model.Id.Entry);
                if (model.Id.Entry == "THE_ARCHITECT")
                {
                    var sequel = set.GetValidDialogues(character.Id, 4, 4, false).ToArray();
                    Check(sequel.Length == 3 && sequel.All(ConstructAncientDialogueSelection.IsConstructDialogue),
                        "Architect repeat meetings stay on construct exchanges");
                    continue;
                }
                if (!ConstructAncientDialogueSelection.Weights.TryGetValue(model.Id.Entry, out var expected))
                {
                    Check(false, "Construct dialogue weights cover " + model.Id.Entry);
                    continue;
                }
                var repeat = set.GetValidDialogues(character.Id, 4, 4, true).ToArray();
                var counts = new int[3];
                foreach (var dialogue in repeat)
                {
                    int index = ConstructAncientDialogueSelection.Index(dialogue);
                    if (index >= 0 && index < counts.Length) counts[index]++;
                }
                Check(repeat.All(ConstructAncientDialogueSelection.IsConstructDialogue) && counts.SequenceEqual(expected)
                    && repeat.Length == expected.Sum(),
                    "Construct repeat meetings follow authored dialogue weights: " + model.Id.Entry);
            }
            using var visuals = character.CreateVisuals();
            Check(visuals != null, "Combat robot scene instantiates");
            using var select = ResourceLoader.Load<PackedScene>(character.CharacterSelectBg).Instantiate();
            Check(select != null, "Character selection scene instantiates");
            GD.Print("[PunchConstruct SMOKE DLL] " + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(typeof(SmokeCheck).Assembly.Location))));
            GD.Print("[PunchConstruct SMOKE PASS]");
            tree.Quit();
        }
        catch (Exception e)
        {
            GD.Print("[PunchConstruct SMOKE FAIL] " + e);
            tree.Quit(1);
        }
    }
}
