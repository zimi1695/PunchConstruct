using System.Runtime.CompilerServices;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Saves;

namespace PunchConstruct;

// Presentation only: no combat RNG, serialized fields, delays or network messages.
internal static class ObserverDivinityVfx
{
    private const string Path = "res://PunchConstruct/observer_vfx/";
    private static readonly ConditionalWeakTable<Creature, Node2D> Active = new();
    private static readonly Random CosmeticRandom = new();
    internal static Node2D? Find(Creature owner) => Active.TryGetValue(owner, out var node) && GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion() ? node : null;
    internal static Texture2D Texture(string name) => ResourceLoader.Load<Texture2D>(Path + name + ".ctex");
    internal static Texture2D EntryRayTexture() => new AtlasTexture
    {
        Atlas = Texture("divinity_atlas"), Region = new Rect2(1402, 19, 122, 17), FilterClip = true
    };
    internal static void Mantra(Creature owner)
    {
        var room = NCombatRoom.Instance;
        if (room != null) PlaySound(room, "mantra");
    }

    internal static void Enter(Creature owner)
    {
        Remove(owner);
        var actor = NCombatRoom.Instance?.GetCreatureNode(owner);
        if (actor == null) return;
        var root = new Node2D { Name = "ObserverDivinityVfx" };
        actor.Visuals.AddChild(root);
        Active.Add(owner, root);
        root.AddChild(Particles("DivinityFog", "exhaust_l", 10, 2, new(30, 35), 2, 2.7f, new(.65f, .05f, .65f, .15f), 2, 8, 30, 40));
        root.AddChild(Particles("DivinityGlow", "glow_spark", 14, 1.5, new(25, 25), .15f, .4f, new(.9f, .6f, .9f, .4f), 15, 45, 180, 0));
        // Eye frames and cadence follow the original Watcher stance effect.
        var timer = new Godot.Timer { Name = "DivinityEyes", WaitTime = .3, Autostart = true };
        timer.Timeout += () =>
        {
            if (!owner.IsAlive || !owner.HasPower<ObserverDivinity>()) { Remove(owner); return; }
            if (root.GetChildren().OfType<Sprite2D>().Count(s => s.HasMeta("observer_divinity_eye") && !s.IsQueuedForDeletion()) < 4) SpawnEye(root);
            timer.WaitTime = .35 + CosmeticRandom.NextDouble() * .25;
        };
        root.AddChild(timer);
        Burst(root);
        if (owner.CombatState?.Players.Count() == 1) Border(root);
        PlaySound(root, "divinity");
        Diagnostics.Write("info", "observer.divinity.enter", new { owner = owner.Player?.NetId.ToString(), particles = 24, eyes = true });
    }

    internal static void Remove(Creature owner)
    {
        if (Active.TryGetValue(owner, out var node))
        {
            if (GodotObject.IsInstanceValid(node) && !node.IsQueuedForDeletion()) { node.Visible = false; node.QueueFree(); }
            Active.Remove(owner);
            Diagnostics.Write("info", "observer.divinity.exit");
        }
    }

    private static CpuParticles2D Particles(string name, string texture, int amount, double life, Vector2 extents,
        float minScale, float maxScale, Color color, float minVelocity, float maxVelocity, float spread, float angular)
        => new()
        {
            Name = name, Texture = Texture(texture), Amount = amount, Lifetime = life, Preprocess = life,
            Randomness = 1, LifetimeRandomness = texture == "glow_spark" ? .4 : .3, LocalCoords = true,
            EmissionShape = CpuParticles2D.EmissionShapeEnum.Rectangle, EmissionRectExtents = extents,
            Direction = Vector2.Up, Spread = spread, Gravity = Vector2.Zero,
            InitialVelocityMin = minVelocity, InitialVelocityMax = maxVelocity,
            ScaleAmountMin = minScale, ScaleAmountMax = maxScale,
            AngularVelocityMin = -angular, AngularVelocityMax = angular,
            Position = new(0, -130), Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add },
            ColorRamp = texture == "glow_spark"
                ? new Gradient { Colors = [color, new(.8f, .5f, .8f, 0)] }
                : new Gradient { Offsets = [0, .3f, 1], Colors = [new(color.R, color.G, color.B, 0), color, new(color.R, color.G, color.B, 0)] },
            Emitting = true
        };

    private static void SpawnEye(Node2D root)
    {
        float scale = 1 + CosmeticRandom.NextSingle() * .5f;
        var offset = new Vector2(CosmeticRandom.Next(-110, 111), CosmeticRandom.Next(-80, 81));
        // Original eyes and entry rays live in combat/world space, independent of
        // the actor's facing and visual scale. Only the fog follows the actor.
        var center = root.GlobalPosition + new Vector2(0, -95) + offset;
        var color = new Color(.9f + CosmeticRandom.NextSingle() * .1f, .55f + CosmeticRandom.NextSingle() * .2f, .9f + CosmeticRandom.NextSingle() * .1f, 0);
        var first = Texture("eye0");
        var eye = new Sprite2D
        {
            Name = "DivinityEye", TopLevel = true, Texture = first, Scale = Vector2.One * scale, ZIndex = 50,
            RotationDegrees = (6 + CosmeticRandom.NextSingle() * 6) * (offset.X > 0 ? -1 : 1),
            Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add }
        };
        eye.SetMeta("observer_divinity_eye", true);
        root.AddChild(eye);
        eye.Modulate = color;
        var tween = eye.CreateTween();
        tween.TweenMethod(Callable.From<float>(remaining =>
        {
            (int frame, float nudge) = remaining switch
            {
                > .85f => (0, 12), > .8f => (1, 8), > .75f => (2, 4), > .7f => (3, 3),
                > .65f => (4, 0), > .6f => (5, 0), > .55f => (6, 0), > .38f => (5, 0),
                > .3f => (4, 0), > .25f => (3, 3), > .2f => (2, 4), > .15f => (1, 8), _ => (0, 12)
            };
            var texture = Texture("eye" + frame);
            eye.Texture = texture;
            eye.GlobalPosition = center + new Vector2((texture.GetWidth() - first.GetWidth()) / 2f, (first.GetHeight() - texture.GetHeight()) / 2f - nudge);
            float t = Mathf.Clamp(remaining <= .5f ? remaining * 2 : (1 - remaining) * 2, 0, 1);
            color.A = t * t * t * (t * (t * 6 - 15) + 10);
            eye.Modulate = color;
        }), 1f, 0f, scale + .8);
        tween.TweenCallback(Callable.From(eye.QueueFree));
    }

    private static void Burst(Node2D root)
    {
        var texture = EntryRayTexture();
        for (int i = 0; i < 20; i++)
        {
            float angle = CosmeticRandom.NextSingle() * Mathf.Tau;
            float distance = 800 + CosmeticRandom.NextSingle() * 400;
            float scale = 4 + CosmeticRandom.NextSingle();
            var origin = root.GlobalPosition + new Vector2(CosmeticRandom.Next(-10, 11), -95 + CosmeticRandom.Next(-10, 11));
            var direction = Vector2.FromAngle(angle);
            // Watcher shows every line immediately at the center; its random
            // delay postpones the inward motion, not the initial flash.
            var ray = new Sprite2D { Name = "DivinityEntryRay", TopLevel = true, Texture = texture,
                Rotation = angle, Modulate = Colors.Pink,
                Material = new CanvasItemMaterial { BlendMode = CanvasItemMaterial.BlendModeEnum.Add } };
            root.AddChild(ray);
            ray.GlobalPosition = origin;
            var tween = ray.CreateTween();
            tween.TweenInterval(CosmeticRandom.NextDouble() * .4);
            tween.TweenMethod(Callable.From<float>(remaining =>
            {
                float t = remaining * 2;
                ray.GlobalPosition = origin + new Vector2(direction.X * distance * Mathf.Lerp(.02f, .95f, t * t),
                    direction.Y * distance * Mathf.Lerp(.02f, .95f, t * t * t));
                ray.Scale = Vector2.One * scale * (remaining + .1f);
                var color = Colors.Pink;
                color.A = t * t * t;
                ray.Modulate = color;
            }), .5f, 0f, .25);
            tween.TweenCallback(Callable.From(ray.QueueFree));
        }
    }

    private static void Border(Node2D root)
    {
        var layer = new CanvasLayer { Name = "DivinityBorder", Layer = 2 };
        root.AddChild(layer);
        var rect = new ColorRect { MouseFilter = Control.MouseFilterEnum.Ignore, Modulate = new(1, 1, 1, 0),
            Material = new ShaderMaterial { Shader = ResourceLoader.Load<Shader>(Path + "divinity_border.gdshader") } };
        layer.AddChild(rect);
        rect.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        var tween = rect.CreateTween();
        tween.TweenProperty(rect, "modulate:a", 1, .65).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        tween.TweenInterval(.35);
        tween.TweenProperty(rect, "modulate:a", 0, 2.4).SetEase(Tween.EaseType.InOut).SetTrans(Tween.TransitionType.Sine);
        tween.TweenCallback(Callable.From(layer.QueueFree));
    }

    internal static AudioStreamPlayer? PlaySound(Node root, string sound)
    {
        var settings = SaveManager.Instance.SettingsSave;
        float volume = settings.VolumeMaster * settings.VolumeSfx;
        // Native settings already apply squared master/SFX gain to Godot's buses.
        // Applying those gains again here made stance sounds far quieter than
        // Watcher's SFX-bus playback (another 12 dB at the default 50% sliders).
        if (volume <= 0) { Diagnostics.Write("info", "observer.audio.muted", new { sound }); return null; }
        CustomAudioVolumes.Sync();
        var stream = AudioStreamOggVorbis.LoadFromBuffer(Godot.FileAccess.GetFileAsBytes(Path + sound + ".ogg"));
        if (stream == null)
        {
            Diagnostics.Write("error", "observer.audio.load_failed", new { sound });
            return null;
        }
        var player = new AudioStreamPlayer { Name = "ObserverStanceSound", Stream = stream, Bus = "SFX", VolumeDb = 0 };
        // One-shots must finish even if the stance is removed immediately.
        (NCombatRoom.Instance as Node ?? root).AddChild(player);
        player.Finished += player.QueueFree;
        player.Play();
        Diagnostics.Write("info", "observer.audio.play", new { sound, bus = player.Bus.ToString(),
            gainDb = player.VolumeDb, playing = player.Playing, seconds = stream.GetLength(),
            driver = AudioServer.GetDriverName() });
        return player;
    }
}
