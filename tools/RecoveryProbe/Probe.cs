using System.Diagnostics;
using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Enchantments;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Transport.ENet;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;
#if !NATIVE_ONLY
using PunchConstruct;
#endif

// Isolated QA mod. Never copied into playable runtime or release candidates.
// No gameplay-rule patches: native join UI and native load-lobby machinery.
[ModInitializer(nameof(Init))]
public static class RecoveryProbe
{
    private static SceneTree Tree = null!;
    private static AssetLoadingSession? Common;
    private static bool Started;
    private static INetGameService? Net;
    private static string Role = "", Stage = "", Coord = "";
    private static bool Native;
    private static int Players;
    private static string Party = "";
    private static string PeerRole(int index) => index == 0 ? "host" : index == 1 ? "client" : "client" + index;
    private static int PeerIndex => Role == "host" ? 0 : Role == "client" ? 1 : int.Parse(Role[6..]);
    private static int Checks;
    private static bool? HostClosed;
    private static readonly List<NetError> Popups = [];
    private static readonly ThrowingPlayerChoiceContext Context = new();
    private static CharacterModel Character(int index)
    {
#if NATIVE_ONLY
        return ModelDb.Character<Ironclad>();
#else
        return Native || (Party == "mixed" && index > 1) ? ModelDb.Character<Ironclad>()
            : Party == "mixed" && index == 1 ? ModelDb.Character<Necrobinder>() : ModelDb.Character<ConstructCharacter>();
#endif
    }
    private static string Arg(string name) => OS.GetCmdlineArgs().Single(a => a.StartsWith(name + "=")).Substring(name.Length + 1);
    private static string FileAt(string name) => Path.Combine(Coord, name);
    private static void Mark(string name) => File.WriteAllText(FileAt(name + "-" + Role), "ready");
    private static void Check(bool value, string text)
    { if (!value) throw new InvalidOperationException(text); GD.Print($"[Recovery CHECK {Checks++:D3}] {text}"); }
    public static void Init()
    {
        var patch = new Harmony("RecoveryProbe.QaOnly");
        patch.Patch(AccessTools.Method(typeof(PreloadManager), "LoadAssets"), postfix: new HarmonyMethod(typeof(RecoveryProbe), nameof(Preload)));
        patch.Patch(AccessTools.Method(typeof(NMainMenu), nameof(NMainMenu._Ready)), postfix: new HarmonyMethod(typeof(RecoveryProbe), nameof(Start)));
        patch.Patch(AccessTools.Method(typeof(NErrorPopup), "Create", [typeof(NetErrorInfo)]), prefix: new HarmonyMethod(typeof(RecoveryProbe), nameof(Popup)));
        patch.Patch(AccessTools.Method(typeof(ENetHost), "SetHostIsClosed"), postfix: new HarmonyMethod(typeof(RecoveryProbe), nameof(HostClosedChanged)));
    }
    private static void HostClosedChanged(bool __0) { HostClosed = __0; GD.Print("[Recovery HOST CLOSED] " + __0); }
    private static void Popup(NetErrorInfo info) { Popups.Add(info.GetReason()); GD.Print("[Recovery POPUP] " + info.GetReason()); }
    private static void Preload(string name, AssetLoadingSession __result) { if (name == "Common") Common = __result; }
    private static void Start(NMainMenu __instance)
    {
        if (Started) return;
        Started = true; Tree = __instance.GetTree();
        Callable.From(() => { _ = Run(); }).CallDeferred();
    }
    private static async Task Frames(int n = 1) { for (int i = 0; i < n; i++) await Tree.ToSignal(Tree, SceneTree.SignalName.ProcessFrame); }
    private static async Task Wait(Func<bool> condition, string text, int timeout = 40)
    {
        var watch = Stopwatch.StartNew();
        while (!condition())
        {
            if (watch.Elapsed.TotalSeconds > timeout) throw new TimeoutException(text);
            if (NRun.Instance == null) Net?.Update();
            await Frames();
        }
    }
    private static async Task Barrier(string name)
    { Mark(name); await Wait(() => Enumerable.Range(0, Players).All(i => File.Exists(FileAt(name + "-" + PeerRole(i)))), name); }
    private sealed class NewListener : IStartRunLobbyListener
    {
        internal StartRunLobby Lobby = null!;
        internal Task<RunState>? Task;
        public void BeginRun(string seed, List<ActModel> acts, IReadOnlyList<ModifierModel> modifiers)
            => Task = Start(seed, acts, modifiers);
        private async Task<RunState> Start(string seed, List<ActModel> acts, IReadOnlyList<ModifierModel> modifiers)
        {
            // Like the native lobby transition, leave message dispatch before
            // replacing lobby handlers. Skipping visuals must not skip this.
            await Frames(1);
            return await NGame.Instance!.StartNewMultiplayerRun(Lobby, true, acts, modifiers, seed, Lobby.Ascension);
        }
        public void PlayerConnected(StartRunLobbyPlayer player) { }
        public void PlayerChanged(StartRunLobbyPlayer player, bool random) { }
        public void AscensionChanged() { } public void SeedChanged() { } public void ModifiersChanged() { } public void MaxAscensionChanged() { }
        public void RemotePlayerDisconnected(StartRunLobbyPlayer player) { }
        public void LocalPlayerDisconnected(NetErrorInfo info) { }
    }
    private sealed class LoadListener : ILoadRunLobbyListener
    {
        internal LoadRunLobby Lobby = null!;
        internal Task<RunState>? Task;
        public Task<bool> ShouldAllowRunToBegin() => System.Threading.Tasks.Task.FromResult(true);
        public void BeginRun() => Task = Load();
        private async Task<RunState> Load()
        {
            // Native load UI yields during its transition before replacing
            // lobby handlers. Do not mutate a message-bus handler list inside
            // the dispatch of LobbyBeginLoadedRunMessage.
            await Frames(1);
            var run = RunState.FromSerializable(Lobby.Run);
            await RunManager.Instance.SetUpSavedMultiplayer(run, Lobby);
            await NGame.Instance!.LoadRun(run, Lobby.Run.PreFinishedRoom);
            return run;
        }
        public void PlayerConnected(LoadRunLobbyPlayer p) { } public void RemotePlayerDisconnected(ulong id) { }
        public void PlayerReadyChanged(ulong id) { } public void LocalPlayerDisconnected(NetErrorInfo info) { }
    }
    private static string Snapshot(RunState run) => JsonSerializer.Serialize(new {
        seed = run.Rng.StringSeed, mode = run.GameMode, players = run.Players.Select(p => new {
            p.NetId, character = p.Character.Id.ToString(), p.Gold, p.Creature.CurrentHp, p.Creature.MaxHp,
            deck = p.Deck.Cards.Select(c => new { id = c.Id.ToString(), c.IsUpgraded, c.CurrentUpgradeLevel,
                values = c.DynamicVars.Select(v => new { v.Key, v.Value.BaseValue }).ToArray(),
                energy = c.EnergyCost.GetWithModifiers(CostModifiers.None), stars = c.CurrentStarCost,
                keywords = c.Keywords.OrderBy(k => k).ToArray(),
                enchantment = c.Enchantment?.Id.ToString(), level = c.Enchantment?.Amount }).ToArray(),
            relics = p.Relics.Select(r => new { id = r.Id.ToString(), r.DisplayAmount }).ToArray()
        }).ToArray()
    });
    private static NetHostGameService Host(ushort port)
    {
        var host = new NetHostGameService(PeerVersionInfo.LocalDefault());
        Check(host.StartENetHost(port, Players - 1) == null, "Native ENet host starts"); Net = host; return host;
    }
    private static async Task<RunState> NewRun(ushort port)
    {
        var listener = new NewListener(); StartRunLobby lobby;
        if (Role == "host")
        {
            lobby = new StartRunLobby(GameMode.Standard, Host(port), listener, Players); listener.Lobby = lobby;
            lobby.AddLocalHostPlayer(new UnlockState(SaveManager.Instance.Progress), 0);
            lobby.SetLocalCharacter(Character(PeerIndex));
            lobby.SetSeed("RECOVERY2026"); Mark("listening");
        }
        else
        {
            await Wait(() => File.Exists(FileAt("listening-host")), "seed host listening");
            if (PeerIndex > 1) await Wait(() => File.Exists(FileAt("joined-" + (PeerIndex - 1))), "previous guest joined");
            var client = new NetClientGameService(PeerVersionInfo.LocalDefault());
            var joined = await new JoinFlow(client).Begin(new ENetClientConnectionInitializer((ulong)(PeerIndex + 1), "127.0.0.1", port), Tree);
            Check(joined.joinResponse != null, "New-run native join response received"); Net = client;
            await Frames(1); // JoinFlow may complete inside join-response dispatch.
            lobby = new StartRunLobby(GameMode.Standard, client, listener, Players); listener.Lobby = lobby;
            lobby.InitializeFromMessage(joined.joinResponse!.Value);
            lobby.SetLocalCharacter(Character(PeerIndex));
            File.WriteAllText(FileAt("joined-" + PeerIndex), "ready");
        }
        await Wait(() => lobby.Players.Count == Players && lobby.Players.All(p => p.character?.Id == Character((int)p.id - 1).Id), "character synchronization");
        await Barrier("lobby"); lobby.SetReady(true);
        await Wait(() => listener.Task is { IsCompleted: true }, "new native multiplayer run");
        var run = await listener.Task!; lobby.CleanUp(false);
        Check(run.Players.Count == Players && LocalContext.GetMe(run)?.NetId == (ulong)(PeerIndex + 1), "All saved players have the correct native local identity");
        return run;
    }
    private static async Task Seed(RunState run, ushort port)
    {
        var drawings = NMapScreen.Instance!.Drawings;
        drawings.SetDrawingModeLocal(DrawingMode.Drawing);
        await Wait(() => run.Players.All(p => drawings.GetDrawingMode(p.NetId) == DrawingMode.Drawing), "connected native map drawing modes synchronize");
        Check(drawings.GetLocalDrawingMode(false) == DrawingMode.Drawing, "Connected local and remote map drawing updates still work");
        await Barrier("drawing-modes");
        foreach (var p in run.Players)
        {
#if !NATIVE_ONLY
            if (p.Character is not ConstructCharacter) continue;
            var fervor = run.CreateCard<BattleFervor>(p); fervor.UpgradeInternal();
            await CardPileCmd.Add(fervor, PileType.Deck, skipVisuals: true);
            var scorching = run.CreateCard<ScorchingIronWave>(p);
            int upgrades = 2 + (int)p.NetId;
            for (int i = 0; i < upgrades; i++)
            { scorching.UpgradeInternal(); scorching.FinalizeUpgradeInternal(); }
            await CardPileCmd.Add(scorching, PileType.Deck, skipVisuals: true);
            for (int i = 0; i < (int)p.NetId; i++)
                await CardPileCmd.Add(run.CreateCard<GammaBlast>(p), PileType.Deck, skipVisuals: true);
            await RelicCmd.Replace(p.Relics.OfType<GuardStance>().Single(), ModelDb.Relic<AncientGuardStance>().ToMutable());
            await RelicCmd.Obtain<ConstructHelmet>(p); await RelicCmd.Obtain<SpinningScrew>(p);
            await RelicCmd.Obtain<OpenEndWrench>(p); await RelicCmd.Obtain<SpareKnuckles>(p);
            await RelicCmd.Obtain<EmergencyOilCan>(p);
            int momentum = 4 + (int)p.NetId;
            CardCmd.Enchant(ModelDb.Enchantment<Momentum>().ToMutable(), p.Deck.Cards.OfType<SmallWave>().First(), momentum);
            await RelicCmd.Obtain(ModelDb.Relic<ConstructNutritiousSoup>().ToMutable(), p);
            Check(p.Deck.Cards.OfType<IronWave>().Count(c => c.Keywords.Contains(CardKeyword.Eternal) && c.EnergyCost.GetWithModifiers(CostModifiers.None) == 0) == 4 &&
                p.Deck.Cards.OfType<SmallWave>().Any(c => c.Enchantment is Momentum m && m.Amount == momentum),
                "Soup prepares four zero-cost Eternal Iron Waves and skips the Momentum Small Wave for disk recovery");
            Check(scorching.CurrentUpgradeLevel == upgrades && scorching.DynamicVars.Damage.BaseValue == 5 + upgrades * (upgrades + 3) / 2 &&
                scorching.DynamicVars.Block.BaseValue == 5 + upgrades * (upgrades + 3) / 2 && !scorching.Keywords.Contains(CardKeyword.Eternal),
                "Each owner saves a distinct repeatedly upgraded Scorching Iron Wave untouched by Soup");
#endif
        }
        // We are on the initial map. preFinishedRoom is only valid for native
        // combat/event rooms, not an arbitrary current MapRoom.
        await SaveManager.Instance.SaveRun(null);
        if (Role == "host")
        {
            Check(HostClosed == true, "Native run closes ENet to new incoming connections");
            await Wait(() => SaveManager.Instance.HasMultiplayerRunSave, "native host save flush");
            Check(SaveManager.Instance.HasMultiplayerRunSave, "Actual native host multiplayer disk save exists");
        }
        File.WriteAllText(FileAt("expected-" + Role + ".json"), Snapshot(run));
        await Barrier("saved");
        if (Arg("--recovery-disconnect") == "host-loss")
        {
            if (Role == "host")
            {
                Mark("kill-ready");
                await Wait(() => false, "QA runner terminates this disposable host", 70);
            }
            else
            {
                await Wait(() => NRun.Instance == null && !Net!.IsConnected, "native guest detects actual host process loss and returns to menu", 70);
                Check(drawings.GetLocalDrawingMode(false) == DrawingMode.None, "Host loss preserves native local map cleanup");
                Check(Popups.Count > 0, "Native host-loss error popup reaches the disconnected guest");
                Net = null;
            }
            await Barrier("seed-finished");
            return;
        }
        if (Role == "host")
        {
            bool disconnected = false;
            ((NetHostGameService)Net!).ClientDisconnected += (_, _) => disconnected = true;
            Mark("disconnect-observer");
            await Wait(() => disconnected, "real guest disconnect notification", 70);
            Check(NRun.Instance != null && RunManager.Instance.DebugOnlyGetState() == run, "Host retains its native run after actual guest disconnect");
            Mark("disconnect-observed");
            await Wait(() => File.Exists(FileAt("attempted-client")), "native client rejoin UI finishes");
        }
        else if (Role == "client")
        {
            await Wait(() => File.Exists(FileAt("disconnect-observer-host")), "host installs observer");
            if (Arg("--recovery-disconnect") == "process-loss")
            {
                Mark("kill-ready");
                await Wait(() => false, "QA runner terminates this disposable process", 70);
            }
            else if (Arg("--recovery-disconnect") == "abrupt") Net!.Disconnect(NetError.Quit, true);
            else await NGame.Instance!.ReturnToMainMenu();
            await Wait(() => NRun.Instance == null, "native disconnect returns guest to menu"); Net = null;
            if (Arg("--recovery-disconnect") == "abrupt")
                Check(drawings.GetLocalDrawingMode(false) == DrawingMode.None, "Disconnected map still resets its local drawing mode during native cleanup");
            else
                Check(!GodotObject.IsInstanceValid(drawings) || !drawings.IsInsideTree(), "Normal menu exit removes the previous map drawing UI");
            await Wait(() => File.Exists(FileAt("disconnect-observed-host")), "host detects disconnect");
            if (Arg("--recovery-livejoin") == "yes")
            {
                var screen = NJoinFriendScreen.Create(); Tree.Root.AddChild(screen);
                Popups.Clear();
                await screen!.JoinGameAsync(new ENetClientConnectionInitializer(2, "127.0.0.1", port));
                Check(Popups.Contains(NetError.RunInProgress), "Actual native join UI refuses live rejoin with RunInProgress");
                screen.QueueFree();
            }
            Check(NRun.Instance == null, "Disconnected guest remains safely in the native menu before regrouping");
            Mark("attempted");
        }
        else
        {
            await Wait(() => File.Exists(FileAt("disconnect-observed-host")), "host observes other guest leaving");
            Check(Net!.IsConnected && NRun.Instance != null && RunManager.Instance.DebugOnlyGetState() == run,
                "Remaining guest keeps its native run and connection when another guest leaves");
        }
        await Barrier("seed-finished");
    }
    private static async Task<RunState> Resume(ushort port)
    {
        var listener = new LoadListener(); LoadRunLobby lobby;
        if (Role == "host")
        {
            var disk = SaveManager.Instance.LoadAndCanonicalizeMultiplayerRunSave(1);
            Check(disk.Success && disk.SaveData != null, "New process reads actual native multiplayer save");
            lobby = new LoadRunLobby(Host(port), listener, disk.SaveData!); listener.Lobby = lobby;
            lobby.AddLocalHostPlayer(); Mark("listening");
        }
        else
        {
            await Wait(() => File.Exists(FileAt("listening-host")), "load host listening");
            bool reverse = Arg("--recovery-resume-order") == "reverse";
            int previous = reverse ? PeerIndex + 1 : PeerIndex - 1;
            if (previous > 0 && previous < Players)
                await Wait(() => File.Exists(FileAt("joined-" + previous)), "previous saved guest joined");
            var client = new NetClientGameService(PeerVersionInfo.LocalDefault());
            var joined = await new JoinFlow(client).Begin(new ENetClientConnectionInitializer((ulong)(PeerIndex + 1), "127.0.0.1", port), Tree);
            Check(joined.loadJoinResponse != null, "Returning player receives native load-lobby response"); Net = client;
            await Frames(1); // Register lobby handlers only after dispatch returns.
            lobby = new LoadRunLobby(client, listener, joined.loadJoinResponse!.Value); listener.Lobby = lobby;
            File.WriteAllText(FileAt("joined-" + PeerIndex), "ready");
            GD.Print($"[Recovery JOIN] saved player {PeerIndex + 1}; order={(reverse ? "reverse" : "same")}");
        }
        await Wait(() => lobby.Players.Count == Players, "all saved players return to native load lobby");
        await Barrier("lobby"); lobby.SetReady(true);
        await Wait(() => listener.Task is { IsCompleted: true }, "native saved multiplayer startup");
        var run = await listener.Task!; lobby.CleanUp(false, NetError.Quit);
        Check(LocalContext.GetMe(run)?.NetId == (ulong)(PeerIndex + 1) && LocalContext.GetMe(run)?.Character.Id == Character(PeerIndex).Id,
            "Fresh-process local player slot and character match their saved identity");
        var expected = File.ReadAllText(FileAt("expected-" + Role + ".json"));
        var actual = Snapshot(run);
        File.WriteAllText(FileAt("restored-" + Role + ".json"), actual);
        Check(actual == expected, "Native disk/load-lobby restoration preserves seed, identity, health, gold, upgraded deck, enchantments and relics");
#if !NATIVE_ONLY
        foreach (var p in run.Players.Where(p => p.Character is ConstructCharacter))
        {
            var scorching = p.Deck.Cards.OfType<ScorchingIronWave>().Single();
            int upgrades = 2 + (int)p.NetId;
            Check(scorching.CurrentUpgradeLevel == upgrades && scorching.DynamicVars.Damage.BaseValue == 5 + upgrades * (upgrades + 3) / 2 &&
                scorching.DynamicVars.Block.BaseValue == 5 + upgrades * (upgrades + 3) / 2 &&
                p.Relics.Count(r => r is OpenEndWrench or SpinningScrew or SpareKnuckles or EmergencyOilCan) == 4,
                "Fresh-process disk recovery retains exact repeated upgrades and all four workshop relic identities for owner " + p.NetId);
        }
#endif
        await Barrier("restored");
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
        await Wait(() => run.Players.All(p => p.PlayerCombatState?.Phase == PlayerTurnPhase.Play) && !CombatManager.Instance.PlayerActionsDisabled, "post-recovery native combat ready");
        foreach (var p in run.Players)
        {
#if !NATIVE_ONLY
            if (p.Character is not ConstructCharacter) continue;
            Check(p.Relics.OfType<AncientGuardStance>().Single().DisplayAmount == 1, "Recovered ancient growth begins its new combat at one");
            await CreatureCmd.GainBlock(p.Creature, 20, ValueProp.Unpowered, null);
            await CreatureCmd.Damage(Context, p.Creature, 1, ValueProp.Unpowered, p.Creature.CombatState!.HittableEnemies.First());
            Check(p.Creature.GetPowerAmount<GuardStrengthNextTurnPower>() == 1 && p.Relics.OfType<AncientGuardStance>().Single().DisplayAmount == 2,
                "Recovered ancient relic actually queues one Strength and advances its own next grant");
#endif
        }
        var actor = run.Players[0]; var card = actor.PlayerCombatState!.Hand.Cards.First(c => c.Type == CardType.Attack);
        await PlayerCmd.SetEnergy(20, actor);
        await Barrier("attack-prepared");
        if (LocalContext.IsMine(card)) Check(card.TryManualPlay(actor.Creature.CombatState!.HittableEnemies.First()), "Recovered host queues a real network attack");
        await Wait(() => card.Pile?.Type is not (PileType.Hand or PileType.Play), "restored network action resolves");
        await RunManager.Instance.ActionExecutor.FinishedExecutingActions();
        await Barrier("attack-resolved");
        Check(!RunManager.Instance.ActionExecutor.IsRunning, "All restored peers finish the native action without a stuck queue");
        return run;
    }
    private static async Task Run()
    {
        try
        {
            Role = Arg("--recovery-role"); Stage = Arg("--recovery-stage"); Coord = Arg("--recovery-dir");
            Native = Arg("--recovery-character") == "native";
            Players = int.Parse(Arg("--recovery-players")); Party = Arg("--recovery-party");
#if NATIVE_ONLY
            Check(AppDomain.CurrentDomain.GetAssemblies().All(a => a.GetName().Name != "PunchConstruct"), "Independent control has no loaded PunchConstruct assembly");
#endif
            ushort port = ushort.Parse(Arg("--recovery-port"));
            NAudioManager.Instance!.SetMasterVol(0);
            await NGame.Instance!.GameStartupComplete; await Wait(() => Common != null, "common preload"); await Common!.WaitForCompletion();
            SaveManager.Instance.Progress.EnableFtues = false;
            Check(SaveManager.Instance.SettingsSave.VolumeMaster == 0, "QA is muted");
            if (Stage == "seed") await Seed(await NewRun(port), port); else await Resume(port);
            await Barrier("finished");
            GD.Print($"[Recovery PASS] {Stage} {Role} {Checks} checks; nativeControl={Native}");
            if (NRun.Instance != null) await NGame.Instance.ReturnToMainMenu();
            await Frames(10); await Common!.WaitForCompletion(); PoolShutdown.ReleaseCards(); await Frames(3); Tree.Quit();
        }
        catch (Exception ex) { GD.Print("[Recovery FAIL] " + ex); Tree.Quit(1); }
    }
}

