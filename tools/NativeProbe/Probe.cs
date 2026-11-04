using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;

// Independent QA instrumentation, never shipped. No character models, game
// rule patches, resource replacement or reference to PunchConstruct/BaseLib.
[ModInitializer(nameof(Init))]
public static class Probe
{
    private static SceneTree Tree = null!;
    private static AssetLoadingSession? Common;
    private static bool Started;
    public static void Init()
    {
        var patch = new Harmony("NativeProbe.Instrumentation");
        patch.Patch(AccessTools.Method(typeof(PreloadManager), "LoadAssets"), postfix: new HarmonyMethod(typeof(Probe), nameof(ObservePreload)));
        patch.Patch(AccessTools.Method(typeof(NMainMenu), nameof(NMainMenu._Ready)), postfix: new HarmonyMethod(typeof(Probe), nameof(Start)));
    }
    private static void ObservePreload(string name, AssetLoadingSession __result)
    { if (name == "Common") Common = __result; }
    private static void Start(NMainMenu __instance)
    {
        if (Started) return;
        Started = true; Tree = __instance.GetTree();
        Callable.From(() => { _ = Run(); }).CallDeferred();
    }
    private static async Task Frames(int n)
    { for (int i = 0; i < n; i++) await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame); }
    private static IEnumerable<Node> Nodes(Node n)
    { yield return n; foreach (var c in n.GetChildren()) foreach (var x in Nodes(c)) yield return x; }
    private static async Task Until(Func<bool> condition)
    { for (int i = 0; i < 1800; i++) { if (condition()) return; await Frames(1); } throw new TimeoutException(); }
    private static async Task Run()
    {
        try
        {
            NAudioManager.Instance!.SetMasterVol(0);
            await NGame.Instance!.GameStartupComplete;
            await Until(() => Common != null);
            await Common!.WaitForCompletion();
            SaveManager.Instance.Progress.EnableFtues = false;
            if (AppDomain.CurrentDomain.GetAssemblies().Any(a => a.GetName().Name == "PunchConstruct"))
                throw new InvalidOperationException("Character mod must not be loaded in the independent control");
            GD.Print("[NativeProbe ISOLATION] PunchConstruct not loaded; native rules and assets");
            var run = await NGame.Instance.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true,
                [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "EVENTROUTES", GameMode.Standard);
            var player = run.Players.Single();
            AccessTools.Field(typeof(Player), "<UnlockState>k__BackingField").SetValue(player,
                AccessTools.Field(typeof(UnlockState), "all").GetValue(null));
            foreach (var pool in new CardPoolModel[] { ModelDb.CardPool<RegentCardPool>(), ModelDb.CardPool<NecrobinderCardPool>(), ModelDb.CardPool<DefectCardPool>() })
            {
                await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<ColorfulPhilosophers>(), showTransition: false);
                var ev = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
                var task = (Task)AccessTools.Method(typeof(ColorfulPhilosophers), "OfferRewards").Invoke(ev, [pool])!;
                await Until(() => task.IsFaulted || Nodes(Tree.Root).OfType<NRewardsScreen>().Any(s => s.IsVisibleInTree()));
                if (task.IsFaulted) await task;
                var screen = Nodes(Tree.Root).OfType<NRewardsScreen>().Single(s => s.IsVisibleInTree());
                var buttons = Nodes(screen).OfType<NRewardButton>().Where(b => b.Reward is CardReward).ToArray();
                if (buttons.Length != 3) throw new InvalidOperationException("Expected native three reward rows");
                int before = player.Deck.Cards.Count;
                foreach (var button in buttons)
                {
                    var card = ((CardReward)button.Reward!).Cards.First();
                    AccessTools.Method(typeof(NRewardButton), "OnRelease").Invoke(button, []);
                    await Until(() => Nodes(Tree.Root).OfType<NCardRewardSelectionScreen>().Any());
                    var row = Nodes(Tree.Root).OfType<NCardRewardSelectionScreen>().Single();
                    await Frames(45);
                    AccessTools.Method(typeof(NCardRewardSelectionScreen), "SelectCard").Invoke(row, [row.GetCardHolder(card)]);
                    await Frames(45);
                }
                await task;
                if (player.Deck.Cards.Count != before + 3) throw new InvalidOperationException("Native reward acquisition mismatch");
                GD.Print("[NativeProbe REWARDS] three native selections completed: " + pool.EnergyColorName);
            }
            await Frames(180);
            await NGame.Instance.ReturnToMainMenu();
            await Frames(20);
            if (OS.GetCmdlineArgs().Contains("--nativeprobe-release-pools"))
            { PoolShutdown.ReleaseCards(); await Frames(3); }
            GC.Collect(); GC.WaitForPendingFinalizers();
            await Frames(3);
            GD.Print("[NativeProbe PASS] independent native event reward route");
            Tree.Quit();
        }
        catch (Exception e) { GD.PrintErr("[NativeProbe FAIL] " + e); Tree.Quit(1); }
    }
}
