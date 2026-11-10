using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    // This observer is installed only by the explicit animation QA mode.
    private sealed class AnimationProbe : IDisposable
    {
        private readonly Harmony _harmony = new("PunchConstruct.QA.Animation");
        internal static NCreature? Actor;
        internal static readonly List<(string Trigger, string Animation)> Motions = [];
        internal static readonly List<string> Sounds = [];
        internal AnimationProbe()
        {
            _harmony.Patch(AccessTools.Method(typeof(NCreature), nameof(NCreature.SetAnimationTrigger)),
                postfix: new HarmonyMethod(typeof(AnimationProbe), nameof(Motion)));
            _harmony.Patch(AccessTools.Method(typeof(NAudioManager), nameof(NAudioManager.PlayOneShot), [typeof(string), typeof(float)]),
                postfix: new HarmonyMethod(typeof(AnimationProbe), nameof(Sound)));
        }
        private static void Motion(NCreature __instance, string __0)
        {
            if (__instance == Actor && __0 is "Attack" or "DoubleAttack" or "Cast" or "PowerUp")
                Motions.Add((__0, __instance.Visuals.SpineBody!.GetAnimationState().GetCurrentAnimationName() ?? ""));
        }
        private static void Sound(string path)
        {
            if (path is ConstructPresentation.SingleSfx or ConstructPresentation.DoubleSfx or ConstructPresentation.GuardSfx)
                Sounds.Add(path);
        }
        internal static void Clear() { Motions.Clear(); Sounds.Clear(); }
        public void Dispose() { _harmony.UnpatchAll(_harmony.Id); Actor = null; Clear(); }
    }

    private static async Task AnimationChecks()
    {
        var run = await ReliabilityRun("CONSTRUCTANIMATIONS");
        var player = run.Players.Single();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Animation fixture ready");
        var state = (CombatState)player.Creature.CombatState!;
        foreach (var enemy in state.HittableEnemies.ToArray()) await CreatureCmd.SetMaxAndCurrentHp(enemy, 10000);
        var target = state.HittableEnemies.First();
        var actor = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
        using var probe = new AnimationProbe();
        AnimationProbe.Actor = actor;

        async Task Verify<T>(string trigger) where T : CardModel, new()
        {
            AnimationProbe.Clear();
            decimal hp = target.CurrentHp;
            await Play<T>(player, ModelDb.Card<T>().TargetType == TargetType.AnyEnemy ? target : null);
            string animation = trigger switch { "DoubleAttack" => "attack_double", "Cast" or "PowerUp" => "block", _ => "attack" };
            string sound = trigger switch { "DoubleAttack" => ConstructPresentation.DoubleSfx, "Cast" or "PowerUp" => ConstructPresentation.GuardSfx, _ => ConstructPresentation.SingleSfx };
            Check(AnimationProbe.Motions.SequenceEqual(new[] { (trigger, animation) }),
                typeof(T).Name + " triggers exactly one real native " + animation + " animation; saw " +
                string.Join(", ", AnimationProbe.Motions.Select(m => m.Trigger + "->" + m.Animation)));
            Check(AnimationProbe.Sounds.SequenceEqual(new[] { sound }), typeof(T).Name + " plays exactly one matching original monster sound");
            if (typeof(T) == typeof(SmallWave)) Check(target.CurrentHp == hp - 3, "Double-punch presentation leaves a single-hit Small Wave at 3 damage");
            if (typeof(T) == typeof(FightMe)) Check(target.CurrentHp == hp - 12, "Multi-hit Fight Me retains two 6-damage hits");
        }

        await Verify<SmallWave>("Attack");
        await Verify<SmallWave>("Attack");
        await Verify<SmallWave>("DoubleAttack");
        await Verify<SmallWave>("Attack");
        await Verify<FightMe>("DoubleAttack");
        await Verify<SmallWave>("Attack");
        await Verify<SmallShockwave>("Cast");
        // Keep later raw-damage assertions independent of this skill's real debuff.
        foreach (var enemy in state.HittableEnemies.ToArray()) await PowerCmd.Remove<VulnerablePower>(enemy);
        await Verify<IronWaveForm>("Cast");
        await Verify<DemonForm>("Cast"); // Wrapper cast covers native powers as well.
        await Verify<DefendIronclad>("Cast"); // Native skills already call Cast: no duplicate pose/sound.
        await Verify<SmallWave>("Attack"); // Skills/powers do not consume the attack sequence.
        await Verify<SmallWave>("DoubleAttack");
        await Verify<TonyWave>("Cast");

        // Multiple damage commands in one play count as one card, not two moves.
        AnimationProbe.Clear();
        var card = state.CreateCard<SmallWave>(player);
        var play = new CardPlay { Card = card, Player = player, Target = target, ResultPile = PileType.Discard,
            Resources = default, IsAutoPlay = false, PlayIndex = 0, PlayCount = 1 };
        for (int i = 0; i < 2; i++)
            await DamageCmd.Attack(1).FromCard(card, play).Targeting(target).Execute(Context);
        Check(AnimationProbe.Motions.Count == 1 && AnimationProbe.Sounds.Count == 1, "Two damage commands in one card play do not restart animation or advance twice");
        await Verify<SmallWave>("Attack"); // Leaves two single punches before changing combat.

        if (OS.GetCmdlineArgs().Contains("--punchconstruct-visual")) await CaptureNativePunchAnimations();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
        await Until(() => player.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Second direct combat ready");
        AnimationProbe.Actor = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
        AnimationProbe.Clear();
        await Play<SmallWave>(player, player.Creature.CombatState!.HittableEnemies.First());
        Check(AnimationProbe.Motions.Single().Trigger == "Attack", "A new combat resets the presentation sequence");

        await NGame.Instance!.ReturnToMainMenu();
        SaveManager.Instance.DeleteCurrentRun();
        var native = await NGame.Instance.StartNewSingleplayerRun(ModelDb.Character<Ironclad>(), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "ANIMATIONISOLATION", GameMode.Standard);
        var other = native.Players.Single();
        await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<SlimesWeak>().ToMutable(), showTransition: false);
        await Until(() => other.PlayerCombatState?.Phase == PlayerTurnPhase.Play && !CombatManager.Instance.PlayerActionsDisabled, "Native isolation fixture ready");
        AnimationProbe.Actor = NCombatRoom.Instance!.GetCreatureNode(other.Creature)!;
        AnimationProbe.Clear();
        await Play<IronWave>(other, other.Creature.CombatState!.HittableEnemies.First());
        Check(AnimationProbe.Motions.Count == 1 && AnimationProbe.Motions[0].Trigger == "Attack" && AnimationProbe.Sounds.Count == 0,
            "Ironclad retains native attack animation/sound and virtual card effects");
        await NGame.Instance.ReturnToMainMenu();
        SaveManager.Instance.DeleteCurrentRun();
    }

    private static async Task CaptureNativePunchAnimations()
    {
        var viewport = new SubViewport { Size = new Vector2I(1000, 760), RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        _tree.Root.AddChild(viewport);
        var background = new ColorRect { Color = new Color(.14f, .16f, .20f), Size = new Vector2(1000, 760) };
        viewport.AddChild(background);
        var native = ResourceLoader.Load<PackedScene>("res://scenes/creature_visuals/punch_construct.tscn").Instantiate<NCreatureVisuals>();
        native.Position = new Vector2(620, 690);
        native.Scale = Vector2.One * 1.6f;
        viewport.AddChild(native);
        var animator = ModelDb.Monster<MegaCrit.Sts2.Core.Models.Monsters.PunchConstruct>().GenerateAnimator(native.SpineBody!);
        var sprite = native.SpineBody!.BoundObject;
        var animationState = sprite.Call("get_animation_state").AsGodotObject();
        sprite.Call("set_update_mode", 2); // Manual stepping; a frozen state leaves the body in its setup pose.
        foreach (var trigger in new[] { "Attack", "DoubleAttack", "Cast" })
        {
            native.SpineBody.GetSkeleton()!.BoundObject.Call("set_to_setup_pose");
            animator.SetTrigger(trigger);
            var entry = animationState.Call("get_current", 0).AsGodotObject();
            double end = entry.Call("get_animation_end").AsDouble();
            GD.Print($"[Construct NATIVE ANIMATION] {trigger} duration={end}");
            var poses = new HashSet<string>();
            for (int i = 0; i < 24; i++)
            {
                sprite.Call("update_skeleton", end / 24);
                await _tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
                var image = viewport.GetTexture().GetImage();
                poses.Add(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(image.GetData())));
                image.SavePng(OS.GetExecutablePath().GetBaseDir() + $"/qa-animation-{trigger}-{i:D2}.png");
            }
            Check(poses.Count >= 3, "Original " + trigger + " capture contains actual changing poses, not only animation names");
        }
        viewport.QueueFree();
        await Frames(3);
    }
}
