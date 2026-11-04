using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task EventSceneControl()
    {
        bool noParticles = OS.GetCmdlineArgs().Contains("--punchconstruct-event-scene-no-particles");
        var scene = ResourceLoader.Load<PackedScene>("res://scenes/vfx/events/colorful_philosophers_vfx.tscn");
        for (int i = 0; i < 3; i++)
        {
            var instance = scene.Instantiate<Node2D>();
            instance.Position = new Vector2(640, 360);
            int particles = Descendants(instance).OfType<GpuParticles2D>().Count();
            if (noParticles)
                foreach (var particle in Descendants(instance).OfType<GpuParticles2D>().ToArray())
                { particle.GetParent().RemoveChild(particle); particle.Free(); }
            _tree.Root.AddChild(instance);
            await Frames(45);
            Check(instance.IsInsideTree() && particles == 9, "Native Philosopher scene rendered independently of run/reward/card rules: " + i);
            instance.QueueFree();
            await Frames(10);
            Check(!GodotObject.IsInstanceValid(instance), "Native event VFX instance is freed: " + i);
        }
        await Frames(10);
        GC.Collect(); GC.WaitForPendingFinalizers();
        await Frames(2);
        GD.Print($"[Construct EVENT SCENE PASS] {_checks} checks; noParticles={noParticles}");
    }

    private static async Task EventRoomControl()
    {
        var run = await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "PHILOSOPHER-ROOM-CONTROL", GameMode.Standard);
        var player = run.Players.Single();
        AccessTools.Field(typeof(Player), "<UnlockState>k__BackingField").SetValue(player,
            AccessTools.Field(typeof(MegaCrit.Sts2.Core.Unlocks.UnlockState), "all").GetValue(null));
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<ColorfulPhilosophers>(), showTransition: false);
        await Frames(120);
        Check(((EventRoom)run.CurrentRoom!).LocalMutableEvent is ColorfulPhilosophers,
            "Native Philosopher room and preloading render without any reward selection");
        await NGame.Instance.ReturnToMainMenu();
        await Frames(20);
        GC.Collect(); GC.WaitForPendingFinalizers();
        await Frames(2);
        GD.Print($"[Construct EVENT ROOM PASS] {_checks} checks; native room only");
    }

    private static async Task NativeShaderControl()
    {
        bool native = OS.GetCmdlineArgs().Contains("--punchconstruct-shader-native-role");
        var run = native ? await NGame.Instance!.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "DECIMILLIPEDE-SHADER", GameMode.Standard)
            : await ReliabilityRun("DECIMILLIPEDE-SHADER");
        var player = run.Players.Single();
        Check(!GraphicsCompatibility.Applies(false, true) && !GraphicsCompatibility.Applies(true, false) &&
            GraphicsCompatibility.Applies(true, true), "Shader adaptation is restricted to compatibility rendering with a construct");
        foreach (MonsterModel segment in new MonsterModel[] { ModelDb.Monster<DecimillipedeSegmentFront>(),
            ModelDb.Monster<DecimillipedeSegmentMiddle>(), ModelDb.Monster<DecimillipedeSegmentBack>() })
        {
            string path = (string)AccessTools.PropertyGetter(typeof(MonsterModel), "VisualsPath").Invoke(segment, [])!;
            Check(path.StartsWith(native ? "res://scenes/creature_visuals/" : "res://PunchConstruct/native_compat/"),
                "Preload and instantiation use isolated paths for all three native segments: " + segment.Id);
        }
        await RunManager.Instance.EnterRoomDebug(RoomType.Elite, model: ModelDb.Encounter<DecimillipedeElite>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play,
            "Direct Decimillipede room starts");
        await Frames(120);
        var materials = Descendants(NCombatRoom.Instance!).OfType<CanvasItem>()
            .Select(n => n.Material).OfType<ShaderMaterial>().Where(m => m.Shader?.Code.Contains("suckPattern") == true).Distinct().ToArray();
        Check(materials.Length >= 3 && materials.All(m => m.Shader.ResourcePath.StartsWith(native ?
            "res://shaders/vfx/monsters/" : "res://PunchConstruct/native_compat/") && !m.Shader.Code.Contains("fma(")),
            "Actual rendered segment materials use compatible code and retain role-specific resource ownership");
        Check(materials.All(m => new[] { "suckPattern", "suckGradient", "suckShape" }
            .All(p => m.GetShaderParameter(p).AsGodotObject() is Texture2D)),
            "All native suck textures remain bound on the actual scene materials");
        foreach (var vfx in Descendants(NCombatRoom.Instance!).OfType<NDecimillipedeSegmentVfx>()) vfx.Regenerate();
        await Frames(90);
        Check(player.Creature.CombatState!.Enemies.Count() >= 3, "Native Decimillipede segments render in a directly prepared room");
        await Capture("qa-decimillipede-native");
        await NGame.Instance!.ReturnToMainMenu();
        await Frames(20);
        var outside = (string)AccessTools.PropertyGetter(typeof(MonsterModel), "VisualsPath")
            .Invoke(ModelDb.Monster<DecimillipedeSegmentFront>(), [])!;
        Check(outside.StartsWith("res://scenes/creature_visuals/"), "Leaving the construct run restores native scene selection without changing shared resources");
        GD.Print($"[Construct SHADER PASS] {_checks} checks; direct native encounter");
    }
}
