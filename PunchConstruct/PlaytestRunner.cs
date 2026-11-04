using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Factories;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.GameOverScreen;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Rewards;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Random;

namespace PunchConstruct;

[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static partial class PlaytestRunner
{
    private static bool _started;
    private static SceneTree _tree = null!;
    private static int _checks;
    internal static bool HoldEnemyActions;
    internal static Func<MonsterModel, Task>? EnemyMoveForQa;
    private static readonly ThrowingPlayerChoiceContext Context = new();
    private static void Postfix(NMainMenu __instance)
    {
        if (_started || !OS.GetCmdlineArgs().Any(a => a is "--punchconstruct-selectioncheck" or "--punchconstruct-boxingcheck" or "--punchconstruct-animationcheck" or "--punchconstruct-draftcheck" or "--punchconstruct-postercheck" or "--punchconstruct-vigorcheck" or "--punchconstruct-modescheck" or "--punchconstruct-coopcheck" or "--punchconstruct-adaptcheck" or "--punchconstruct-soupcheck" or "--punchconstruct-facingcheck" or "--punchconstruct-playtest" or "--punchconstruct-launchcheck" or "--punchconstruct-endcheck" or "--punchconstruct-designcheck" or "--punchconstruct-flowcheck" or "--punchconstruct-focuscheck" or "--punchconstruct-dollcheck" or "--punchconstruct-updatecheck" or "--punchconstruct-persistseed" or "--punchconstruct-persistresume" or "--punchconstruct-edgecheck" or "--punchconstruct-eventcheck" or "--punchconstruct-boundarycheck" or "--punchconstruct-reliccheck" or "--punchconstruct-additionscheck" or "--punchconstruct-architectcheck" or "--punchconstruct-observercheck" or "--punchconstruct-observerseed" or "--punchconstruct-observerresume")) return;
        _started = true;
        _tree = __instance.GetTree();
        Callable.From(() => { _ = Run(__instance); }).CallDeferred();
    }
    private static void Check(bool ok, string description)
    {
        if (!ok) throw new InvalidOperationException(description);
        GD.Print($"[Construct QA {_checks++:D3}] {description}");
    }
    private static async Task Frames(int count = 5)
    {
        for (int i = 0; i < count; ++i) await _tree.ToSignal(_tree, SceneTree.SignalName.ProcessFrame);
    }
    private static async Task Until(Func<bool> predicate, string operation, int frames = 1800)
    {
        for (int i = 0; i < frames; ++i) { if (predicate()) return; await Frames(1); }
        throw new TimeoutException(operation);
    }
    private static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren()) foreach (var result in Descendants(child)) yield return result;
    }
    private static async Task Capture(string name)
    {
        if (!OS.GetCmdlineArgs().Contains("--punchconstruct-visual")) return;
        await _tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        _tree.Root.GetTexture().GetImage().SavePng(OS.GetExecutablePath().GetBaseDir() + "/" + name + ".png");
    }
    private static async Task<CardModel> Play<T>(Player player, Creature? target = null, bool upgraded = false) where T : CardModel, new()
    {
        var state = (CombatState)player.Creature.CombatState!;
        var card = state.CreateCard<T>(player);
        if (upgraded) { card.UpgradeInternal(); card.FinalizeUpgradeInternal(); }
        await CardPileCmd.AddGeneratedCardToCombat(card, PileType.Hand, player);
        await PlayerCmd.SetEnergy(20, player);
        Check(card.TryManualPlay(target), "Queue manual play: " + card.Title);
        await Until(() => card.Pile?.Type != PileType.Hand && card.Pile?.Type != PileType.Play, "Card resolution: " + card.Title);
        await Frames(15);
        return card;
    }
    private static async Task NextTurn(Player player, Creature enemy)
    {
        int before = player.PlayerCombatState!.TurnNumber;
        HoldEnemyActions = true;
        try
        {
            PlayerCmd.EndTurn(player, false, () => { enemy.LoseBlockInternal(enemy.Block); return Task.CompletedTask; });
            await Until(() => player.PlayerCombatState.TurnNumber > before && player.PlayerCombatState.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Next player turn");
            await Frames(20);
        }
        finally { HoldEnemyActions = false; }
    }
    private static async Task Run(NMainMenu menu)
    {
        try
        {
            MegaCrit.Sts2.Core.Nodes.Audio.NAudioManager.Instance?.SetMasterVol(0);
            GD.Print("[Construct QA START]");
            GD.Print("[Construct QA DLL] " + Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.IO.File.ReadAllBytes(typeof(PlaytestRunner).Assembly.Location))));
            await QaStartupPreload.Wait(_tree);
            DiagnosticChecks();
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-selectioncheck"))
            {
                await SelectionChecks(menu);
                GD.Print($"[Construct SELECTION PASS] {_checks} checks; native selection buttons, silent audio capture and difficulty unlock");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-boxingcheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await BoxingChecks();
                GD.Print($"[Construct BOXING PASS] {_checks} checks; second paragraph, direct event and combat fixtures");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-animationcheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await AnimationChecks();
                GD.Print($"[Construct ANIMATION PASS] {_checks} checks; original motions/sounds, isolated muted direct combats");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-draftcheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await DraftChecks();
                GD.Print($"[Construct DRAFT PASS] {_checks} checks; first paragraph only, direct event fixtures");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-postercheck"))
            {
                await PosterChecks(menu);
                GD.Print($"[Construct POSTER PASS] {_checks} checks; real selection screen only, muted isolated profile");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-vigorcheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await VigorChecks();
                GD.Print($"[Construct VIGOR PASS] {_checks} checks; direct thorns fixture only");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-modescheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await ModeChecks(menu);
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-coopcheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await CoopChecks();
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-adaptcheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await AdaptationChecks();
                GD.Print($"[Construct ADAPT PASS] {_checks} checks; selected direct room and reward rolls only");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-soupcheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await SoupChecks();
                GD.Print($"[Construct SOUP PASS] {_checks} checks; direct event fixture only");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-facingcheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await FacingChecks();
                GD.Print($"[Construct FACING PASS] {_checks} checks; directly prepared crab combat only");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Any(a => a is "--punchconstruct-observerseed" or "--punchconstruct-observerresume"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await ObserverPersistence(menu, OS.GetCmdlineArgs().Contains("--punchconstruct-observerresume"));
                GD.Print($"[Construct OBSERVER PERSIST PASS] {_checks} checks; no Watcher loaded");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-observercheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await ObserverChecks();
                GD.Print($"[Construct OBSERVER PASS] {_checks} checks; no Watcher loaded");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-architectcheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await ArchitectChecks();
                GD.Print($"[Construct ARCHITECT PASS] {_checks} checks; direct Architect event only");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-additionscheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await AdditionsChecks();
                GD.Print($"[Construct ADDITIONS PASS] {_checks} checks; one direct room");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-reliccheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                if (OS.GetCmdlineArgs().Contains("--punchconstruct-workshop-only"))
                {
                    await WorkshopRelicChecks();
                    GD.Print($"[Construct WORKSHOP RELIC PASS] {_checks} checks; targeted relic room and turn boundaries");
                    _tree.Quit();
                    return;
                }
                if (OS.GetCmdlineArgs().Contains("--punchconstruct-treasure-only"))
                {
                    await ReliabilityRun("TREASUREEXCLUSIONS");
                    TreasureRelicChecks();
                    GD.Print($"[Construct TREASURE PASS] {_checks} checks; native treasure generation only");
                    _tree.Quit();
                    return;
                }
                await RelicChecks();
                GD.Print($"[Construct RELIC PASS] {_checks} checks; one directly prepared combat");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-boundarycheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await BoundaryChecks();
                GD.Print($"[Construct BOUNDARY PASS] {_checks} checks; directly granted items and debug rooms only");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-persistseed") || OS.GetCmdlineArgs().Contains("--punchconstruct-persistresume"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                if (OS.GetCmdlineArgs().Contains("--punchconstruct-persistseed")) await PersistenceSeed();
                else await PersistenceResume(menu);
                GD.Print($"[Construct PERSIST PASS] {_checks} checks; isolated profile, no map progression");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-eventcheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await EventChecks();
                Check(PoolShutdown.ReleaseCards() > 0, "QA shutdown frees only idle cached cards before renderer shutdown");
                await Frames(3);
                GD.Print($"[Construct EVENT PASS] {_checks} checks; directly jumped events only");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-edgecheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await EdgeChecks();
                GD.Print($"[Construct EDGE PASS] {_checks} checks; direct commands only");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-updatecheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await UpdateChecks();
                GD.Print($"[Construct UPDATE PASS] {_checks} checks; one direct test room, no map progression");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-dollcheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await DollChecks();
                GD.Print($"[Construct DOLL PASS] {_checks} checks; direct event only");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-focuscheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await FocusChecks();
                GD.Print($"[Construct FOCUS PASS] {_checks} checks; direct test room, no map progression");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-endcheck"))
            {
                SaveManager.Instance.Progress.EnableFtues = false;
                await Frames(120);
                await EndRunChecks();
                GD.Print($"[Construct QA PASS] {_checks} checks");
                _tree.Quit();
                return;
            }
            bool launchOnly = OS.GetCmdlineArgs().Contains("--punchconstruct-launchcheck");
            if (!launchOnly) SaveManager.Instance.Progress.EnableFtues = false;
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-visual"))
            {
                using var source = ResourceLoader.Load<Texture2D>("res://images/events/punch_off.png").GetImage();
                if (source.IsCompressed()) source.Decompress();
                source.SavePng(OS.GetExecutablePath().GetBaseDir() + "/source-punch-off.png");
            }
            await Frames(120);
            var sub = menu.OpenSingleplayerSubmenu();
            AccessTools.Method(typeof(NSingleplayerSubmenu), "OpenCharacterSelect").Invoke(sub, [null]);
            await Frames(60);
            var select = Descendants(menu).OfType<NCharacterSelectScreen>().Single();
            var characterButton = Descendants(select).OfType<NCharacterSelectButton>().Single(b => b.Character is ConstructCharacter);
            select.SelectCharacter(characterButton, ModelDb.Character<ConstructCharacter>());
            await Frames(90);
            Check(select.Lobby.LocalPlayer.character is ConstructCharacter, "Select the construct through the real character-selection lobby");
            Check(Descendants(select).OfType<TextureRect>().Any(n => n.Name == "Poster" && n.Texture != null), "Selection poster loads during the actual character-select scene");
            await Capture("qa-character-select");
            if (launchOnly)
            {
                foreach (var field in new[] { "_icon", "_iconAdd" })
                {
                    var icon = (TextureRect)AccessTools.Field(typeof(NCharacterSelectButton), field).GetValue(characterButton)!;
                    Check(icon.Texture.ResourcePath == "res://PunchConstruct/art/character_select_icon.ctex" &&
                        icon.Texture.GetSize() == new Vector2(132,195) && icon.StretchMode == TextureRect.StretchModeEnum.Scale &&
                        icon.TextureFilter == CanvasItem.TextureFilterEnum.Linear,
                        "Selection button uses the new 132x195 grey Underdocks portrait without cover cropping: " + field);
                }
                Check(ResourceLoader.Load<Texture2D>(ModelDb.Character<ConstructCharacter>().CustomIconTexturePath).GetSize() == new Vector2(32,32),
                    "Top-panel avatar remains the separate 32x32 portrait");
                GD.Print("[Construct LAUNCH PASS] clean profile can select the construct with the separate portrait strip");
                _tree.Quit();
                return;
            }
            var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<ConstructCharacter>(), true,
                [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "IRONWAVEQA1", GameMode.Standard);
            var player = run.Players.Single();
            Check(player.Deck.Cards.Count == 8, "New run receives eight cards");
            RelicRollChecks();
            VisualIconChecks();
            PreviewChecks(player);
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<PunchConstructNormal>().ToMutable(), showTransition: false);
            await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "First combat play phase");
            await Frames(90);
            await Capture("qa-combat-initial");
            await AdoptedArtChecks(player);
            var state = (CombatState)player.Creature.CombatState!;
            var enemy = state.Enemies.First();
            await CreatureCmd.SetMaxAndCurrentHp(enemy, 1000);
            await CreatureCmd.SetMaxAndCurrentHp(player.Creature, 1000);
            foreach (var power in enemy.Powers.ToArray()) await PowerCmd.Remove(power);
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-flowcheck"))
            {
                await FlowChecks(run, player, enemy);
                GD.Print($"[Construct QA PASS] {_checks} checks");
                _tree.Quit();
                return;
            }
            if (OS.GetCmdlineArgs().Contains("--punchconstruct-designcheck"))
            {
                await NewDesignChecks(run, player, enemy);
                await FlowChecks(run, player, enemy);
                GD.Print($"[Construct QA PASS] {_checks} checks");
                _tree.Quit();
                return;
            }
            var hp = enemy.CurrentHp;
            var block = player.Creature.Block;
            await Play<SmallWave>(player, enemy);
            Check(enemy.CurrentHp == hp - 3 && player.Creature.Block == block + 3, "Small Wave deals 3 and grants 3 in actual manual play");
            await PowerCmd.Apply<StrengthPower>(Context, player.Creature, 2, player.Creature, null);
            await PowerCmd.Apply<DexterityPower>(Context, player.Creature, 3, player.Creature, null);
            hp = enemy.CurrentHp; block = player.Creature.Block;
            await Play<SmallWave>(player, enemy);
            Check(enemy.CurrentHp == hp - 5 && player.Creature.Block == block + 6, "Strength and Dexterity modify damage and block normally");
            await PowerCmd.Remove<StrengthPower>(player.Creature);
            await PowerCmd.Remove<DexterityPower>(player.Creature);
            var smallShockwave = await Play<SmallShockwave>(player, enemy);
            Check(smallShockwave.Pile?.Type == PileType.Exhaust && enemy.GetPowerAmount<WeakPower>() == 1 && enemy.GetPowerAmount<VulnerablePower>() == 1, "Shock exhausts and applies one Weak and Vulnerable");
            await PowerCmd.Remove<WeakPower>(enemy); await PowerCmd.Remove<VulnerablePower>(enemy);
            await CreatureCmd.GainBlock(player.Creature, 50, ValueProp.Unpowered, null);
            var vigor = player.Creature.GetPowerAmount<VigorPower>();
            await CreatureCmd.Damage(Context, player.Creature, 2, ValueProp.Unpowered, enemy);
            await CreatureCmd.Damage(Context, player.Creature, 2, ValueProp.Unpowered, enemy);
            Check(player.Creature.GetPowerAmount<VigorPower>() == vigor + 3, "Guard Stance grants increasing Vigor for separate fully blocked hits");
            await PowerCmd.Remove<VigorPower>(player.Creature);
            await Play<IronWaveForm>(player);
            player.Creature.LoseBlockInternal(player.Creature.Block);
            enemy.GainBlockInternal(3);
            await CreatureCmd.Damage(Context, enemy, 5, ValueProp.Unpowered, player.Creature);
            await CreatureCmd.Damage(Context, enemy, 5, ValueProp.Unpowered, player.Creature);
            await CreatureCmd.Damage(Context, enemy, 7, ValueProp.Unpowered, player.Creature);
            Check(player.Creature.Block == 2, "Iron Wave Form uses unblocked damage and stops after the first hit");
            await Play<IronWaveForm>(player);
            Check(player.Creature.GetPowerAmount<IronWaveFormPower>() == 2, "Two base copies stack into two damage instances");
            block = player.Creature.Block;
            await CreatureCmd.Damage(Context, enemy, 3, ValueProp.Unpowered, player.Creature);
            await CreatureCmd.Damage(Context, enemy, 3, ValueProp.Unpowered, player.Creature);
            await CreatureCmd.Damage(Context, enemy, 3, ValueProp.Unpowered, player.Creature);
            Check(player.Creature.Block == block + 3, "Stacking does not reset consumed hits or multiply each block grant");
            await NextTurn(player, enemy);
            block = player.Creature.Block;
            await CreatureCmd.Damage(Context, enemy, 2, ValueProp.Unpowered, player.Creature);
            Check(player.Creature.Block == block + 2, "Form allowance resets on the next real player turn");
            await PowerCmd.Remove<IronWaveFormPower>(player.Creature);
            player.Creature.LoseBlockInternal(player.Creature.Block);
            enemy.LoseBlockInternal(enemy.Block);
            hp = enemy.CurrentHp;
            await Play<TonyWave>(player);
            Check(enemy.CurrentHp == hp && player.Creature.Block == 0, "Tony Wave grants neither immediate damage nor block");
            await NextTurn(player, enemy);
            Check(enemy.CurrentHp == hp - 7 && player.Creature.Block == 7 && !player.Creature.HasPower<DelayedWavePower>(), "Tony Wave resolves once on the next real turn: random damage 7 and block 7");
            player.Creature.LoseBlockInternal(player.Creature.Block);
            hp = enemy.CurrentHp;
            await Play<TonyWave>(player);
            await Play<TonyWave>(player, upgraded: true);
            await NextTurn(player, enemy);
            Check(enemy.CurrentHp == hp - 16 && player.Creature.Block == 16, "Two delayed waves preserve their separate 7 and 9 values");
            var dummy = await RelicCmd.Obtain<StrikeDummy>(player);
            hp = enemy.CurrentHp; block = player.Creature.Block;
            await Play<SmallWave>(player, enemy);
            Check(enemy.CurrentHp == hp - 6 && player.Creature.Block == block + 3, "Strike Dummy adds 3 damage to Small Wave without adding block");
            var fakeDummy = await RelicCmd.Obtain<FakeStrikeDummy>(player);
            hp = enemy.CurrentHp;
            await Play<SmallWave>(player, enemy);
            Check(enemy.CurrentHp == hp - 7, "Fake Strike Dummy keeps its smaller +1 bonus");
            Check(dummy.DynamicDescription.GetFormattedText().Contains("斩波"), "Adapted relic tooltip describes waves");
            await RelicCmd.Remove(dummy); await RelicCmd.Remove(fakeDummy);
            var seed = await RelicCmd.Obtain<GhostSeed>(player);
            var etherealWave = state.CreateCard<SmallWave>(player);
            await CardPileCmd.AddGeneratedCardToCombat(etherealWave, PileType.Hand, player);
            var plainIron = state.CreateCard<IronWave>(player);
            await CardPileCmd.AddGeneratedCardToCombat(plainIron, PileType.Hand, player);
            Check(etherealWave.Keywords.Contains(CardKeyword.Ethereal) && !plainIron.Keywords.Contains(CardKeyword.Ethereal), "Ghost Seed affects Small Wave only");
            await RelicCmd.Remove(seed);
            var basin = await RelicCmd.Obtain<RippleBasin>(player);
            block = player.Creature.Block;
            await basin.BeforeSideTurnEnd(Context, CombatSide.Player, [player.Creature]);
            Check(player.Creature.Block == block + 4, "Reversed basin grants 4 block after playing an attack");
            await NextTurn(player, enemy);
            block = player.Creature.Block;
            await basin.BeforeSideTurnEnd(Context, CombatSide.Player, [player.Creature]);
            Check(player.Creature.Block == block, "Reversed basin grants no block on a turn without attacks");
            await RelicCmd.Remove(basin);
            var dart = await RelicCmd.Obtain<ConstructDart>(player);
            await Play<SmallWave>(player, enemy);
            Check(player.Creature.GetPowerAmount<DexterityPower>() == 1, "Exclusive dart grants 1 Dexterity for one attack");
            await Play<SmallWave>(player, enemy);
            Check(player.Creature.GetPowerAmount<DexterityPower>() == 2, "Exclusive dart triggers on every attack without a three-card threshold");
            await NextTurn(player, enemy);
            Check(player.Creature.GetPowerAmount<DexterityPower>() == 0, "Dart Dexterity expires at turn end");
            await RelicCmd.Remove(dart);
            var helmet = await RelicCmd.Obtain<ConstructHelmet>(player);
            await PowerCmd.Apply<StrengthPower>(Context, player.Creature, 2, player.Creature, null);
            Check(player.Creature.GetPowerAmount<StrengthPower>() == 2 && player.Creature.GetPowerAmount<DexterityPower>() == 2, "Helmet mirrors the first Strength grant exactly once without a feedback loop");
            await PowerCmd.Apply<StrengthPower>(Context, player.Creature, 1, player.Creature, null);
            Check(player.Creature.GetPowerAmount<DexterityPower>() == 2, "Second Strength grant is not mirrored");
            await PowerCmd.Apply<DexterityPower>(Context, player.Creature, 4, player.Creature, null);
            Check(player.Creature.GetPowerAmount<StrengthPower>() == 3 && player.Creature.GetPowerAmount<DexterityPower>() == 6, "Strength and Dexterity share one mirror allowance for the whole combat");
            var gamma = state.CreateCard<GammaBlast>(player);
            await CardPileCmd.AddGeneratedCardToCombat(gamma, PileType.Hand, player);
            var originalGamma = ModelDb.Card<GammaBlast>();
            Check(gamma.EnergyCost.GetWithModifiers(CostModifiers.None) == 2 && gamma.CurrentStarCost == 0 && gamma.DynamicVars.Damage.BaseValue == originalGamma.DynamicVars.Damage.BaseValue + 3, "Gamma Blast receives only the character-specific cost and damage changes");
            var clonedGamma = gamma.CreateCloneForPlayer(player);
            Check(clonedGamma.DynamicVars.Damage.BaseValue == gamma.DynamicVars.Damage.BaseValue, "Cloning an adapted card does not double the bonus");
            clonedGamma.UpgradeInternal(); clonedGamma.DowngradeInternal();
            Check(clonedGamma.DynamicVars.Damage.BaseValue == gamma.DynamicVars.Damage.BaseValue && clonedGamma.CurrentStarCost == 0, "Adaptation survives upgrade and downgrade without drifting");
            var nativePlayer = Player.CreateForNewRun<Ironclad>(new MegaCrit.Sts2.Core.Unlocks.UnlockState([], [], 0), 9999);
            var nativeGamma = run.CreateCard<GammaBlast>(nativePlayer);
            Check(nativeGamma.CurrentStarCost == originalGamma.CurrentStarCost && nativeGamma.DynamicVars.Damage.BaseValue == originalGamma.DynamicVars.Damage.BaseValue, "Other characters retain original Gamma Blast stats and star cost");
            await PowerCmd.Remove<StrengthPower>(player.Creature); await PowerCmd.Remove<DexterityPower>(player.Creature);
            await RelicCmd.Remove(helmet);
            var summonHp = player.Osty?.CurrentHp ?? 0;
            await OstyCmd.Summon(Context, player, 5, ModelDb.Card<SicEm>());
            Check(player.Osty is { IsAlive: true } && player.Osty.CurrentHp == summonHp + 5, "Summon creates and supports Osty for the custom character");
            await Play<ColdSnap>(player, enemy);
            Check(player.PlayerCombatState!.OrbQueue.Capacity == 1 && player.PlayerCombatState.OrbQueue.Orbs.Any(), "Native non-Defect channel creates its first orb slot automatically");
            await AncientCombatChecks(player, enemy);
            await NewDesignChecks(run, player, enemy);
            await Capture("qa-combat");
            await FlowChecks(run, player, enemy);
            await BaselineBattles();
            await EndRunChecks();
            GD.Print($"[Construct QA PASS] {_checks} checks");
            _tree.Quit();
        }
        catch (Exception ex)
        {
            Diagnostics.Error("qa.failed", ex);
            GD.Print("[Construct QA FAIL] " + ex);
            _tree.Quit(1);
        }
    }

    private static void PreviewChecks(Player player)
    {
        var original = ModelDb.Relic<RippleBasin>();
        string Raw(RelicModel relic) => ((MegaCrit.Sts2.Core.Localization.LocString)AccessTools.PropertyGetter(typeof(RelicModel), "Description").Invoke(relic, [])!).GetRawText();
        string nativeDescription = Raw(original);
        var reward = new RelicReward(original.ToMutable(), player);
        reward.Populate();
        Check(Raw(reward.Relic!).Contains("打出过"), "An unclaimed relic reward already shows the adapted description");
        var shopEntry = new MerchantRelicEntry(original.ToMutable(), player);
        Check(Raw(shopEntry.Model!).Contains("打出过"), "An unpurchased shop relic already shows the adapted description");
        var nativePlayer = Player.CreateForNewRun<Ironclad>(new MegaCrit.Sts2.Core.Unlocks.UnlockState([], [], 0), 999);
        _ = RunState.CreateForTest([nativePlayer]);
        var nativeReward = new RelicReward(original.ToMutable(), nativePlayer);
        nativeReward.Populate();
        Check(Raw(nativeReward.Relic!) == nativeDescription && Raw(original) == nativeDescription, "Preview adaptations leave other players and canonical relics unchanged");
        if (DisplayServer.GetName() != "headless")
            Check(reward.Relic!.Icon.GetInstanceId() != original.Icon.GetInstanceId() && nativeReward.Relic!.Icon.GetInstanceId() == original.Icon.GetInstanceId(), "Only the construct's preview receives the transformed icon");
    }

    private static async Task AncientCombatChecks(Player player, Creature enemy)
    {
        foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
        foreach (var power in enemy.Powers.ToArray()) await PowerCmd.Remove(power);
        enemy.LoseBlockInternal(enemy.Block);
        var guard = player.Relics.OfType<GuardStance>().Single();
        await RelicCmd.Replace(guard, ModelDb.Relic<AncientGuardStance>().ToMutable());
        var strength = player.Creature.GetPowerAmount<StrengthPower>();
        await CreatureCmd.Damage(Context, player.Creature, 1, ValueProp.Unpowered, enemy);
        Check(player.Creature.GetPowerAmount<StrengthPower>() == strength && player.Creature.GetPowerAmount<GuardStrengthNextTurnPower>() == 1,
            "Actual ancient replacement queues one next-turn temporary Strength instead of granting permanent Strength");
        await PowerCmd.Remove<GuardStrengthNextTurnPower>(player.Creature);
        await RelicCmd.Replace(player.Relics.OfType<AncientGuardStance>().Single(), ModelDb.Relic<GuardStance>().ToMutable());
        await PowerCmd.Remove<VigorPower>(player.Creature);
    }

    private static void RelicRollChecks()
    {
        int Sample<T>(bool back, bool banned = false) where T : CharacterModel, new()
        {
            var p = Player.CreateForNewRun<T>(new MegaCrit.Sts2.Core.Unlocks.UnlockState([], [], 0), 1234);
            _ = RunState.CreateForTest([p], seed: "RELICWEIGHTS");
            int count = 0;
            var bagField = AccessTools.Field(typeof(Player), "<RelicGrabBag>k__BackingField");
            for (ulong i = 0; i < 1200; ++i)
            {
                var bag = new RelicGrabBag();
                bag.Populate(banned ? [ModelDb.Relic<Orichalcum>(), ModelDb.Relic<BronzeScales>()] : [ModelDb.Relic<Anchor>(), ModelDb.Relic<BronzeScales>()], new Rng(i));
                bagField.SetValue(p, bag);
                var relic = back ? RelicFactory.PullNextRelicFromBack(p, RelicRarity.Common, _ => true) : RelicFactory.PullNextRelicFromFront(p, RelicRarity.Common);
                if (relic is BronzeScales) ++count;
            }
            return count;
        }
        int weighted = Sample<ConstructCharacter>(false), shop = Sample<ConstructCharacter>(true), native = Sample<Ironclad>(false);
        GD.Print($"[Construct QA weights] construct {weighted}/1200, shop {shop}/1200, vanilla {native}/1200");
        Check(weighted is > 720 and < 880 && shop is > 720 and < 880, "Actual reward and shop relic draws implement the provisional two-to-one weight");
        Check(native is > 530 and < 670, "Vanilla character relic draws remain evenly distributed");
        Check(Sample<ConstructCharacter>(false, true) == 1200 && Sample<ConstructCharacter>(true, true) == 1200, "Orichalcum is excluded from both reward and shop draw paths");
        int ChemicalDraws<T>(bool back, bool onlyChemical = false) where T : CharacterModel, new()
        {
            var p = Player.CreateForNewRun<T>(new MegaCrit.Sts2.Core.Unlocks.UnlockState([], [], 0), 1234);
            _ = RunState.CreateForTest([p], seed: "CHEMICALXEXCLUSION");
            int count = 0, eligible = 0;
            for (ulong i = 0; i < 64; ++i)
            {
                var bag = new RelicGrabBag();
                bag.Populate(onlyChemical ? [ModelDb.Relic<ChemicalX>()] : [ModelDb.Relic<ChemicalX>(), ModelDb.Relic<Bread>()], new Rng(i));
                AccessTools.Field(typeof(Player), "<RelicGrabBag>k__BackingField").SetValue(p, bag);
                var relic = back ? RelicFactory.PullNextRelicFromBack(p, RelicRarity.Shop, _ => true)
                    : RelicFactory.PullNextRelicFromFront(p, RelicRarity.Shop);
                if (onlyChemical ? relic?.Id == RelicFactory.FallbackRelic.Id : relic is Bread) ++eligible;
                if (relic is ChemicalX) ++count;
            }
            if (typeof(T) == typeof(ConstructCharacter))
                Check(eligible == 64, onlyChemical ? "An excluded-only relic bag uses the native fallback instead of Chemical X"
                    : "Chemical X filtering preserves the eligible shop relic in all 64 draws");
            return count;
        }
        Check(ChemicalDraws<ConstructCharacter>(false) == 0 && ChemicalDraws<ConstructCharacter>(true) == 0,
            "Chemical X is excluded from both construct relic draw directions");
        ChemicalDraws<ConstructCharacter>(true, onlyChemical: true);
        ChemicalDraws<ConstructCharacter>(false, onlyChemical: true);
        Check(ChemicalDraws<Ironclad>(false) > 0 && ChemicalDraws<Ironclad>(true) > 0,
            "Other characters can still obtain Chemical X through both draw directions");
        Check(RelicWeights.Preferred.Count == 13 && RelicWeights.Weight(ModelDb.Relic<HelicalDart>()) == 1, "Exactly the approved thirteen relics receive extra weight");
    }

    private static void VisualIconChecks()
    {
        if (DisplayServer.GetName() == "headless") return;
        using var pixels = RelicIcons.ReadPixels(ModelDb.Relic<GuardStance>().Icon);
        Check(pixels.GetWidth() > 0 && pixels.GetData().Any(b => b != 0), "Derived relic icon has visible pixels even when its source is a compressed atlas");
    }

    private static async Task AdoptedArtChecks(Player player)
    {
        foreach (var (name, size) in new[] {
            ("portrait", new Vector2(32, 32)), ("character_select", new Vector2(1920, 1080)),
            ("small_wave", new Vector2(1000, 760)), ("iron_wave_form", new Vector2(1000, 760)),
            ("tony_wave", new Vector2(1000, 760)), ("forge_workshop", new Vector2(1000, 760)),
            ("royal_bearing", new Vector2(1000, 760)), ("strike_while_hot", new Vector2(1000, 760)),
            ("final_judgment", new Vector2(1000, 760)), ("unarm_to_arms", new Vector2(1000, 760)),
            ("doom_resonance", new Vector2(1000, 760)), ("never_back_down", new Vector2(1000, 760)),
            ("boxing_champion_form", new Vector2(606, 852)),
            ("guard_stance", new Vector2(128, 128)), ("guard_stance_big", new Vector2(256, 256)) })
        {
            var texture = ResourceLoader.Load<Texture2D>($"res://PunchConstruct/art/{name}.ctex");
            Check(texture != null && texture.GetSize() == size, "Adopted art decodes at target size: " + name);
        }
        var icon = Descendants(_tree.Root).OfType<TextureRect>().Single(n => n.Name == "ConstructIcon");
        Check(icon.Size.X > 0 && icon.Size == ((Control)icon.GetParent()).Size && icon.Size.X < 96,
            "Top-bar portrait fills its native container without a fixed 96px overflow");
        Check(icon.Texture.GetSize() == new Vector2(32, 32) && icon.TextureFilter == CanvasItem.TextureFilterEnum.Linear,
            "Top-bar avatar uses the real 32px texture and nearest filtering");
        Check(Descendants(_tree.Root).OfType<NCreatureVisuals>().Any(v => v.HasMeta("punch_construct") && v.FormVfxHolder != null),
            "Construct visuals provide the native form-effect holder needed by death cleanup");
        if (!OS.GetCmdlineArgs().Contains("--punchconstruct-visual")) return;
        var layer = new CanvasLayer { Layer = 100 };
        _tree.Root.AddChild(layer);
        var area = _tree.Root.GetVisibleRect().Size;
        var cards = new CardModel[] { player.RunState.CreateCard<SmallWave>(player), player.RunState.CreateCard<IronWaveForm>(player) };
        for (int i = 0; i < cards.Length; i++)
        {
            var card = NCard.Create(cards[i])!;
            layer.AddChild(card);
            card.Position = new Vector2(area.X * (.35f + .3f * i), area.Y * .48f);
            card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
        }
        await Frames(30);
        await Capture("qa-adopted-cards");
        var more = new CardModel[] {
            player.RunState.CreateCard<TonyWave>(player), player.RunState.CreateCard<ForgeWorkshop>(player), player.RunState.CreateCard<RoyalBearing>(player),
            player.RunState.CreateCard<StrikeWhileHot>(player), player.RunState.CreateCard<FinalJudgment>(player), player.RunState.CreateCard<UnarmToArms>(player),
            player.RunState.CreateCard<DoomResonance>(player), player.RunState.CreateCard<NeverBackDown>(player), player.RunState.CreateCard<BoxingChampionForm>(player) };
        for (int page = 0; page < 3; page++)
        {
            foreach (var child in layer.GetChildren()) { layer.RemoveChild(child); child.QueueFree(); }
            for (int i = 0; i < 3; i++)
            {
                var card = NCard.Create(more[page * 3 + i])!;
                layer.AddChild(card);
                card.Position = new Vector2(area.X * (.23f + .27f * i), area.Y * .48f);
                card.UpdateVisuals(PileType.None, CardPreviewMode.Normal);
            }
            await Frames(15);
            await Capture("qa-adopted-cards-" + (page + 2));
        }
        layer.QueueFree();
        await Frames();
    }

    private static async Task EndRunChecks()
    {
        foreach (bool abandon in new[] { true, false })
        {
            string label = abandon ? "abandon" : "lethal enemy damage";
            if (RunManager.Instance.IsInProgress) await NGame.Instance!.ReturnToMainMenu();
            var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<ConstructCharacter>(), true,
                [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "ENDCHECK" + abandon, GameMode.Standard);
            var player = run.Players.Single();
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<NibbitsWeak>().ToMutable(), showTransition: false);
            await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "End-flow fixture ready");
            var visualActor = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance!.GetCreatureNode(player.Creature)
                ?? throw new InvalidOperationException("Player visual missing from death fixture");
            await Capture("qa-deathvisual-" + abandon + "-before");
            if (abandon)
            {
                RunManager.Instance.Abandon();
                await ObserveDeathVisuals(visualActor, abandon.ToString());
            }
            else
            {
                await CreatureCmd.SetCurrentHp(player.Creature, 1);
                player.Creature.LoseBlockInternal(player.Creature.Block);
                var lethal = CreatureCmd.Damage(Context, player.Creature, 999, ValueProp.Unpowered, ((CombatRoom)run.CurrentRoom!).CombatState.Enemies.First());
                await ObserveDeathVisuals(visualActor, abandon.ToString());
                await lethal;
            }
            await Until(() => Descendants(_tree.Root).OfType<NGameOverScreen>().Any(), "Game-over screen after " + label);
            await Frames(180);
            GD.Print($"[Construct END state] {label}: dead={player.Creature.IsDead}, runOver={run.IsGameOver}, managerOver={RunManager.Instance.IsGameOver}, combat={CombatManager.Instance.IsInProgress}, ending={CombatManager.Instance.IsOverOrEnding}");
            Check(player.Creature.IsDead && run.IsGameOver && CombatManager.Instance.IsOverOrEnding, "Death ends the combat and run: " + label);
            Check(!SaveManager.Instance.LoadRunSave().Success, "Finished run has no resumable current-run save: " + label);
            await Capture(abandon ? "qa-abandon-result" : "qa-death-result");
            var screen = Descendants(_tree.Root).OfType<NGameOverScreen>().Single();
            AccessTools.Method(typeof(NGameOverScreen), "ReturnToMainMenu").Invoke(screen, []);
            await Until(() => NGame.Instance.MainMenu != null && !Descendants(_tree.Root).OfType<NGameOverScreen>().Any(), "Return to main menu after " + label);
            await Frames(60);
            Check(!RunManager.Instance.IsInProgress && !CombatManager.Instance.IsInProgress, "Main menu has no running combat: " + label);
        }
        var fresh = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<ConstructCharacter>(), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "AFTERDEATH", GameMode.Standard);
        var freshPlayer = fresh.Players.Single();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<NibbitsWeak>().ToMutable(), showTransition: false);
        await Until(() => freshPlayer.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "New combat after death");
        Check(freshPlayer.Creature.IsAlive && freshPlayer.Deck.Cards.Count == 8 && !RunManager.Instance.IsGameOver && !RunManager.Instance.IsAbandoned,
            "Fresh run after both endings has a live character, clean deck and playable combat");
        if (OS.GetCmdlineArgs().Contains("--punchconstruct-visual"))
        {
            var nativeVisual = ResourceLoader.Load<PackedScene>("res://scenes/creature_visuals/punch_construct.tscn")
                .Instantiate<NCreatureVisuals>();
            nativeVisual.Position = new Vector2(-3000, -3000);
            _tree.Root.AddChild(nativeVisual);
            var nativeAnimator = ModelDb.Monster<MegaCrit.Sts2.Core.Models.Monsters.PunchConstruct>().GenerateAnimator(nativeVisual.SpineBody!);
            nativeAnimator.SetTrigger("Dead");
            await Frames(20);
            Check(nativeVisual.SpineBody!.GetSkeleton()!.FindBone("root")!.BoundObject.Call("get_scale_x").AsSingle() < 0,
                "Original enemy Punch Construct keeps its unmodified death animation");
            var freshVisual = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance!.GetCreatureNode(freshPlayer.Creature)!.Visuals;
            Check(freshVisual.Body.GlobalTransform.Determinant() < 0 && freshVisual.SpineBody!.GetSkeleton()!.FindBone("root")!.BoundObject.Call("get_scale_x").AsSingle() > 0,
                "Fresh player after death retains the normal idle facing");
            nativeVisual.QueueFree();
        }
        await NGame.Instance.ReturnToMainMenu();
        SaveManager.Instance.DeleteCurrentRun();
        GD.Print("[Construct END FLOW PASS] abandon, lethal damage, result screen, save removal, menu, fresh combat");
    }

    private static async Task ObserveDeathVisuals(NCreature actor, string label)
    {
        if (!OS.GetCmdlineArgs().Contains("--punchconstruct-visual")) return;
        bool sawDeath = false;
        bool keptFacing = true;
        float minRootScale = float.MaxValue;
        var originalScale = actor.Visuals.Body.Scale;
        for (int frame = 0; frame <= 90; frame++)
        {
            var body = actor.Visuals.Body;
            var spine = actor.Visuals.SpineBody ?? throw new InvalidOperationException("Player Spine body missing");
            var rootBone = spine.GetSkeleton()?.FindBone("root")?.BoundObject;
            if (spine.GetAnimationState().GetCurrentAnimationName() == "die")
            {
                sawDeath = true;
                minRootScale = Math.Min(minRootScale, rootBone!.Call("get_scale_x").AsSingle());
                keptFacing &= body.Scale == originalScale;
            }
            if (frame % 5 == 0)
            {
                var bone = spine.GetSkeleton()?.FindBone("root")?.BoundObject;
                string root = bone == null ? "no root" : string.Join(" ", new[] { "get_scale_x", "get_scale_y", "get_rotation" }
                    .Where(m => bone.HasMethod(m)).Select(m => m + "=" + bone.Call(m)));
                GD.Print($"[Construct DEATH FRAME] {label} {frame}: anim={spine.GetAnimationState().GetCurrentAnimationName()} body={body.Scale} rotation={body.Rotation} visuals={actor.Visuals.Scale} {root}");
            }
            if (frame is 0 or 10 or 30 or 60) await Capture($"qa-deathvisual-{label}-{frame:D2}");
            await Frames(1);
        }
        Check(sawDeath && minRootScale > 0 && keptFacing, "Death keeps both root and scene facing throughout 91 sampled frames: " + label);
    }

    private static async Task DollChecks()
    {
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<ConstructCharacter>(), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "WINDDOLL", GameMode.Standard);
        var player = run.Players.Single();
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<DollRoom>(), showTransition: false);
        var doll = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
        await (Task)AccessTools.Method(typeof(DollRoom), "ChooseRandom").Invoke(doll, [])!;
        Check(player.Relics.Any(r => r is DaughterOfTheWind), "Random doll choice actually grants Daughter of the Wind");
        string title = ModelDb.Relic<DaughterOfTheWind>().Title.GetFormattedText();
        foreach (var (method, count) in new[] { ("TakeSomeTime", 2), ("Examine", 3) })
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<DollRoom>(), showTransition: false);
            doll = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
            await (Task)AccessTools.Method(typeof(DollRoom), method).Invoke(doll, [])!;
            Check(doll.CurrentOptions.Count == count && doll.CurrentOptions[0].Title.GetFormattedText() == title,
                method + " shows " + count + " options with Daughter of the Wind first");
        }
        await NGame.Instance.ReturnToMainMenu();
        SaveManager.Instance.DeleteCurrentRun();
    }

    private sealed class UpdateSelector : MegaCrit.Sts2.Core.TestSupport.ICardSelector
    {
        internal readonly List<CardModel[]> Options = [];
        public Task<IEnumerable<CardModel>> GetSelectedCards(IEnumerable<CardModel> options, int minSelect, int maxSelect)
        {
            var cards = options.ToArray();
            Options.Add(cards);
            return Task.FromResult<IEnumerable<CardModel>>(cards.Take(Math.Max(1, minSelect)));
        }
        public MegaCrit.Sts2.Core.TestSupport.CardRewardSelection GetSelectedCardReward(
            IReadOnlyList<CardCreationResult> options,
            IReadOnlyList<MegaCrit.Sts2.Core.Entities.CardRewardAlternatives.CardRewardAlternative> alternatives)
            => throw new InvalidOperationException("Unexpected reward selector in targeted relic checks");
    }

    private static async Task UpdateChecks()
    {
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<ConstructCharacter>(), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "POOLANDCARDS", GameMode.Standard);
        var player = run.Players.Single();
        Check(player.Deck.Cards.Count == 8 && player.Deck.Cards.Count(c => c is SmallWave) == 5,
            "New run starts with five Small Waves and eight cards total");
        var native = Player.CreateForNewRun<Ironclad>(new MegaCrit.Sts2.Core.Unlocks.UnlockState([], [], 0), 9999);
        foreach (var card in new CardModel[] { ModelDb.Card<Neutralize>(), ModelDb.Card<FallingStar>(), ModelDb.Card<Unleash>() })
            Check(run.CreateCard(card, player).Rarity == CardRarity.Common && run.CreateCard(card, native).Rarity == CardRarity.Basic,
                "Imported basic becomes common only for construct: " + card.Title);
        var colorlessPool = ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.ColorlessCardPool>();
        var colorlessOptions = CardCreationOptions.ForNonCombatWithUniformOdds([colorlessPool]);
        var candidates = colorlessOptions.GetPossibleCards(player).ToArray();
        Check(candidates.Length == 11 && candidates.All(c => c.Pool == colorlessPool && player.Character.CardPool.AllCardIds.Contains(c.Id) && !BoxingGeneration.Restricted(c)),
            "Colorless sources offer eleven allowed cards and exclude the four fixed-source boxing cards");
        Check(candidates.Count(c => c is IronWaveForm or NeverBackDown) == 2 && candidates.Any(c => c is FightMe) && candidates.Any(c => c is BattleFervor),
            "Selected colorless cards include Form, Never Back Down, Fight Me and Battle Fervor");
        var nativeRun = RunState.CreateForTest([native], [ModelDb.Act<Overgrowth>()]);
        Check(colorlessOptions.GetPossibleCards(native).Any(c => !player.Character.CardPool.AllCardIds.Contains(c.Id)),
            "Other characters still use the original full colorless candidates");
        var curseOptions = CardCreationOptions.ForNonCombatWithUniformOdds([ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.CurseCardPool>()]);
        Check(curseOptions.GetPossibleCards(player).Any(c => c.Type == CardType.Curse),
            "Explicit curse-card sources remain available outside the normal character pool");
        for (int i = 0; i < 12; i++)
            Check(CardFactory.CreateForReward(player, 2, colorlessOptions).All(r => candidates.Any(c => c.Id == r.Card.Id)),
                "Colorless reward draw stays within the selected eleven: " + i);
        var shop = MerchantInventory.CreateForNormalMerchant(player);
        Check(shop.ColorlessCardEntries.Count == 2 && shop.ColorlessCardEntries.All(e => e.CreationResult?.Card is { } c && candidates.Any(v => v.Id == c.Id)),
            "Actual colorless shop slots contain only selected colorless cards");
        var selector = new UpdateSelector();
        using (CardSelectCmd.UseSelector(selector))
        {
            int count = player.Deck.Cards.Count;
            await RelicCmd.Obtain(ModelDb.Relic<LeadPaperweight>().ToMutable(), player);
            Check(selector.Options.Count == 1 && selector.Options[0].Length == 2 &&
                selector.Options[0].All(c => candidates.Any(v => v.Id == c.Id)) && player.Deck.Cards.Count == count + 1,
                "Actual Lead Paperweight offers two valid candidates and grants the chosen card");
            count = player.Deck.Cards.Count;
            await RelicCmd.Obtain(ModelDb.Relic<SeaGlass>().ToMutable(), player);
            var sea = selector.Options.Last();
            Check(sea.Length == 15 && sea.Select(c => c.Id).Distinct().Count() == 15 && sea.All(CardAcquisition.IsSelectedImport),
                "Actual Sea Glass offers fifteen distinct selected imports");
            Check(sea.Count(c => c.Pool is MegaCrit.Sts2.Core.Models.CardPools.RegentCardPool) == 5 &&
                sea.Count(c => c.Pool is MegaCrit.Sts2.Core.Models.CardPools.NecrobinderCardPool) == 5 &&
                sea.Count(c => c.Pool is MegaCrit.Sts2.Core.Models.CardPools.DefectCardPool) == 5 && player.Deck.Cards.Count == count + 1,
                "Sea Glass has exactly five Regent, five Necrobinder, five Defect and grants selection");
        }
        Check(!CardAcquisition.SeaGlassSelection, "Sea Glass bypass ends after candidate construction");
        int deckCount = player.Deck.Cards.Count, maxHp = player.Creature.MaxHp;
        var smalls = player.Deck.Cards.OfType<SmallWave>().ToArray();
        var leaf = ModelDb.Relic<LeafyPoultice>().ToMutable();
        await RelicCmd.Obtain(leaf, player);
        Check(player.Deck.Cards.Count == deckCount && player.Deck.Cards.Count(c => c is SmallWave) == smalls.Length - 2 &&
            player.Creature.MaxHp == maxHp - leaf.DynamicVars.MaxHp.IntValue,
            "Actual Leafy Poultice transforms two different Small Waves and pays the native max-HP cost");
        Check(player.Deck.Cards.Where(c => !smalls.Contains(c)).All(c => player.Character.CardPool.AllCardIds.Contains(c.Id)),
            "Leafy Poultice replacements stay inside the construct card pool");
        Check(leaf.DynamicDescription.GetFormattedText().Contains("小斩波") && leaf.DynamicEventDescription.GetFormattedText().Contains("小斩波"),
            "Leafy Poultice actual and event-preview descriptions show the adapted targets");

        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<NibbitsWeak>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Direct card check room ready", 600);
        var enemy = player.Creature.CombatState!.HittableEnemies.First();
        await CreatureCmd.SetMaxAndCurrentHp(enemy, 1000);
        foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
        foreach (var power in enemy.Powers.ToArray()) await PowerCmd.Remove(power);
        enemy.LoseBlockInternal(enemy.Block);
        var generated = CardFactory.GetDistinctForCombat(player, colorlessPool.GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint), 20, new Rng(91)).ToArray();
        Check(generated.Length == 11 && generated.Select(c => c.Id).ToHashSet().SetEquals(candidates.Select(c => c.Id)),
            "Combat colorless generator includes exactly the eleven allowed cards and safely limits distinct count");
        var randomCards = CardFactory.GetDistinctForCombat(player, ModelDb.AllCards.Where(c => c.Rarity is CardRarity.Common or CardRarity.Uncommon or CardRarity.Rare), 12, new Rng(92)).ToArray();
        Check(randomCards.Length == 12 && randomCards.All(c => CardAcquisition.Eligible(player, c)),
            "Combat random generation cannot escape the selected pool or bypass group gates");
        var repeated = CardFactory.GetForCombat(player, colorlessPool.GetUnlockedCards(player.UnlockState, player.RunState.CardMultiplayerConstraint), 15, new Rng(93)).ToArray();
        Check(repeated.Length == 15 && repeated.All(c => candidates.Any(v => v.Id == c.Id)),
            "With-replacement combat generation also stays inside selected colorless cards");
        Check(run.CreateCard<MadScience>(player).Rarity == CardRarity.Event,
            "Explicit Mad Science event-card creation remains available");
        player.Creature.LoseBlockInternal(player.Creature.Block);
        var form = await Play<IronWaveForm>(player);
        enemy.GainBlockInternal(3);
        await CreatureCmd.Damage(Context, enemy, 5, ValueProp.Unpowered, player.Creature);
        await CreatureCmd.Damage(Context, enemy, 5, ValueProp.Unpowered, player.Creature);
        Check(player.Creature.Block == 2 && player.Creature.GetPowerAmount<IronWaveFormPower>() == 1,
            "Base form only triggers on first damage instance and uses unblocked damage");
        await PowerCmd.Remove<IronWaveFormPower>(player.Creature);
        player.Creature.LoseBlockInternal(player.Creature.Block);
        form = await Play<IronWaveForm>(player, upgraded: true);
        await CreatureCmd.Damage(Context, enemy, 3, ValueProp.Unpowered, player.Creature);
        await CreatureCmd.Damage(Context, enemy, 4, ValueProp.Unpowered, player.Creature);
        await CreatureCmd.Damage(Context, enemy, 5, ValueProp.Unpowered, player.Creature);
        Check(player.Creature.Block == 7 && !form.Keywords.Contains(CardKeyword.Retain),
            "Upgraded form triggers on first two instances and no longer gains retain");
        await PowerCmd.Remove<IronWaveFormPower>(player.Creature);
        var hp = enemy.CurrentHp;
        var fight = await Play<FightMe>(player, enemy);
        Check(enemy.CurrentHp == hp - 12 && player.Creature.GetPowerAmount<DexterityPower>() == 2 && enemy.GetPowerAmount<StrengthPower>() == 1 && fight.EnergyCost.Canonical == 2,
            "Fight Me actually deals 6x2, grants 2 Dexterity and gives target 1 Strength at 2 cost");
        await PowerCmd.Remove<DexterityPower>(player.Creature);
        await PowerCmd.Remove<StrengthPower>(enemy);
        hp = enemy.CurrentHp;
        await Play<FightMe>(player, enemy, upgraded: true);
        Check(enemy.CurrentHp == hp - 12 && player.Creature.GetPowerAmount<DexterityPower>() == 3 && enemy.GetPowerAmount<StrengthPower>() == 1,
            "Fight Me+ keeps 6x2 damage, grants 3 Dexterity and still gives target 1 Strength");
        await PowerCmd.Remove<DexterityPower>(player.Creature);
        var royal = await Play<RoyalBearing>(player);
        var blade = player.PlayerCombatState!.Hand.Cards.OfType<SovereignBlade>().Single();
        var bladeDamage = blade.DynamicVars.Damage.BaseValue;
        Check(royal.Rarity == CardRarity.Rare && bladeDamage == ModelDb.Card<SovereignBlade>().DynamicVars.Damage.BaseValue + 3 && player.Creature.GetPowerAmount<RoyalBearingPower>() == 4,
            "Gold Royal Bearing first forges 3 then installs 4 end-turn block");
        await Play<RoyalBearing>(player, upgraded: true);
        Check(blade.DynamicVars.Damage.BaseValue == bladeDamage + 5 && player.Creature.GetPowerAmount<RoyalBearingPower>() == 10 && ModelDb.Card<DoomResonance>().Rarity == CardRarity.Rare,
            "Royal Bearing+ forges 5 and adds 6 end-turn block; Doom Resonance is gold");
    }

    private static async Task FocusChecks()
    {
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<ConstructCharacter>(), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "IRONWAVEFOCUS", GameMode.Standard);
        var player = run.Players.Single();
        var iron = player.Deck.Cards.First(c => c is IronWave);
        var native = Player.CreateForNewRun<Ironclad>(new MegaCrit.Sts2.Core.Unlocks.UnlockState([], [], 0), 9999);
        Check(iron.PortraitPath == "res://PunchConstruct/art/iron_wave.ctex" && iron.Portrait != null &&
            run.CreateCard<IronWave>(native).PortraitPath != iron.PortraitPath, "New Iron Wave art loads only for the construct");
        var ancient = ModelDb.Relic<AncientGuardStance>();
        Check(ancient.PackedIconPath != ModelDb.Relic<GuardStance>().PackedIconPath && ancient.Icon != null && ancient.BigIcon != null && ancient.IconOutline != null,
            "Ancient Guard Stance has its own v0.1 icon, large icon and outline");
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Targeted combat ready", 600);
        var enemies = player.Creature.CombatState!.HittableEnemies.ToArray();
        Check(enemies.Length > 1, "Direct test room provides multiple targets for Shock");
        foreach (var enemy in enemies)
        {
            foreach (var power in enemy.Powers.ToArray()) await PowerCmd.Remove(power);
        }
        var shock = await Play<SmallShockwave>(player);
        Check(shock.Pile?.Type == PileType.Exhaust && enemies.All(e => e.GetPowerAmount<WeakPower>() == 1 && e.GetPowerAmount<VulnerablePower>() == 1),
            "Shock applies one Vulnerable and Weak to every enemy and exhausts");
        foreach (var enemy in enemies) { await PowerCmd.Remove<WeakPower>(enemy); await PowerCmd.Remove<VulnerablePower>(enemy); }
        shock = await Play<SmallShockwave>(player, upgraded: true);
        Check(shock.Pile?.Type == PileType.Exhaust && enemies.All(e => e.GetPowerAmount<WeakPower>() == 2 && e.GetPowerAmount<VulnerablePower>() == 2),
            "Shock+ applies two Vulnerable and Weak to every enemy and still exhausts");
        var target = player.Creature;
        var starter = player.Relics.OfType<GuardStance>().Single();
        await CreatureCmd.GainBlock(target, 20, ValueProp.Unpowered, null);
        var blockedResult = (await CreatureCmd.Damage(Context, target, 2, ValueProp.Unpowered, enemies[0])).Single();
        await GuardStanceTiming<VigorPower>(starter, blockedResult, enemies[0]);
        await starter.BeforeCombatStart();
        await PowerCmd.Remove<VigorPower>(target);
        int vigor = target.GetPowerAmount<VigorPower>();
        await CreatureCmd.Damage(Context, target, 2, ValueProp.Unpowered, enemies[0]);
        await CreatureCmd.Damage(Context, target, 2, ValueProp.Unpowered, enemies[0]);
        Check(target.GetPowerAmount<VigorPower>() == vigor + 3 && starter.DisplayAmount == 3,
            "Starter's first two blocked hits grant one then two Vigor, and display next grant three");
        await NextTurn(player, enemies[0]);
        await CreatureCmd.GainBlock(target, 20, ValueProp.Unpowered, null);
        await CreatureCmd.Damage(Context, target, 2, ValueProp.Unpowered, enemies[0]);
        Check(target.GetPowerAmount<VigorPower>() == vigor + 6 && starter.DisplayAmount == 4,
            "Starter continues at three on the next turn of the same combat");
        target.LoseBlockInternal(target.Block); target.GainBlockInternal(1);
        await CreatureCmd.Damage(Context, target, 2, ValueProp.Unpowered, enemies[0]);
        Check(target.GetPowerAmount<VigorPower>() == vigor + 6 && starter.DisplayAmount == 4,
            "Partial block neither grants Vigor nor advances its growth");
        async Task RestartCombat()
        {
            foreach (var e in player.Creature.CombatState!.HittableEnemies.ToArray()) await CreatureCmd.SetCurrentHp(e, 1);
            await Play<Thunderclap>(player);
            await Until(() => !CombatManager.Instance.IsInProgress, "Debug-prepared encounter ends");
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
            await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Next direct combat ready");
            enemies = player.Creature.CombatState!.HittableEnemies.ToArray();
        }
        await RestartCombat();
        Check(starter.DisplayAmount == 1, "Starter resets its grant to one in the next actual combat");
        await CreatureCmd.GainBlock(target, 20, ValueProp.Unpowered, null);
        await CreatureCmd.Damage(Context, target, 2, ValueProp.Unpowered, enemies[0]);
        Check(target.GetPowerAmount<VigorPower>() == 1 && starter.DisplayAmount == 2,
            "New-combat starter actually grants one Vigor again");
        var touch = (TouchOfOrobas)ModelDb.Relic<TouchOfOrobas>().ToMutable();
        Check(touch.SetupForPlayer(player) && touch.UpgradedRelic == ancient.Id, "Touch of Orobas selects Ancient Guard Stance through the native setup");
        await RelicCmd.Obtain(touch, player);
        Check(player.Relics.Any(r => r is AncientGuardStance) && player.Relics.All(r => r is not GuardStance), "Actually acquiring Touch replaces the starter with its ancient version");
        var upgradedRelic = player.Relics.OfType<AncientGuardStance>().Single();
        int strength = target.GetPowerAmount<StrengthPower>();
        vigor = target.GetPowerAmount<VigorPower>();
        await CreatureCmd.Damage(Context, target, 2, ValueProp.Unpowered, enemies[0]);
        await CreatureCmd.Damage(Context, target, 2, ValueProp.Unpowered, enemies[0]);
        Check(target.GetPowerAmount<StrengthPower>() == strength && target.GetPowerAmount<GuardStrengthNextTurnPower>() == 3 && target.GetPowerAmount<VigorPower>() == vigor &&
            !target.HasPower<DrawCardsNextTurnPower>() && upgradedRelic.DisplayAmount == 3,
            "Ancient first two blocked hits queue one then two temporary Strength, with no immediate Strength, Vigor or draw");
        var brassNut = Godot.ResourceLoader.Load<Godot.Texture2D>(target.GetPower<GuardStrengthNextTurnPower>()!.PackedIconPath);
        Check(brassNut != null && brassNut.GetSize() == new Vector2(64, 64) &&
            brassNut.GetImage().GetData().Any(b => b != 0) && brassNut.GetImage().GetPixel(0, 0).A == 0 &&
            target.GetPower<GuardStrengthNextTurnPower>()!.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description.Contains("3"),
            "Brass-nut power icon loads at 64px with transparent margins; pending amount and tooltip remain available");
        target.LoseBlockInternal(target.Block); target.GainBlockInternal(1);
        await CreatureCmd.Damage(Context, target, 2, ValueProp.Unpowered, enemies[0]);
        Check(target.GetPowerAmount<StrengthPower>() == strength && target.GetPowerAmount<GuardStrengthNextTurnPower>() == 3 && upgradedRelic.DisplayAmount == 3,
            "Partial block neither queues temporary Strength nor advances ancient growth");
        await PowerCmd.Apply<StrengthPower>(Context, target, 2, target, null);
        strength += 2;
        var indicator = target.GetPower<GuardStrengthNextTurnPower>()!;
        await Frames(2);
        var indicatorUi = Descendants(_tree.Root).OfType<NPower>().Single(p => p.Model == indicator);
        ulong indicatorId = indicatorUi.GetInstanceId();
        await NextTurn(player, enemies[0]);
        Check(player.PlayerCombatState!.Hand.Cards.Count == 5 && !target.HasPower<DrawCardsNextTurnPower>() &&
            target.GetPowerAmount<StrengthPower>() == strength + 3 && target.GetPowerAmount<GuardTemporaryStrengthPower>() == 3 &&
            indicator.ActiveAmount == 3 && indicator.PendingAmount == 0 && target.GetPower<GuardStrengthNextTurnPower>() == indicator,
            "Next actual turn activates the queued temporary Strength and draws the normal five cards");
        Check(Godot.GodotObject.IsInstanceValid(indicatorUi) && indicatorUi.IsInsideTree() && indicatorUi.GetInstanceId() == indicatorId &&
            !Descendants(_tree.Root).OfType<NPower>().Any(p => p.Model is GuardTemporaryStrengthPower),
            "The same visible status node survives pending-to-active transition; the internal expiry helper has no icon");
        await CreatureCmd.GainBlock(target, 20, ValueProp.Unpowered, null);
        await CreatureCmd.Damage(Context, target, 2, ValueProp.Unpowered, enemies[0]);
        Check(target.GetPowerAmount<StrengthPower>() == strength + 3 && indicator.PendingAmount == 3 && indicator.ActiveAmount == 3 && upgradedRelic.DisplayAmount == 4,
            "Blocking during temporary Strength queues the next turn without increasing the current turn's Strength");
        Check(indicator.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description.Contains("本回合已获得3") &&
            indicator.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description.Contains("下回合获得3"),
            "One continuous status distinguishes active Strength from next-turn queued Strength");
        await NextTurn(player, enemies[0]);
        Check(target.GetPowerAmount<StrengthPower>() == strength + 3 && target.GetPowerAmount<GuardTemporaryStrengthPower>() == 3,
            "Old temporary Strength expires before the next queued grant, without accumulating permanently");
        await NextTurn(player, enemies[0]);
        Check(target.GetPowerAmount<StrengthPower>() == strength && !target.HasPower<GuardTemporaryStrengthPower>() && !target.HasPower<GuardStrengthNextTurnPower>(),
            "Temporary Strength fully expires and preserves independent permanent Strength");
        await RestartCombat();
        Check(upgradedRelic.DisplayAmount == 1 && target.GetPowerAmount<StrengthPower>() == 0,
            "Ancient grant and native Strength reset for the next combat");
        await CreatureCmd.GainBlock(target, 20, ValueProp.Unpowered, null);
        await CreatureCmd.Damage(Context, target, 2, ValueProp.Unpowered, enemies[0]);
        Check(target.GetPowerAmount<StrengthPower>() == 0 && target.GetPowerAmount<GuardStrengthNextTurnPower>() == 1 && upgradedRelic.DisplayAmount == 2,
            "New-combat ancient relic starts at one queued temporary Strength again");
        await GuardStanceTiming<GuardStrengthNextTurnPower>(upgradedRelic, blockedResult, enemies[0]);
        await Capture("qa-relic-shock-update");
        await NGame.Instance.ReturnToMainMenu();
        SaveManager.Instance.DeleteCurrentRun();
    }

    private static async Task GuardStanceTiming<T>(RelicModel relic, DamageResult blockedResult, Creature enemy)
        where T : PowerModel, new()
    {
        var target = relic.Owner.Creature;
        await PowerCmd.Remove<T>(target);
        var timer = System.Diagnostics.Stopwatch.StartNew();
        // Native first application plus three stacks form the timing control.
        for (int i = 0; i < 4; i++) await PowerCmd.Apply<T>(Context, target, 1, target, null);
        double nativeMs = timer.Elapsed.TotalMilliseconds;
        Check(target.GetPowerAmount<T>() == 4, $"Native {typeof(T).Name} control applies normally outside Guard Stance");
        await PowerCmd.Remove<T>(target);
        int first = relic.DisplayAmount;
        timer.Restart();
        for (int i = 0; i < 4; i++)
            await relic.AfterDamageReceived(Context, target, blockedResult, ValueProp.Unpowered, enemy, null);
        double guardMs = timer.Elapsed.TotalMilliseconds;
        Check(target.GetPowerAmount<T>() == first * 4 + 6 && relic.DisplayAmount == first + 4,
            $"Four {typeof(T).Name} grants preserve per-hit growth, including fresh application and stacking");
        GD.Print($"[Construct GUARD TIMING] {typeof(T).Name}: native 4 grants={nativeMs:F1}ms; relic 4 grants={guardMs:F1}ms");
        Check(nativeMs > 250 && guardMs < nativeMs * 0.5,
            $"Guard Stance removes repeated {typeof(T).Name} presentation pauses without accelerating native gains");
        timer.Restart();
        await PowerCmd.Apply<T>(Context, target, 1, target, null);
        Check(timer.Elapsed.TotalMilliseconds > nativeMs / 8,
            $"Native {typeof(T).Name} presentation delay is restored after the relic trigger");
    }

    private static async Task NewDesignChecks(RunState run, Player player, Creature enemy)
    {
        foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
        enemy.LoseBlockInternal(enemy.Block);
        var native = Player.CreateForNewRun<Ironclad>(new MegaCrit.Sts2.Core.Unlocks.UnlockState([], [], 0), 9999);
        Check(run.CreateCard<Bodyguard>(player).Rarity == CardRarity.Common && run.CreateCard<Bodyguard>(native).Rarity == CardRarity.Basic,
            "Bodyguard is an obtainable common only for the construct");
        Check(run.CreateCard<Suppress>(player).Rarity == CardRarity.Ancient && run.CreateCard<Suppress>(native).Rarity == ModelDb.Card<Suppress>().Rarity,
            "Imported ancient rarity is scoped to construct-owned cards");
        Check(!CardAcquisition.Eligible(player, ModelDb.Card<Parry>()) && !CardAcquisition.Eligible(player, ModelDb.Card<Unleash>()) && !CardAcquisition.Eligible(player, ModelDb.Card<FinalJudgment>()) && !CardAcquisition.Eligible(player, ModelDb.Card<Hailstorm>()),
            "Empty starting deck locks all four restricted groups");
        var transform = CardFactory.GetDefaultTransformationOptions(player.Deck.Cards.First(c => c is IronWave), false).ToArray();
        Check(transform.Length > 0 && transform.All(c => player.Character.CardPool.AllCardIds.Contains(c.Id) && CardAcquisition.Eligible(player, c)),
            "Transforming an imported card uses the construct pool and cannot bypass deck gates");
        var transformed = CardFactory.CreateRandomCardForTransform(player.Deck.Cards.First(c => c is IronWave), false, new Rng(553));
        Check(transformed.Owner == player && CardAcquisition.Eligible(player, transformed), "Actual native random transformation returns an eligible owned card");
        async Task<CardModel> Add<T>() where T : CardModel, new()
        {
            var card = run.CreateCard<T>(player);
            await CardPileCmd.Add(card, PileType.Deck, skipVisuals: true);
            return card;
        }
        var wall = await Add<Bulwark>();
        Check(CardAcquisition.Eligible(player, ModelDb.Card<Parry>()) && CardAcquisition.Eligible(player, ModelDb.Card<ForgeWorkshop>()) && CardAcquisition.Weight(player, wall) == CardAcquisition.GroupMultiplier,
            "Permanent Bulwark unlocks forge cards and enables affinity weight");
        await CardPileCmd.RemoveFromDeck(wall, false);
        var resonance = await Add<DoomResonance>();
        Check(CardAcquisition.Eligible(player, ModelDb.Card<FinalJudgment>()) && !CardAcquisition.Eligible(player, ModelDb.Card<Unleash>()),
            "Doom Resonance unlocks doom cards but does not replace the specific Bodyguard prerequisite");
        var guard = await Add<Bodyguard>();
        Check(CardAcquisition.Eligible(player, ModelDb.Card<Unleash>()) && CardAcquisition.Eligible(player, ModelDb.Card<BoneShards>()), "Bodyguard unlocks the two dependent summon cards");
        await CardPileCmd.RemoveFromDeck(guard, false); await CardPileCmd.RemoveFromDeck(resonance, false);
        var sampleRng = new Rng(314159);
        wall = await Add<Bulwark>();
        int weighted = Enumerable.Range(0, 1200).Count(_ => CardAcquisition.Pick(sampleRng, [ModelDb.Card<Parry>(), ModelDb.Card<Uppercut>()], player) is Parry);
        Check(weighted > 720 && weighted < 900, "Seeded affinity draw favours its group at the configured 2:1 trial weight");
        var affinityOptions = CardCreationOptions.ForNonCombatWithUniformOdds([player.Character.CardPool], c => c is Parry or Uppercut);
        int actualWeighted = Enumerable.Range(0, 1200).Count(_ => CardFactory.CreateForReward(player, 1, affinityOptions).Single().Card is Parry);
        Check(actualWeighted > 720 && actualWeighted < 900, "Actual native reward factory retains the group weight after its distinct filtering");
        await CardPileCmd.RemoveFromDeck(wall, false);
        var tome = (DustyTome)ModelDb.Relic<DustyTome>().ToMutable(); tome.SetupForPlayer(player);
        Check(tome.AncientCard == ModelDb.GetId<BoxingChampionForm>(), "Dusty Tome selects Boxing Champion Form exclusively");
        var shop = MerchantInventory.CreateForNormalMerchant(player);
        AncientShopCards.Install(shop);
        var ancient = shop.CharacterCardEntries.Single(e => CardAcquisition.ShopAncients.Contains(e.CreationResult!.Card.GetType()));
        Check(ancient.CreationResult!.Card is not Protector && ancient.Cost > 158, "Ancient shop card costs more than a gold and Protector stays gated");
        Check(ModelDb.CardPool<ConstructCardPool>().AllCards.Count(CardAcquisition.IsSelectedImport) == 30, "Sea Glass imports stay limited to the chosen native three-color subset, excluding ancient cards");
        foreach (var model in ModelDb.AllAncients)
        {
            var set = model.DialogueSet;
            Check(set.CharacterDialogues.TryGetValue(ModelDb.GetId<ConstructCharacter>().Entry, out var lines) && lines.Count == 3 && lines.All(d => d.Lines.Count == 3 && d.Lines.All(l => l.LineText?.Exists() == true) && d.Lines.Take(2).All(l => l.NextButtonText?.Exists() == true)),
                "Three localized construct exchanges have usable continue buttons: " + model.Id.Entry);
        }

        await Play<ForgeWorkshop>(player);
        player.Creature.LoseBlockInternal(player.Creature.Block);
        await ForgeCmd.Forge(2, player, ModelDb.Card<ForgeWorkshop>());
        Check(player.Creature.Block == 4, "Forge Workshop grants block once per forge, not once per forge point");
        Check(player.Creature.GetPower<ForgeWorkshopPower>()!.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description.Contains("4"),
            "Forge Workshop status tooltip includes its current four block");
        await PowerCmd.Remove<ForgeWorkshopPower>(player.Creature);
        await Play<ForgeWorkshop>(player, upgraded: true);
        await ForgeCmd.Forge(2, player, ModelDb.Card<ForgeWorkshop>());
        Check(player.Creature.Block == 10, "Upgraded Forge Workshop grants six block");
        Check(player.Creature.GetPower<ForgeWorkshopPower>()!.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description.Contains("6"),
            "Upgraded Forge Workshop status tooltip includes six block");
        await PowerCmd.Remove<ForgeWorkshopPower>(player.Creature);
        await Play<RoyalBearing>(player, upgraded: true);
        var royal = player.Creature.GetPower<RoyalBearingPower>()!;
        Check(royal.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description.Contains("6"),
            "Royal Bearing status tooltip includes its six block and end-turn condition");
        var block = player.Creature.Block;
        await royal.BeforeSideTurnEnd(Context, CombatSide.Player, [player.Creature]);
        Check(player.Creature.Block == block + 6, "Royal Bearing detects a Sovereign Blade in hand");
        await PowerCmd.Remove<RoyalBearingPower>(player.Creature);
        await Play<StrikeWhileHot>(player);
        var hot = player.Creature.GetPower<StrikeWhileHotPower>()!;
        var blade = player.PlayerCombatState!.Hand.Cards.OfType<SovereignBlade>().First();
        var damage = blade.DynamicVars.Damage.BaseValue;
        player.Creature.LoseBlockInternal(player.Creature.Block); player.Creature.GainBlockInternal(9);
        await hot.BeforeSideTurnEnd(Context, CombatSide.Player, [player.Creature]);
        Check(blade.DynamicVars.Damage.BaseValue == damage, "Strike While Hot does not trigger below ten block");
        player.Creature.GainBlockInternal(1);
        await hot.BeforeSideTurnEnd(Context, CombatSide.Player, [player.Creature]);
        Check(blade.DynamicVars.Damage.BaseValue == damage + 7, "Strike While Hot forges seven at exactly ten block");
        Check(hot.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description.Contains("7") &&
            hot.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description.Contains("大于等于10"),
            "Actual Strike While Hot icon tooltip shows its seven Forge and inclusive ten-block threshold");
        await Play<StrikeWhileHot>(player, upgraded: true);
        Check(hot.Amount == 17 && hot.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description.Contains("17"),
            "Normal and upgraded Strike While Hot stack into a tooltip showing seventeen Forge");
        await PowerCmd.Remove<StrikeWhileHotPower>(player.Creature);
        await Play<StrikeWhileHot>(player, upgraded: true);
        Check(player.Creature.GetPower<StrikeWhileHotPower>()!.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description.Contains("10"),
            "Standalone upgraded Strike While Hot status tooltip displays ten Forge");
        await PowerCmd.Remove<StrikeWhileHotPower>(player.Creature);

        await Play<FinalJudgment>(player);
        await CreatureCmd.SetCurrentHp(enemy, 100);
        await PowerCmd.Apply<DoomPower>(Context, enemy, 99, player.Creature, null);
        player.Creature.LoseBlockInternal(player.Creature.Block);
        var hp = player.Creature.CurrentHp + (player.Osty?.CurrentHp ?? 0);
        await CreatureCmd.Damage(Context, player.Creature, 3, ValueProp.Unpowered, enemy);
        Check(player.Creature.CurrentHp + (player.Osty?.CurrentHp ?? 0) == hp - 3, "Final Judgment allows damage below the doom threshold");
        await PowerCmd.Apply<DoomPower>(Context, enemy, 1, player.Creature, null);
        hp = player.Creature.CurrentHp + (player.Osty?.CurrentHp ?? 0);
        await CreatureCmd.Damage(Context, player.Creature, 7, ValueProp.Unpowered, enemy);
        Check(player.Creature.CurrentHp + (player.Osty?.CurrentHp ?? 0) == hp, "Final Judgment prevents damage at exactly doom equals HP");
        await PowerCmd.Remove<DoomPower>(enemy); await PowerCmd.Remove<FinalJudgmentPower>(player.Creature);

        player.Creature.GainBlockInternal(8);
        int ostyHp = player.Osty?.CurrentHp ?? 0;
        await Play<UnarmToArms>(player);
        await CreatureCmd.GainBlock(player.Creature, 5, ValueProp.Unpowered, null);
        Check(player.Creature.Block == 8 && player.Osty!.CurrentHp == ostyHp + 5, "Unarm to Arms retains existing block and replaces a new grant with equal summon");
        await PowerCmd.Apply<DexterityPower>(Context, player.Creature, 2, player.Creature, null);
        ostyHp = player.Osty!.CurrentHp;
        await Play<SmallWave>(player, enemy);
        Check(player.Creature.Block == 8 && player.Osty.CurrentHp == ostyHp + 5, "Replacement includes native Dexterity-modified block exactly once");
        await PowerCmd.Remove<DexterityPower>(player.Creature);
        await NextTurn(player, enemy);
        Check(!player.Creature.HasPower<UnarmToArmsPower>(), "Unarm to Arms expires at the end of its turn");
        await Play<DoomResonance>(player, upgraded: true);
        Check(enemy.GetPowerAmount<DoomPower>() == player.Osty!.CurrentHp, "Doom Resonance uses Osty HP after the upgraded summon resolves");
        await PowerCmd.Remove<DoomPower>(enemy);
        await Play<NeverBackDown>(player);
        await Play<NeverBackDown>(player, upgraded: true);
        var existingFists = player.PlayerCombatState.AllCards.OfType<Fisticuffs>().ToHashSet();
        int discardBurns = player.PlayerCombatState.DiscardPile.Cards.Count(c => c is Burn);
        await player.Creature.GetPower<NeverBackDownPower>()!.AfterPlayerTurnStart(Context, player);
        var fistCards = player.PlayerCombatState.AllCards.OfType<Fisticuffs>().Except(existingFists).ToArray();
        Check(fistCards.Length == 2 && fistCards.All(c => !c.IsUpgraded) &&
            player.PlayerCombatState.DiscardPile.Cards.Count(c => c is Burn) == discardBurns + 2,
            "Stacked Never Back Down generates two normal Fisticuffs, plus two Burns in discard");
        await PowerCmd.Remove<NeverBackDownPower>(player.Creature);
        await Play<BoxingChampionForm>(player, upgraded: true);
        Check(player.Creature.GetPower<BoxingChampionFormPower>()!.HoverTips.OfType<MegaCrit.Sts2.Core.HoverTips.HoverTip>().First().Description.Contains("3"),
            "Boxing Champion Form status tooltip includes its three Strength and Dexterity");
        var str = player.Creature.GetPowerAmount<StrengthPower>(); var dex = player.Creature.GetPowerAmount<DexterityPower>();
        await player.Creature.GetPower<BoxingChampionFormPower>()!.AfterPlayerTurnStart(Context, player);
        Check(player.Creature.GetPowerAmount<StrengthPower>() == str + 3 && player.Creature.GetPowerAmount<DexterityPower>() == dex + 3, "Upgraded Boxing Champion Form grants three Strength and Dexterity");
        foreach (var power in player.Creature.Powers.ToArray()) await PowerCmd.Remove(power);
        await StatusTooltipChecks(player, enemy);
        await CreatureCmd.SetMaxAndCurrentHp(enemy, 1000);
        GD.Print("[Construct DESIGN PASS] acquisition, new powers, summon, forge, doom, generated cards");
    }

    private static async Task BaselineBattles()
    {
        foreach (var encounter in new EncounterModel[] { ModelDb.Encounter<NibbitsWeak>(), ModelDb.Encounter<SlimesWeak>(), ModelDb.Encounter<CorpseSlugsWeak>() })
        {
            await NGame.Instance!.ReturnToMainMenu();
            var run = await NGame.Instance.StartNewSingleplayerRun(ModelDb.Character<ConstructCharacter>(), true,
                [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "BASELINE" + encounter.Id.Entry, GameMode.Standard);
            var player = run.Players.Single();
            await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: encounter.ToMutable(), showTransition: false);
            await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Baseline combat starts");
            int turns = 0;
            while (CombatManager.Instance.IsInProgress && player.Creature.IsAlive && turns++ < 40)
            {
                var state = player.Creature.CombatState!;
                foreach (var card in player.PlayerCombatState!.Hand.Cards.OrderBy(c => c is SmallShockwave ? 0 : c is TonyWave ? 1 : c is IronWave ? 2 : 3).ToArray())
                {
                    if (!CombatManager.Instance.IsInProgress) break;
                    var enemy = state.HittableEnemies.OrderBy(e => e.CurrentHp).FirstOrDefault();
                    var target = card.TargetType == TargetType.AnyEnemy ? enemy : null;
                    if (!card.CanPlay() || !card.CanPlayTargeting(target)) continue;
                    if (!card.TryManualPlay(target)) continue;
                    await Until(() => card.Pile?.Type is not (PileType.Hand or PileType.Play) || !CombatManager.Instance.IsInProgress, "Baseline manual card resolution");
                    await Frames(15);
                }
                if (!CombatManager.Instance.IsInProgress) break;
                int turn = player.PlayerCombatState!.TurnNumber;
                PlayerCmd.EndTurn(player, false, () => Task.CompletedTask);
                await Until(() => !CombatManager.Instance.IsInProgress || (player.PlayerCombatState!.TurnNumber > turn && player.PlayerCombatState.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled), "Baseline enemy AI turn");
                await Frames(25);
            }
            GD.Print($"[Construct QA baseline] {encounter.Id.Entry}: turns={turns}, hp={player.Creature.CurrentHp}/{player.Creature.MaxHp}");
            Check(!CombatManager.Instance.IsInProgress && player.Creature.IsAlive, "Unmodified starting deck wins against normal enemy AI: " + encounter.Id.Entry);
        }
    }

    private static async Task RelicChecks()
    {
        var run = await ReliabilityRun("RELICADAPTATIONS");
        var player = run.Players.Single();
        TreasureRelicChecks();
        RelicRollChecks();
        PreviewChecks(player);
        VisualIconChecks();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<PunchConstructNormal>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Relic fixture ready");
        var state = (CombatState)player.Creature.CombatState!;
        var enemy = state.HittableEnemies.Single();
        await CreatureCmd.SetMaxAndCurrentHp(enemy, 1000);
        foreach (var power in enemy.Powers.ToArray()) await PowerCmd.Remove(power);
        decimal hp, block;
        player.Creature.LoseBlockInternal(player.Creature.Block);
        enemy.LoseBlockInternal(enemy.Block);
        hp = enemy.CurrentHp;
        await Play<TonyWave>(player);
        Check(enemy.CurrentHp == hp && player.Creature.Block == 0, "Tony Wave grants neither immediate damage nor block");
        await NextTurn(player, enemy);
        Check(enemy.CurrentHp == hp - 7 && player.Creature.Block == 7 && !player.Creature.HasPower<DelayedWavePower>(), "Tony Wave resolves once on the next real turn: random damage 7 and block 7");
        player.Creature.LoseBlockInternal(player.Creature.Block);
        hp = enemy.CurrentHp;
        await Play<TonyWave>(player);
        await Play<TonyWave>(player, upgraded: true);
        await NextTurn(player, enemy);
        Check(enemy.CurrentHp == hp - 16 && player.Creature.Block == 16, "Two delayed waves preserve their separate 7 and 9 values");
        var dummy = await RelicCmd.Obtain<StrikeDummy>(player);
        hp = enemy.CurrentHp; block = player.Creature.Block;
        await Play<SmallWave>(player, enemy);
        Check(enemy.CurrentHp == hp - 6 && player.Creature.Block == block + 3, "Strike Dummy adds 3 damage to Small Wave without adding block");
        var fakeDummy = await RelicCmd.Obtain<FakeStrikeDummy>(player);
        hp = enemy.CurrentHp;
        await Play<SmallWave>(player, enemy);
        Check(enemy.CurrentHp == hp - 7, "Fake Strike Dummy keeps its smaller +1 bonus");
        Check(dummy.DynamicDescription.GetFormattedText().Contains("斩波"), "Adapted relic tooltip describes waves");
        await RelicCmd.Remove(dummy); await RelicCmd.Remove(fakeDummy);
        await ScorchingWaveChecks(player, enemy);
        var seed = await RelicCmd.Obtain<GhostSeed>(player);
        var etherealWave = state.CreateCard<SmallWave>(player);
        await CardPileCmd.AddGeneratedCardToCombat(etherealWave, PileType.Hand, player);
        var plainIron = state.CreateCard<IronWave>(player);
        await CardPileCmd.AddGeneratedCardToCombat(plainIron, PileType.Hand, player);
        Check(etherealWave.Keywords.Contains(CardKeyword.Ethereal) && !plainIron.Keywords.Contains(CardKeyword.Ethereal), "Ghost Seed affects Small Wave only");
        await RelicCmd.Remove(seed);
        var basin = await RelicCmd.Obtain<RippleBasin>(player);
        block = player.Creature.Block;
        await basin.BeforeSideTurnEnd(Context, CombatSide.Player, [player.Creature]);
        Check(player.Creature.Block == block + 4, "Reversed basin grants 4 block after playing an attack");
        await NextTurn(player, enemy);
        block = player.Creature.Block;
        await basin.BeforeSideTurnEnd(Context, CombatSide.Player, [player.Creature]);
        Check(player.Creature.Block == block, "Reversed basin grants no block on a turn without attacks");
        await RelicCmd.Remove(basin);
        var dart = await RelicCmd.Obtain<ConstructDart>(player);
        await Play<SmallWave>(player, enemy);
        Check(player.Creature.GetPowerAmount<DexterityPower>() == 1, "Exclusive dart grants 1 Dexterity for one attack");
        await Play<SmallWave>(player, enemy);
        Check(player.Creature.GetPowerAmount<DexterityPower>() == 2, "Exclusive dart triggers on every attack without a three-card threshold");
        await NextTurn(player, enemy);
        Check(player.Creature.GetPowerAmount<DexterityPower>() == 0, "Dart Dexterity expires at turn end");
        await RelicCmd.Remove(dart);
        var nativeDart = await RelicCmd.Obtain<HelicalDart>(player);
        await Play<FightMe>(player, enemy);
        Check(player.Creature.GetPowerAmount<DexterityPower>() == 3, "Native Helical Dart uses its construct adaptation, once for a multi-hit card");
        await NextTurn(player, enemy);
        Check(player.Creature.GetPowerAmount<DexterityPower>() == 2, "Native Helical Dart temporary bonus expires without removing permanent Dexterity");
        await RelicCmd.Remove(nativeDart);
        var summonHp = player.Osty?.CurrentHp ?? 0;
        await OstyCmd.Summon(Context, player, 5, ModelDb.Card<SicEm>());
        Check(player.Osty is { IsAlive: true } && player.Osty.CurrentHp == summonHp + 5, "Native Summon creates Osty for this character");
        await Play<ColdSnap>(player, enemy);
        Check(player.PlayerCombatState!.OrbQueue.Capacity == 1 && player.PlayerCombatState.OrbQueue.Orbs.Any(), "Native channeling creates the first non-Defect orb slot");
        await NGame.Instance!.ReturnToMainMenu();
        SaveManager.Instance.DeleteCurrentRun();
    }

    private static async Task EventChecks()
    {
        if (OS.GetCmdlineArgs().Contains("--punchconstruct-event-scene-control")) { await EventSceneControl(); return; }
        if (OS.GetCmdlineArgs().Contains("--punchconstruct-event-room-control")) { await EventRoomControl(); return; }
        if (OS.GetCmdlineArgs().Contains("--punchconstruct-shader-control")) { await NativeShaderControl(); return; }
        if (OS.GetCmdlineArgs().Contains("--punchconstruct-capsule-native-control")) { await CapsuleNativeControl(); return; }
        bool nativeControl = OS.GetCmdlineArgs().Contains("--punchconstruct-event-native-control");
        var run = nativeControl ? await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<MegaCrit.Sts2.Core.Models.Characters.Ironclad>(), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "EVENTROUTES", GameMode.Standard) : await ReliabilityRun("EVENTROUTES");
        var player = run.Players.Single();
        if (nativeControl)
            AccessTools.Field(typeof(Player), "<UnlockState>k__BackingField").SetValue(player,
                AccessTools.Field(typeof(MegaCrit.Sts2.Core.Unlocks.UnlockState), "all").GetValue(null));
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<BrainLeech>(), showTransition: false);
        if (!nativeControl) await CapsuleChecks(player);
        if (!OS.GetCmdlineArgs().Contains("--punchconstruct-event-philosopher-only"))
        {
        var brain = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
        int eventDeckCount = player.Deck.Cards.Count;
        var brainTask = (Task)AccessTools.Method(typeof(BrainLeech), "ShareKnowledge").Invoke(brain, [])!;
        await Until(() => Descendants(_tree.Root).OfType<NSimpleCardSelectScreen>().Any() || brainTask.IsFaulted, "Native Brain Leech card selection");
        if (brainTask.IsFaulted) await brainTask;
        await Frames(40);
        var brainScreen = Descendants(_tree.Root).OfType<NSimpleCardSelectScreen>().Single();
        var choices = (IReadOnlyList<CardModel>)AccessTools.Field(typeof(NCardGridSelectionScreen), "_cards").GetValue(brainScreen)!;
        Check(nativeControl || choices.All(c => CardAcquisition.Eligible(player, c)), "Brain Leech respects deck gates without forced system cards (or native control)");
        Check(choices.Count == 5, "Brain Leech still shows the five choices advertised by its native description");
        await Capture("qa-event-parry");
        var brainPrefs = (MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs)AccessTools.Field(typeof(NSimpleCardSelectScreen), "_prefs").GetValue(brainScreen)!;
        AccessTools.Method(typeof(NSimpleCardSelectScreen), "OnCardClicked").Invoke(brainScreen, [choices.First()]);
        if (brainPrefs.RequireManualConfirmation) AccessTools.Method(typeof(NSimpleCardSelectScreen), "CompleteSelection").Invoke(brainScreen, []);
        await Until(() => brainTask.IsCompleted, "Native event card acquisition");
        await brainTask;
        Check(player.Deck.Cards.Count == eventDeckCount + 1, "Selecting a native event card adds it to the permanent deck");
        }
        if (OS.GetCmdlineArgs().Contains("--punchconstruct-event-brain-only"))
        {
            await NGame.Instance!.ReturnToMainMenu();
            await Frames(20);
            return;
        }
        int cardPoolBaseline = PoolShutdown.CardCount();
        foreach (var pool in new CardPoolModel[] { ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.RegentCardPool>(), ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.NecrobinderCardPool>(), ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.DefectCardPool>() })
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<ColorfulPhilosophers>(), showTransition: false);
            var philosopher = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
            Check(!philosopher.IsFinished, "Philosopher fixture has available initial choices before invoking its reward callback");
            Check(nativeControl || philosopher.CurrentOptions.Count == 3, "Philosopher offers precisely the three specified color choices (or native control)");
            var task = (Task)AccessTools.Method(typeof(ColorfulPhilosophers), "OfferRewards").Invoke(philosopher, [pool])!;
            await Until(() => Descendants(_tree.Root).OfType<NRewardsScreen>().Any(s => s.IsVisibleInTree()) || task.IsFaulted, "Philosopher rewards screen");
            if (task.IsFaulted) await task;
            var screen = Descendants(_tree.Root).OfType<NRewardsScreen>().Single(s => s.IsVisibleInTree());
            Reward RewardOf(NRewardButton button) => button.Reward!;
            var buttons = Descendants(screen).OfType<NRewardButton>().Where(b => RewardOf(b) is CardReward).ToArray();
            Check(buttons.Length == 3, "Philosopher retains all three native card-reward rows");
            var allCandidates = buttons.SelectMany(button => ((CardReward)RewardOf(button)).Cards).ToArray();
            Check(allCandidates.Length == 9 && allCandidates.Select(c => c.Id).Distinct().Count() == 9,
                "All nine Philosopher candidates are distinct across all three rows: " + pool.EnergyColorName);
            var possible = CardCreationOptions.ForNonCombatWithUniformOdds([pool], CardAcquisition.IsPhilosopherCard).GetPossibleCards(player).ToArray();
            int expectedCount = pool is MegaCrit.Sts2.Core.Models.CardPools.RegentCardPool ? 15 :
                pool is MegaCrit.Sts2.Core.Models.CardPools.NecrobinderCardPool ? 11 : 10;
            Check(nativeControl || possible.Length == expectedCount && possible.All(c => c.Pool == pool && CardAcquisition.IsPhilosopherCard(c)) &&
                ModelDb.CardPool<ConstructCardPool>().AllCards.Where(c => c.Pool == pool && c.GetType().Assembly == typeof(ConstructCharacter).Assembly &&
                    !CardAcquisition.IsAncient(c)).All(c => possible.Any(p => p.Id == c.Id)),
                "Philosopher includes the selected native cards and all matching designed system cards: " + pool.EnergyColorName);
            int deckBeforeRewards = player.Deck.Cards.Count;
            foreach (var button in buttons)
            {
                var cards = ((CardReward)RewardOf(button)).Cards.ToArray();
                Check(cards.Length == 3 && (nativeControl || cards.All(c => CardAcquisition.IsPhilosopherCard(c) && c.Pool == pool)), "Philosopher offers three candidates of " + pool.EnergyColorName);
                AccessTools.Method(typeof(NRewardButton), "OnRelease").Invoke(button, []);
                await Until(() => Descendants(_tree.Root).OfType<NCardRewardSelectionScreen>().Any(), "Philosopher reward row opens");
                var row = Descendants(_tree.Root).OfType<NCardRewardSelectionScreen>().Single();
                AccessTools.Method(typeof(NCardRewardSelectionScreen), "SelectCard").Invoke(row, [row.GetCardHolder(cards[0])]);
                await Frames(45);
            }
            await Until(() => task.IsCompleted, "Philosopher acquisition completion");
            await task;
            Check(player.Deck.Cards.Count == deckBeforeRewards + 3,
                "Philosopher still grants exactly one chosen card from each of three distinct rows: " + pool.EnergyColorName);
            Check(PoolShutdown.CardCount() <= cardPoolBaseline,
                "Native card controls are reused across reward screens without pool growth: " + pool.EnergyColorName);
        }
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<PunchOff>(), showTransition: false);
        var punchOff = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
        Check((nativeControl || punchOff.Description?.GetFormattedText().Contains("同型号") == true) && punchOff.CurrentOptions.Count == 2, "Punch Off recognizes the construct as kin and retains both choices (or native control)");
        await ResolvePunchNab(punchOff);
        Check(nativeControl || punchOff.Description?.GetFormattedText().Contains("碰了碰拳") == true, "Nab result uses the confirmed concise character-specific paragraph (or native control)");
        await Capture("qa-punch-off");
        // Let native reward and event presentation finish before removing the
        // room. Compare this same teardown path with the native role control.
        await Frames(180);
        await NGame.Instance!.ReturnToMainMenu();
        await Frames(10);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        await Frames(2);
        SaveManager.Instance.DeleteCurrentRun();
    }

    private static async Task FlowChecks(RunState run, Player player, Creature enemy)
    {
        // Finish the controlled fixture, then exercise real room UI/data and save paths.
        enemy.LoseBlockInternal(enemy.Block);
        await CreatureCmd.SetCurrentHp(enemy, 1);
        await Play<SmallWave>(player, enemy);
        await Until(() => !CombatManager.Instance.IsInProgress, "Combat victory");
        await Frames(120);
        Check(player.Creature.IsAlive, "Combat finishes in victory and rewards can be generated");
        var rewards = CardFactory.CreateForReward(player, 3, CardCreationOptions.ForRoom(player, RoomType.Monster)).ToArray();
        Check(rewards.Length == 3 && rewards.All(c => CardAcquisition.Eligible(player, c.Card) && c.Card.Rarity is not (CardRarity.Basic or CardRarity.Ancient)), "Normal rewards respect deck gates and exclude basic and ancient cards");
        var reward = new CardReward(CardCreationOptions.ForRoom(player, RoomType.Monster), 3, player);
        reward.Populate();
        var rewardTask = reward.SelectUnsynchronized();
        await Until(() => Descendants(_tree.Root).OfType<NCardRewardSelectionScreen>().Any(), "Reward selection screen");
        await Frames(60);
        int rewardDeck = player.Deck.Cards.Count;
        var rewardScreen = Descendants(_tree.Root).OfType<NCardRewardSelectionScreen>().Single();
        var rewardCard = reward.Cards.First();
        AccessTools.Method(typeof(NCardRewardSelectionScreen), "SelectCard").Invoke(rewardScreen, [rewardScreen.GetCardHolder(rewardCard)]);
        await Until(() => rewardTask.IsCompleted, "Selecting a combat card reward");
        await rewardTask;
        Check(player.Deck.Cards.Count == rewardDeck + 1, "Selecting a card in the real reward screen adds the reward");
        await RunManager.Instance.EnterRoomDebug(RoomType.Shop, showTransition: false);
        var shop = ((MerchantRoom)run.CurrentRoom!).GetLocalInventory();
        Check(shop.CharacterCardEntries.Count > 0 && shop.RelicEntries.All(e => e.Model is {} model && RelicWeights.Allowed(model)), "A normal shop creates cards and contains no excluded relics");
        var entry = shop.CharacterCardEntries.First();
        int deckBefore = player.Deck.Cards.Count;
        await PlayerCmd.SetGold(999, player);
        await entry.OnTryPurchaseWrapper(shop);
        Check(player.Deck.Cards.Count == deckBefore + 1, "Buying a card through the shop entry adds it to the deck");
        await Capture("qa-shop");
        await RunManager.Instance.EnterRoomDebug(RoomType.RestSite, showTransition: false);
        await CreatureCmd.SetCurrentHp(player.Creature, 100);
        int hpBefore = player.Creature.CurrentHp;
        await new HealRestSiteOption(player).OnSelect();
        Check(player.Creature.CurrentHp > hpBefore, "Rest-site healing works for the character");
        var toUpgrade = player.Deck.Cards.First(c => c is SmallWave && !c.IsUpgraded);
        var smithTask = new SmithRestSiteOption(player).OnSelect();
        await Until(() => Descendants(_tree.Root).OfType<NDeckUpgradeSelectScreen>().Any(), "Smith card-selection screen");
        await Frames(40);
        var smithScreen = Descendants(_tree.Root).OfType<NDeckUpgradeSelectScreen>().Single();
        AccessTools.Method(typeof(NDeckUpgradeSelectScreen), "OnCardClicked").Invoke(smithScreen, [toUpgrade]);
        AccessTools.Method(typeof(NDeckUpgradeSelectScreen), "ConfirmSelection").Invoke(smithScreen, [null]);
        await Until(() => smithTask.IsCompleted, "Smith upgrade completion");
        await smithTask;
        Check(toUpgrade.IsUpgraded && toUpgrade.DynamicVars.Damage.BaseValue == 5, "Smithing through the actual card-selection screen upgrades Small Wave");
        await Capture("qa-rest");
        var tooth = (ArchaicTooth)ModelDb.Relic<ArchaicTooth>().ToMutable();
        Check(tooth.SetupForPlayer(player) && tooth.AncientCard!.Id == ModelDb.GetId<ScorchingIronWave>(), "Archaic Tooth selects Scorching Iron Wave for the construct");
        await RelicCmd.Obtain(tooth, player);
        Check(player.Deck.Cards.Any(c => c is ScorchingIronWave && c.CurrentUpgradeLevel == 3) && player.Deck.Cards.All(c => c is not Doubt), "Actually acquiring Archaic Tooth transforms Iron Wave into Scorching Iron Wave upgraded three times");
        int waves = player.Deck.Cards.Count(c => c is SmallWave);
        await RelicCmd.Obtain<NutritiousSoup>(player);
        Check(player.Deck.Cards.All(c => c is not SmallWave) && player.Deck.Cards.Count(c => c is IronWave && c.Keywords.Contains(CardKeyword.Eternal)) == waves, "Nutritious Soup transforms every Small Wave and makes the replacements Eternal");
        Check(ModelDb.Relic<GuardStance>().GetUpgradeReplacement() is AncientGuardStance, "The starter relic has its ancient replacement");
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<FakeMerchant>(), showTransition: false);
        var fakeMerchant = (FakeMerchant)((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
        for (int i = 0; i < 40; ++i)
        {
            await (Task)AccessTools.Method(typeof(FakeMerchant), "BeforeEventStarted").Invoke(fakeMerchant, [false])!;
            Check(fakeMerchant.Inventory.RelicEntries.All(e => e.Model is not FakeOrichalcum), "Fake merchant excludes fake Orichalcum, sample " + i);
        }
        var originalFakePool = (RelicModel[])AccessTools.Field(typeof(FakeMerchant), "_inventoryRelics").GetValue(null)!;
        Check(originalFakePool.Any(r => r is FakeOrichalcum), "Original fake-merchant pool stays intact for other characters");
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<RoomFullOfCheese>(), showTransition: false);
        var eventCards = CardFactory.CreateForReward(player, 3, CardCreationOptions.ForNonCombatWithUniformOdds([player.Character.CardPool], c => c.Rarity != CardRarity.Basic)).ToArray();
        Check(eventCards.All(c => CardAcquisition.Eligible(player, c.Card)), "Cheese-house event respects deck gates without forced Parry replacement");
        Check(eventCards.Length == 3, "Event adaptation preserves the requested number of choices");
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<BrainLeech>(), showTransition: false);
        var brain = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
        int eventDeckCount = player.Deck.Cards.Count;
        var brainTask = (Task)AccessTools.Method(typeof(BrainLeech), "ShareKnowledge").Invoke(brain, [])!;
        await Until(() => Descendants(_tree.Root).OfType<NSimpleCardSelectScreen>().Any() || brainTask.IsFaulted, "Native Brain Leech card selection");
        if (brainTask.IsFaulted) await brainTask;
        await Frames(40);
        var brainScreen = Descendants(_tree.Root).OfType<NSimpleCardSelectScreen>().Single();
        var choices = (IReadOnlyList<CardModel>)AccessTools.Field(typeof(NCardGridSelectionScreen), "_cards").GetValue(brainScreen)!;
        Check(choices.All(c => CardAcquisition.Eligible(player, c)), "Brain Leech respects deck gates without forced system cards");
        Check(choices.Count == 5, "Brain Leech still shows the five choices advertised by its native description");
        await Capture("qa-event-parry");
        var brainPrefs = (MegaCrit.Sts2.Core.CardSelection.CardSelectorPrefs)AccessTools.Field(typeof(NSimpleCardSelectScreen), "_prefs").GetValue(brainScreen)!;
        AccessTools.Method(typeof(NSimpleCardSelectScreen), "OnCardClicked").Invoke(brainScreen, [choices.First()]);
        if (brainPrefs.RequireManualConfirmation) AccessTools.Method(typeof(NSimpleCardSelectScreen), "CompleteSelection").Invoke(brainScreen, []);
        await Until(() => brainTask.IsCompleted, "Native event card acquisition");
        await brainTask;
        Check(player.Deck.Cards.Count == eventDeckCount + 1, "Selecting a native event card adds it to the permanent deck");
        foreach (var pool in new CardPoolModel[] { ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.RegentCardPool>(), ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.NecrobinderCardPool>(), ModelDb.CardPool<MegaCrit.Sts2.Core.Models.CardPools.DefectCardPool>() })
        {
            await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<ColorfulPhilosophers>(), showTransition: false);
            var philosopher = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
            Check(philosopher.CurrentOptions.Count == 3, "Philosopher offers precisely the three specified color choices");
            var task = (Task)AccessTools.Method(typeof(ColorfulPhilosophers), "OfferRewards").Invoke(philosopher, [pool])!;
            await Until(() => Descendants(_tree.Root).OfType<NRewardsScreen>().Any(s => s.IsVisibleInTree()) || task.IsFaulted, "Philosopher rewards screen");
            if (task.IsFaulted) await task;
            var screen = Descendants(_tree.Root).OfType<NRewardsScreen>().Single(s => s.IsVisibleInTree());
            Reward RewardOf(NRewardButton button) => button.Reward!;
            var buttons = Descendants(screen).OfType<NRewardButton>().Where(b => RewardOf(b) is CardReward).ToArray();
            Check(buttons.Length == 3, "Philosopher retains all three native card-reward rows");
            var allCandidates = buttons.SelectMany(button => ((CardReward)RewardOf(button)).Cards).ToArray();
            Check(allCandidates.Length == 9 && allCandidates.Select(c => c.Id).Distinct().Count() == 9,
                "All nine Philosopher candidates are distinct across all three rows: " + pool.EnergyColorName);
            var possible = CardCreationOptions.ForNonCombatWithUniformOdds([pool], CardAcquisition.IsPhilosopherCard).GetPossibleCards(player).ToArray();
            int expectedCount = pool is MegaCrit.Sts2.Core.Models.CardPools.RegentCardPool ? 15 :
                pool is MegaCrit.Sts2.Core.Models.CardPools.NecrobinderCardPool ? 11 : 10;
            Check(possible.Length == expectedCount && possible.All(c => c.Pool == pool && CardAcquisition.IsPhilosopherCard(c)) &&
                ModelDb.CardPool<ConstructCardPool>().AllCards.Where(c => c.Pool == pool && c.GetType().Assembly == typeof(ConstructCharacter).Assembly &&
                    !CardAcquisition.IsAncient(c)).All(c => possible.Any(p => p.Id == c.Id)),
                "Philosopher includes the selected native cards and all matching designed system cards: " + pool.EnergyColorName);
            int deckBeforeRewards = player.Deck.Cards.Count;
            foreach (var button in buttons)
            {
                var cards = ((CardReward)RewardOf(button)).Cards.ToArray();
                Check(cards.Length == 3 && cards.All(c => CardAcquisition.IsPhilosopherCard(c) && c.Pool == pool), "Philosopher offers three unrestricted-rarity candidates including designed system cards of " + pool.EnergyColorName);
                AccessTools.Method(typeof(NRewardButton), "OnRelease").Invoke(button, []);
                await Until(() => Descendants(_tree.Root).OfType<NCardRewardSelectionScreen>().Any(), "Philosopher reward row opens");
                var row = Descendants(_tree.Root).OfType<NCardRewardSelectionScreen>().Single();
                AccessTools.Method(typeof(NCardRewardSelectionScreen), "SelectCard").Invoke(row, [row.GetCardHolder(cards[0])]);
                await Frames(45);
            }
            await Until(() => task.IsCompleted, "Philosopher acquisition completion");
            await task;
            Check(player.Deck.Cards.Count == deckBeforeRewards + 3,
                "Philosopher still grants exactly one chosen card from each of three distinct rows: " + pool.EnergyColorName);
        }
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<PunchOff>(), showTransition: false);
        var punchOff = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
        Check(punchOff.Description?.GetFormattedText().Contains("同型号") == true && punchOff.CurrentOptions.Count == 2, "Punch Off recognizes the construct as kin and retains both choices");
        await ResolvePunchNab(punchOff);
        Check(punchOff.Description?.GetFormattedText().Contains("碰了碰拳") == true, "Nab result uses the confirmed concise character-specific paragraph");
        await Capture("qa-punch-off");
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<DollRoom>(), showTransition: false);
        var doll = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
        int relicCount = player.Relics.Count;
        await (Task)AccessTools.Method(typeof(DollRoom), "ChooseRandom").Invoke(doll, [])!;
        Check(player.Relics.Count == relicCount + 1, "The weighted random-doll event executes and grants a native relic");
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<DollRoom>(), showTransition: false);
        doll = ((EventRoom)run.CurrentRoom!).LocalMutableEvent!;
        await (Task)AccessTools.Method(typeof(DollRoom), "TakeSomeTime").Invoke(doll, [])!;
        Check(doll.CurrentOptions.Count > 0, "The weighted doll shortlist path executes normally");
        await SaveManager.Instance.SaveRun(run.CurrentRoom);
        var disk = SaveManager.Instance.LoadRunSave();
        Check(disk.Success, "The native save loader reads the run from disk successfully");
        var saved = disk.SaveData!;
        var restored = RunState.FromSerializable(saved);
        var restoredPlayer = restored.Players.Single();
        Check(restoredPlayer.Character is ConstructCharacter && restoredPlayer.Deck.Cards.Count == player.Deck.Cards.Count, "Run serialization restores the character and full deck");
        Check(restoredPlayer.Relics.Any(r => r is GuardStance) && restoredPlayer.Deck.Cards.Count(c => c.Keywords.Contains(CardKeyword.Eternal)) == waves, "Relics and transformed Eternal cards survive serialization");
        var gammaSave = restored.CreateCard<GammaBlast>(restoredPlayer);
        Check(gammaSave.CurrentStarCost == 0 && gammaSave.EnergyCost.GetWithModifiers(CostModifiers.None) == 2, "Restored character keeps the Regent card adaptation");
        int savedDeckCount = player.Deck.Cards.Count;
        await NGame.Instance!.ReturnToMainMenu();
        await Frames(90);
        var continueMenu = NGame.Instance.MainMenu!;
        await (Task)AccessTools.Method(typeof(NMainMenu), "OnContinueButtonPressedAsync").Invoke(continueMenu, [])!;
        await Frames(120);
        var resumed = RunManager.Instance.DebugOnlyGetState()!;
        Check(resumed.Players.Single().Character is ConstructCharacter && resumed.Players.Single().Deck.Cards.Count == savedDeckCount, "Continue from the real main menu restores the saved character and deck");
        Check(resumed.Players.Single().Deck.Cards.Count(c => c.Keywords.Contains(CardKeyword.Eternal)) == waves, "Eternal persists after an actual main-menu continue");
    }
}

// Deterministic combat-unit cases use an inert enemy; later flow cases run normal enemy AI.
[HarmonyPatch(typeof(MonsterModel), nameof(MonsterModel.PerformMove))]
internal static class PlaytestInertEnemy
{
    private static bool Prefix(MonsterModel __instance, ref Task __result)
    {
        if (!PlaytestRunner.HoldEnemyActions) return true;
        __result = PlaytestRunner.EnemyMoveForQa?.Invoke(__instance) ?? Task.CompletedTask; return false;
    }
}
