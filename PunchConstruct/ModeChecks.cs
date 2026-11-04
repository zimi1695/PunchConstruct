using System.Diagnostics;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Modifiers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task ResolveModeChoice(Task task)
    {
        var timer = Stopwatch.StartNew();
        while (!task.IsCompleted)
        {
            if (timer.Elapsed.TotalSeconds > 40) throw new TimeoutException("Native modifier reward choice");
            var grid = Descendants(_tree.Root).OfType<NSimpleCardSelectScreen>().FirstOrDefault(s => s.IsVisibleInTree());
            var row = Descendants(_tree.Root).OfType<NCardRewardSelectionScreen>().FirstOrDefault(s => s.IsVisibleInTree());
            if (grid != null)
            {
                var cards = (IReadOnlyList<CardModel>)AccessTools.Field(typeof(NCardGridSelectionScreen), "_cards").GetValue(grid)!;
                var prefs = (MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs)AccessTools.Field(typeof(NSimpleCardSelectScreen), "_prefs").GetValue(grid)!;
                Check(cards.Count >= prefs.MinSelect, "Challenge grid has enough cards for native minimum " + prefs.MinSelect);
                foreach (var card in cards.Take(prefs.MinSelect)) AccessTools.Method(typeof(NSimpleCardSelectScreen), "OnCardClicked").Invoke(grid, [card]);
                if (prefs.RequireManualConfirmation) AccessTools.Method(typeof(NSimpleCardSelectScreen), "CompleteSelection").Invoke(grid, []);
                await Frames(20);
            }
            else if (row != null)
            {
                AccessTools.Method(typeof(NCardRewardSelectionScreen), "SelectCard").Invoke(row, [Descendants(row).OfType<NCardHolder>().First()]);
                await Frames(20);
            }
            await Frames(2);
        }
        await task;
    }

    private static async Task ModeChecks(NMainMenu menu)
    {
        const string seed = "PUNCHMODES2026";
        var sub = menu.OpenSingleplayerSubmenu();
        AccessTools.Method(typeof(NSingleplayerSubmenu), "OpenCustomScreen").Invoke(sub, [null]);
        await Frames(20);
        var screen = Descendants(menu).OfType<NCustomRunScreen>().Single();
        var button = Descendants(screen).OfType<NCharacterSelectButton>().Single(b => b.Character is ConstructCharacter);
        screen.SelectCharacter(button, ModelDb.Character<ConstructCharacter>());
        var list = (NCustomRunModifiersList)AccessTools.Field(typeof(NCustomRunScreen), "_modifiersList").GetValue(screen)!;
        var available = Descendants(list).OfType<NRunModifierTickbox>().Select(t => t.Modifier!).ToArray();
        Check(available.Length >= 18, "Native custom-run screen exposes full challenge catalog");
        var tick = Descendants(list).OfType<NRunModifierTickbox>().First(t => t.Modifier is Flight);
        tick.ForceToggleTick();
        AccessTools.Method(typeof(NCustomRunScreen), "OnSeedInputSubmitted").Invoke(screen, [seed]);
        Check(screen.Lobby.Modifiers.Any(m => m is Flight) && screen.Lobby.Seed == seed,
            "Actual custom screen seed input and challenge checkbox reach native lobby");
        await (Task)AccessTools.Method(typeof(NCustomRunScreen), "StartNewSingleplayerRun").Invoke(screen,
            [seed, new List<ActModel> { ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>() }, screen.Lobby.Modifiers!.ToArray()])!;
        var first = RunManager.Instance.DebugOnlyGetState() ?? throw new InvalidOperationException("UI did not start a run");
        Check(first.GameMode == GameMode.Custom && first.Rng.StringSeed == seed && first.Modifiers.Any(m => m is Flight),
            "Seeded custom run starts through actual screen with construct and selected challenge");
        await NGame.Instance!.ReturnToMainMenu();
        SaveManager.Instance.DeleteCurrentRun();

        // One modifier at a time, plus a representative combination. No full runs.
        var cases = available.Where(m => m is not DeprecatedModifier).Select(m => new[] { m }).ToList();
        cases.Insert(0, []);
        cases.Add([ModelDb.Modifier<Flight>(), ModelDb.Modifier<Terminal>(), ModelDb.Modifier<Midas>()]);
        foreach (var modifiers in cases)
        {
            string label = modifiers.Length == 0 ? "seed-only" : string.Join("+", modifiers.Select(m => m.Title!.GetFormattedText()));
            Diagnostics.Write("info", "qa.mode_case", new { label, seed });
            var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<ConstructCharacter>(), true,
                [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], modifiers.Select(m => m.IsMutable ? (ModifierModel)m.MutableClone() : m.ToMutable()).ToArray(), seed,
                modifiers.Length == 0 ? GameMode.Standard : GameMode.Custom);
            var player = run.Players.Single();
            Check(run.Rng.StringSeed == seed && run.Modifiers.Count == modifiers.Length && player.Character is ConstructCharacter,
                "Seed, character and modifier count preserved: " + label);
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<DollRoom>(), showTransition: false);
            var eventModel = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
            foreach (var modifier in run.Modifiers)
            {
                var initialize = modifier.GenerateNeowOption(eventModel);
                if (initialize != null) await ResolveModeChoice(initialize());
            }
            Check(player.Deck.Cards.Count > 0, "Native challenge initialization leaves a playable deck: " + label);
            var reward = CardFactory.CreateForReward(player, 3, CardCreationOptions.ForRoom(player, RoomType.Monster)
                .WithFlags(CardCreationFlags.IsCardReward)).ToArray();
            Check(reward.Length == 3 && reward.Select(c => c.Card.Id).Distinct().Count() == 3,
                "Challenge card reward has three distinct candidates: " + label);
            if (run.Modifiers.Any(m => m is CharacterCards))
            {
                var foreign = (CharacterCards)run.Modifiers.Single(m => m is CharacterCards);
                var unselected = ModelDb.GetById<CharacterModel>(foreign.CharacterModel).CardPool.AllCards
                    .Where(c => !player.Character.CardPool.AllCardIds.Contains(c.Id)).Select(c => c.Id).ToHashSet();
                var options = foreign.ModifyCardRewardCreationOptions(player, CardCreationOptions.ForRoom(player, RoomType.Monster).WithFlags(CardCreationFlags.IsCardReward));
                int actual = options.GetPossibleCards(player).Count(c => unselected.Contains(c.Id));
                var nativePool = ModelDb.GetById<CharacterModel>(foreign.CharacterModel).CardPool;
                if (nativePool is not ConstructCardPool)
                {
                    Check(unselected.Count > 0 && actual > 0 && nativePool.AllCards.Where(c => c is not IronWave).All(c => CardAcquisition.Eligible(player, c)) &&
                        !options.GetPossibleCards(player).Any(c => c is IronWave),
                        "Selected native character challenge admits its pool while retaining the starter-only Iron Wave exclusion: " + label);
                    var merchant = MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory.CreateForNormalMerchant(player);
                    Check(merchant.CharacterCardEntries.Count > 0 && merchant.CharacterCardEntries.All(e => e.CreationResult != null), "Full-pool challenge merchant populates without an empty-slot failure: " + label);
                }
                Diagnostics.Write("info", "qa.challenge_pool_policy", new { label, foreignCards = unselected.Count, actualForeignCandidates = actual });
            }
            await SaveManager.Instance.SaveRun(run.CurrentRoom);
            var disk = SaveManager.Instance.LoadRunSave();
            Check(disk.Success, "Native disk save loader reads seeded challenge: " + label);
            var restored = RunState.FromSerializable(disk.SaveData!);
            Check(restored.Rng.StringSeed == seed && restored.GameMode == run.GameMode && restored.Modifiers.Count == run.Modifiers.Count &&
                restored.Modifiers.Zip(run.Modifiers).All(pair => pair.First.IsEquivalent(pair.Second)) && restored.Players.Single().Deck.Cards.Count == player.Deck.Cards.Count,
                "Native reconstruction preserves seed, challenge parameters and deck: " + label);
            await NGame.Instance.ReturnToMainMenu();
            SaveManager.Instance.DeleteCurrentRun();
        }
        await QaStartupPreload.Wait(_tree);
        GD.Print($"[Construct MODES PASS] {_checks} checks; real seed/checkbox UI, individual challenge initialization, rewards and disk reconstruction; no full runs");
    }
}
