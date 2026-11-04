using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task AdaptationChecks()
    {
        var run = await ReliabilityRun("SICEMANDRUG");
        var player = run.Players.Single();
        var abrasive = run.CreateCard<Abrasive>(player);
        Check(abrasive.EnergyCost.GetWithModifiers(CostModifiers.None) == 2,
            "Construct Abrasive has a two-energy base cost");
        abrasive.UpgradeInternal(); abrasive.FinalizeUpgradeInternal();
        Check(abrasive.EnergyCost.GetWithModifiers(CostModifiers.None) == 2 &&
            abrasive.DynamicVars["ThornsPower"].BaseValue == 6,
            "Upgraded Abrasive stays two energy and preserves native upgrade");
        var abrasiveClone = (Abrasive)abrasive.MutableClone();
        abrasiveClone.DowngradeInternal(); abrasiveClone.FinalizeUpgradeInternal();
        Check(abrasiveClone.EnergyCost.GetWithModifiers(CostModifiers.None) == 2 &&
            abrasiveClone.DynamicVars["ThornsPower"].BaseValue == 4,
            "Abrasive cloning and downgrade retain the owned cost adaptation");
        var silent = Player.CreateForNewRun<Silent>(new UnlockState([], [], 0), 992);
        var nativeAbrasive = run.CreateCard<Abrasive>(silent);
        Check(nativeAbrasive.EnergyCost.GetWithModifiers(CostModifiers.None) == 3 &&
            ModelDb.Card<Abrasive>().EnergyCost.Canonical == 3,
            "Silent and canonical Abrasive retain the native three-energy cost");
        Check(CardAcquisition.Eligible(player, ModelDb.Card<SicEm>()) &&
            !CardAcquisition.Eligible(player, ModelDb.Card<Unleash>()) && !CardAcquisition.Eligible(player, ModelDb.Card<BoneShards>()),
            "Sic Em is available without Bodyguard; Unleash and Bone Shards keep their prerequisites");

        var options = CardCreationOptions.ForNonCombatWithUniformOdds([player.Character.CardPool], c => c is FightMe or Uppercut)
            .WithFlags(CardCreationFlags.IsCardReward).WithRngOverride(new Rng(12345));
        int Sample(CardCreationOptions value) => Enumerable.Range(0, 1200)
            .Count(_ => CardFactory.CreateForReward(player, 1, value).Single().Card is FightMe);
        int baseline = Sample(options);
        Check(baseline is > 480 and < 720, "Without rug, two same-rarity reward candidates have equal weight");
        var rug = await RelicCmd.Obtain<DingyRug>(player);
        Check(rug.DynamicDescription.GetFormattedText().Contains("概率大幅提高"), "Owned rug shows the new probability description");
        Check(rug.ModifyCardRewardCreationOptions(player, options).CardPools.SequenceEqual(options.CardPools),
            "Construct rug leaves the already-selected card pools intact");
        int boosted = Sample(options);
        GD.Print($"[Construct RUG SAMPLE] 1200 same-rarity reward rolls: without={baseline}; with={boosted}; colorless weight={ConstructDingyRug.ColorlessMultiplier}");
        Check(boosted is > 900 and < 1100 && boosted > baseline + 250, "Actual native reward generation substantially favours colorless cards with rug");
        Check(Sample(options.WithFlags(CardCreationFlags.NoCardPoolModifications)) is > 480 and < 720,
            "Explicit no-pool-modification rewards bypass the rug");
        Check(Sample(options.WithFlags(CardCreationFlags.NoModifyHooks)) is > 480 and < 720,
            "Explicit no-hook rewards bypass the rug");
        Check(Sample(CardCreationOptions.ForNonCombatWithUniformOdds([player.Character.CardPool], c => c is FightMe or Uppercut)) is > 480 and < 720,
            "Non-reward random card creation does not inherit the rug's reward weight");
        for (int i = 0; i < 20; i++)
        {
            var reward = CardFactory.CreateForReward(player, 3, CardCreationOptions.ForRoom(player, RoomType.Monster)
                .WithFlags(CardCreationFlags.IsCardReward)).ToArray();
            Check(reward.Length == 3 && reward.Select(c => c.Card.Id).Distinct().Count() == 3 &&
                reward.All(c => CardAcquisition.Eligible(player, c.Card)),
                "Rug keeps actual three-card rewards distinct and within the selected gated pool: " + i);
        }
        Check(!CardFactoryScope.RugReward && CardFactoryScope.Player == null, "Reward weighting scope is restored after native generation");
        await RelicCmd.Remove(rug);
        Check(Sample(options) is > 480 and < 720, "Removing the rug restores the native reward distribution");

        var native = Player.CreateForNewRun<Ironclad>(new UnlockState([], [], 0), 991);
        _ = RunState.CreateForTest([native]);
        var nativeRug = ModelDb.Relic<DingyRug>().ToMutable(); native.AddRelicInternal(nativeRug);
        var nativeOptions = CardCreationOptions.ForNonCombatWithUniformOdds([native.Character.CardPool], c => c is Uppercut or Fisticuffs)
            .WithFlags(CardCreationFlags.IsCardReward);
        var expanded = nativeRug.ModifyCardRewardCreationOptions(native, nativeOptions);
        Check(expanded.CardPools.Any(p => p is ColorlessCardPool) && !nativeRug.DynamicDescription.GetFormattedText().Contains("概率大幅提高"),
            "Other characters retain the native rug pool expansion and description");
        int nativeColorless = Enumerable.Range(0, 1200).Count(_ => CardFactory.CreateForReward(native, 1, expanded).Single().Card is Fisticuffs);
        Check(nativeColorless is > 480 and < 720, "Other characters do not receive the construct rug's fivefold weight");
        await PrismaticGemChecks(player, native);

        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Sic Em test room ready");
        var enemies = player.Creature.CombatState!.HittableEnemies.ToArray();
        foreach (var enemy in enemies)
        {
            await CreatureCmd.SetMaxAndCurrentHp(enemy, 1000);
            foreach (var power in enemy.Powers.ToArray()) await PowerCmd.Remove(power);
        }
        var target = enemies[0];
        Check(player.IsOstyMissing, "Fixture begins without a living Osty");
        var hp = target.CurrentHp;
        var card = await Play<SicEm>(player, target);
        Check(target.CurrentHp == hp - 5 && target.GetPowerAmount<ConstructSicEmPower>() == 3 && player.IsOstyMissing,
            "Without Osty, Sic Em deals five player damage and then applies its three-summon mark");
        Check(!card.ShouldGlowRed && !card.Tags.Contains(CardTag.OstyAttack) && card.Description.GetRawText().Contains("你或奥斯提"),
            "Adapted card has no missing-Osty warning or attack tag and accurately lists both mark triggers");
        var markTip = target.GetPower<ConstructSicEmPower>()!.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description;
        Check(markTip.Contains("召唤3") && markTip.Contains("你或奥斯提") && !markTip.Contains("{Amount}"),
            "Actual enemy mark also shows the adapted trigger description");
        await Play<SmallWave>(player, target);
        Check(player.Osty?.CurrentHp == 3, "A player attack summons Osty from zero without an Osty attack prerequisite");
        int summoned = player.Osty!.CurrentHp;
        await Play<FightMe>(player, target);
        Check(player.Osty.CurrentHp == summoned + 6, "Fight Me's two player hits summon twice");
        summoned = player.Osty.CurrentHp;
        await Play<SmallWave>(player, enemies[1]);
        Check(player.Osty.CurrentHp == summoned, "An unmarked enemy does not trigger the mark");
        hp = target.CurrentHp;
        await Play<SicEm>(player, target, upgraded: true);
        Check(target.CurrentHp == hp - 6 && target.GetPowerAmount<ConstructSicEmPower>() == 7 && player.Osty.CurrentHp == summoned + 3,
            "Sic Em+ deals six, triggers the existing mark, then stacks four summon for subsequent hits");
        await PowerCmd.Apply<StrengthPower>(Context, player.Creature, 2, player.Creature, null);
        await PowerCmd.Apply<StrengthPower>(Context, player.Osty, 9, player.Creature, null);
        card = ((CombatState)player.Creature.CombatState!).CreateCard<SicEm>(player);
        await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
        card.UpdateDynamicVarPreview(CardPreviewMode.Normal, target, card.DynamicVars);
        Check(card.DynamicVars.OstyDamage.PreviewValue == 7, "Damage forecast uses player Strength, not Osty Strength");
        hp = target.CurrentHp;
        summoned = player.Osty.CurrentHp;
        await Play<SicEm>(player, target);
        Check(target.CurrentHp == hp - 7 && player.Osty.CurrentHp == summoned + 7 && target.GetPowerAmount<ConstructSicEmPower>() == 10,
            "Actual damage uses player Strength and leaves native mark stacking and trigger order intact");
        await PowerCmd.Apply<VigorPower>(Context, player.Creature, 4, player.Creature, null);
        card.UpdateDynamicVarPreview(CardPreviewMode.Normal, target, card.DynamicVars);
        Check(card.DynamicVars.OstyDamage.PreviewValue == 11, "Actual hand preview includes both player Strength and Vigor");
        hp = target.CurrentHp;
        Check(card.TryManualPlay(target), "Play the exact Sic Em card used for the hand damage preview");
        await Until(() => card.Pile?.Type == PileType.Discard, "Previewed Sic Em finishes playing");
        await Frames(15);
        Check(target.CurrentHp == hp - 11 && !player.Creature.HasPower<VigorPower>() && target.GetPowerAmount<ConstructSicEmPower>() == 13,
            "The previewed card deals eleven, consumes native Vigor, and then adds its summon mark");
        target.GainBlockInternal(100);
        summoned = player.Osty.CurrentHp;
        await Play<SmallWave>(player, target);
        Check(player.Osty.CurrentHp == summoned + 13, "Fully blocked hits retain the native per-hit summon trigger");
        target.LoseBlockInternal(target.Block);
        var nativeCard = run.CreateCard<SicEm>(native);
        Check(nativeCard.Tags.Contains(CardTag.OstyAttack) && nativeCard.Description.GetRawText().Contains("奥斯提"),
            "Other characters retain the native Sic Em text and Osty attack tag");
        await NextTurn(player, target);
        Check(!target.HasPower<ConstructSicEmPower>(), "Native end-of-turn processing removes the temporary mark");
        summoned = player.Osty.CurrentHp;
        await Play<SmallWave>(player, target);
        Check(player.Osty.CurrentHp == summoned, "An expired mark cannot summon on the following turn");
        await OstyHealthAttackChecks(player, enemies[1], native);
        await CreatureCmd.SetCurrentHp(target, 1);
        await Play<SicEm>(player, target);
        Check(target.IsDead, "A lethal initial hit finishes cleanly without a power on a dead enemy");
        await NGame.Instance!.ReturnToMainMenu();
        SaveManager.Instance.DeleteCurrentRun();
    }

    private static async Task OstyHealthAttackChecks(Player player, MegaCrit.Sts2.Core.Entities.Creatures.Creature target, Player native)
    {
        await PowerCmd.Remove<StrengthPower>(player.Creature);
        await PowerCmd.Remove<VigorPower>(player.Creature);
        if (player.Osty is { IsAlive: true } pet) await CreatureCmd.Kill(pet);
        Check(player.IsOstyMissing, "HP-scaling attacks begin with no living Osty");
        async Task Verify<T>(int expected, bool upgraded = false) where T : CardModel, new()
        {
            int hp = target.CurrentHp;
            var played = await Play<T>(player, target, upgraded);
            Check(hp - target.CurrentHp == expected && !played.Tags.Contains(CardTag.OstyAttack) && !played.ShouldGlowRed,
                $"{typeof(T).Name} upgrade={upgraded} deals owner damage {expected} without an Osty-attack tag or missing-pet warning");
            Check(!played.Description.GetRawText().StartsWith("[gold]奥斯提[/gold]造成"), "Adapted damage text describes the player's attack");
        }
        await Verify<Unleash>(6);
        await Verify<Unleash>(9, true);
        await Verify<Protector>(10);
        await Verify<Protector>(15, true);
        await OstyCmd.Summon(Context, player, 20, null);
        await CreatureCmd.SetCurrentHp(player.Osty!, 7);
        await PowerCmd.Apply<StrengthPower>(Context, player.Osty!, 30, player.Creature, null);
        await PowerCmd.Apply<StrengthPower>(Context, player.Creature, 2, player.Creature, null);
        await Verify<Unleash>(15); // 6 + 7 current HP + 2 owner Strength
        await Verify<Protector>(32); // 10 + 20 max HP + 2 owner Strength
        await Verify<Unleash>(18, true);
        await Verify<Protector>(37, true);
        var state = (CombatState)player.Creature.CombatState!;
        foreach (var model in new CardModel[] { state.CreateCard<Unleash>(player), state.CreateCard<Protector>(player) })
        {
            await PowerCmd.Apply<VigorPower>(Context, player.Creature, 4, player.Creature, null);
            await CardPileCmd.AddGeneratedCardToCombat(model, PileType.Hand, player);
            model.UpdateDynamicVarPreview(CardPreviewMode.Normal, target, model.DynamicVars);
            int expected = model is Unleash ? 19 : 36;
            Check(model.DynamicVars.CalculatedDamage.PreviewValue == expected,
                "HP-scaling hand preview uses current/max pet HP and owner Strength/Vigor: " + model.Id);
            int hp = target.CurrentHp;
            Check(model.TryManualPlay(target), "Play the exact HP-scaling preview card");
            await Until(() => model.Pile?.Type == PileType.Discard, "HP-scaling card finishes");
            await Frames(15);
            Check(hp - target.CurrentHp == expected && !player.Creature.HasPower<VigorPower>() && player.Osty!.CurrentHp == 7,
                "Preview equals actual owner damage, consumes Vigor, and leaves pet HP intact");
            var clone = state.CloneCard(model);
            await CardPileCmd.AddGeneratedCardToCombat(clone, PileType.Hand, player);
            Check(clone.DynamicVars.CalculatedDamage.Calculate(target) == (model is Unleash ? 13 : 30) && !clone.Tags.Contains(CardTag.OstyAttack),
                "Native cloning preserves adapted HP calculation and tags");
            await CardCmd.Discard(Context, clone);
        }
        foreach (var card in new CardModel[] { native.RunState.CreateCard<Unleash>(native), native.RunState.CreateCard<Protector>(native) })
            Check(card.Tags.Contains(CardTag.OstyAttack) && card.Description.GetRawText().StartsWith("[gold]奥斯提[/gold]造成"),
                "Other roles keep native Osty attack tags and text: " + card.Id);
        await PowerCmd.Remove<StrengthPower>(player.Creature);
    }
}
