using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task CapsuleNativeControl()
    {
        bool construct = Godot.OS.GetCmdlineArgs().Contains("--punchconstruct-capsule-construct-control");
        CharacterModel character = construct ? ModelDb.Character<ConstructCharacter>() : ModelDb.Character<Ironclad>();
        var run = await NGame.Instance!.StartNewSingleplayerRun(character, true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "CAPSULE-NATIVE-CONTROL", GameMode.Standard);
        var player = run.Players.Single();
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<BrainLeech>(), showTransition: false);
        var bag = new RelicGrabBag();
        bag.Populate([ModelDb.Relic<Anchor>(), ModelDb.Relic<BronzeScales>(), ModelDb.Relic<OddlySmoothStone>(), ModelDb.Relic<BagOfPreparation>()], new Rng(912));
        AccessTools.Field(typeof(Player), "<RelicGrabBag>k__BackingField").SetValue(player, bag);
        var before = player.Deck.Cards.ToArray();
        await RelicCmd.Obtain<LargeCapsule>(player);
        var added = player.Deck.Cards.Except(before).ToArray();
        Check(added.Length == 2 && (construct ? added.All(c => c is SmallWave) :
                added.Any(c => c.Tags.Contains(CardTag.Strike)) && added.Any(c => c.Tags.Contains(CardTag.Defend))),
            "Capsule-only control rewards match the selected character: " + character.Id);
        // Include the unmodified native Punch Off scene: its two Spine actors
        // must also be present in the renderer-teardown comparison.
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<PunchOff>(), showTransition: false);
        await ResolvePunchNab(((EventRoom)run.CurrentRoom!).LocalMutableEvent!);
        await Frames(180);
        await NGame.Instance.ReturnToMainMenu();
        await Frames(10);
        GC.Collect(); GC.WaitForPendingFinalizers();
        await Frames(2);
        Godot.GD.Print($"[Construct CAPSULE CONTROL PASS] {_checks} checks; character={character.Id}");
    }
    private static async Task CapsuleChecks(Player player)
    {
        var strike = AccessTools.Method(typeof(LargeCapsule), "GetStrikeForCharacter");
        var defend = AccessTools.Method(typeof(LargeCapsule), "GetDefendForCharacter");
        Check(!player.Character.CardPool.AllCards.Any(c => c.Rarity == CardRarity.Basic && c.Tags.Contains(CardTag.Strike)) &&
            !player.Character.CardPool.AllCards.Any(c => c.Rarity == CardRarity.Basic && c.Tags.Contains(CardTag.Defend)),
            "Capsule fixture reproduces missing native starter-tag candidates without adding global card tags");
        Check(strike.Invoke(null, [player.Character]) is SmallWave && defend.Invoke(null, [player.Character]) is SmallWave,
            "Both capsule starter lookups support the construct instead of throwing First() failures");
        foreach (CharacterModel role in new CharacterModel[] { ModelDb.Character<Ironclad>(), ModelDb.Character<Silent>(), ModelDb.Character<Regent>(), ModelDb.Character<Necrobinder>(), ModelDb.Character<Defect>() })
            Check(((CardModel)strike.Invoke(null, [role])!).Tags.Contains(CardTag.Strike) && ((CardModel)defend.Invoke(null, [role])!).Tags.Contains(CardTag.Defend),
                "Original role keeps its own Strike and Defend: " + role.Id);
        var originalDeck = player.Deck.Cards.ToArray();
        var originalBag = player.RelicGrabBag;
        int relics = player.Relics.Count;
        var capsule = ModelDb.Relic<LargeCapsule>().ToMutable();
        var bag = new RelicGrabBag();
        bag.Populate([ModelDb.Relic<Anchor>(), ModelDb.Relic<BronzeScales>(), ModelDb.Relic<OddlySmoothStone>(), ModelDb.Relic<BagOfPreparation>()], new Rng(912));
        var bagField = AccessTools.Field(typeof(Player), "<RelicGrabBag>k__BackingField");
        bagField.SetValue(player, bag);
        try
        {
            await RelicCmd.Obtain(capsule, player);
            var added = player.Deck.Cards.Except(originalDeck).ToArray();
            Check(added.Length == 2 && added.All(c => c is SmallWave && !c.IsUpgraded && c.Owner == player),
                "Actual capsule acquisition adds exactly two ordinary Small Waves through native commands");
            Check(originalDeck.All(player.Deck.Cards.Contains) && player.Relics.Count == relics + 1 + capsule.DynamicVars["Relics"].IntValue,
                "Native random relic count is retained and the existing deck is not replaced");
            Check(capsule.DynamicDescription.GetFormattedText().Contains("小斩波") && capsule.DynamicEventDescription.GetFormattedText().Contains("小斩波"),
                "Owned capsule and event preview both describe the adapted starter reward");
        }
        finally { bagField.SetValue(player, originalBag); }
    }
}
