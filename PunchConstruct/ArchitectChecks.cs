using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Ancients;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task ArchitectChecks()
    {
        var set = ModelDb.Event<TheArchitect>().DialogueSet;
        string key = ModelDb.GetId<ConstructCharacter>().Entry;
        Check(set.CharacterDialogues.TryGetValue(key, out var own) && own.Count == 3,
            "Architect's separate EventModel dialogue set contains three construct exchanges");
        for (int visits = 0; visits < 6; visits++)
        {
            var choices = set.GetValidDialogues(ModelDb.GetId<ConstructCharacter>(), visits, visits, false).ToArray();
            Check(choices.Length > 0 && choices.All(d => d.Lines.Count == 3 && d.Lines.All(l =>
                l.LineText?.GetFormattedText() is { Length: > 0 } text && !text.Contains(".talk.")) &&
                d.StartAttackers == ArchitectAttackers.None && d.EndAttackers == ArchitectAttackers.Both),
                "First/repeat visit has localized lines and native ending staging: " + visits);
            if (visits < 3) Check(choices.Length == 1 && choices[0].VisitIndex == visits,
                "First three wins select the intended exchange in order: " + visits);
        }
        Check(set.GetValidDialogues(ModelDb.GetId<Ironclad>(), 0, 0, false).All(d =>
            !d.Lines.Any(l => l.LineText?.LocEntryKey.StartsWith("PUNCHCONSTRUCT_") == true)),
            "Native character Architect dialogue remains unchanged");

        // Exercise each conversation through its real event room and option buttons.
        for (int visit = 0; visit < 3; visit++)
        {
            var stats = SaveManager.Instance.Progress.GetOrCreateCharacterStats(ModelDb.GetId<ConstructCharacter>());
            stats.TotalWins = visit;
            var run = await ReliabilityRun("ARCHITECT" + visit);
            var player = run.Players.Single();
            int hp = player.Creature.CurrentHp;
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<TheArchitect>(), showTransition: false);
            // Debug entry does not autosave; provide the files a normal run would
            // have before the native victory flow removes them.
            await SaveManager.Instance.SaveRun(run.CurrentRoom);
            await SaveManager.Instance.SaveRun(run.CurrentRoom);
            var architect = (TheArchitect)((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
            var dialogue = (AncientDialogue)AccessTools.PropertyGetter(typeof(TheArchitect), "Dialogue").Invoke(architect, [])!;
            int LineIndex() => (int)AccessTools.PropertyGetter(typeof(TheArchitect), "CurrentLineIndex").Invoke(architect, [])!;
            Check(dialogue?.VisitIndex == visit && dialogue.Lines.Count == 3 && LineIndex() == 0,
                "Entering actual event starts with the first line: " + visit);
            await Frames(90);
            Check(player.Creature.CurrentHp == hp && !Descendants(_tree.Root).OfType<NGameOverScreen>().Any(),
                "Entry does not kill the player or skip into game over: " + visit);
            Check(AccessTools.PropertyGetter(typeof(TheArchitect), "SpeechBubble").Invoke(architect, []) != null,
                "Architect's first speech bubble is actually displayed: " + visit);
            if (visit == 0) await Capture("qa-architect-first-line");
            for (int line = 0; line < 3; line++)
            {
                Check(LineIndex() == line && player.Creature.CurrentHp == hp,
                    "Conversation line is reached before the ending: " + visit + "/" + line);
                var button = Descendants(_tree.Root).OfType<NEventOptionButton>().Single(b => b.IsVisibleInTree());
                AccessTools.Method(typeof(NEventOptionButton), "OnRelease").Invoke(button, []);
                if (line < 2)
                    await Until(() => LineIndex() == line + 1, "Architect dialogue advances");
                else
                    await Until(() => Descendants(_tree.Root).OfType<NGameOverScreen>().Any(), "Architect ending reaches game over");
                await Frames(60);
            }
            Check(stats.TotalWins == visit + 1, "Normal ending records a victory after all dialogue: " + visit);
            await NGame.Instance!.ReturnToMainMenu();
            await Frames(15);
        }
    }
}
