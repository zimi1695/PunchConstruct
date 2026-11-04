using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static Godot.Collections.Dictionary PosterState(Control node) => node.Call("animation_state").AsGodotDictionary();

    private static async Task<Godot.Image> PosterFrame(Control node, double time)
    {
        node.Call("set_preview_time", time);
        await Frames(2);
        await _tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        return _tree.Root.GetTexture().GetImage();
    }

    private static int PixelDifferences(Godot.Image a, Godot.Image b, Rect2I rect)
    {
        if (a.GetFormat() != b.GetFormat() || a.GetFormat() is not (Godot.Image.Format.Rgb8 or Godot.Image.Format.Rgba8))
            throw new InvalidOperationException("Unexpected captured image formats");
        var left = a.GetData();
        var right = b.GetData();
        int channels = a.GetFormat() == Godot.Image.Format.Rgb8 ? 3 : 4;
        int width = a.GetWidth();
        int differences = 0;
        for (int y = rect.Position.Y; y < rect.End.Y; ++y)
            for (int x = rect.Position.X; x < rect.End.X; ++x)
            {
                int offset = (y * width + x) * channels;
                if (Math.Max(Math.Abs(left[offset] - right[offset]), Math.Max(Math.Abs(left[offset + 1] - right[offset + 1]), Math.Abs(left[offset + 2] - right[offset + 2]))) > 3)
                    ++differences;
            }
        return differences;
    }

    private static async Task PosterChecks(NMainMenu menu)
    {
        Check(!OS.GetCmdlineArgs().Contains("--headless"), "Poster acceptance uses the actual renderer");
        await Frames(90);
        var sub = menu.OpenSingleplayerSubmenu();
        AccessTools.Method(typeof(NSingleplayerSubmenu), "OpenCharacterSelect").Invoke(sub, [null]);
        await Frames(60);
        var screen = Descendants(menu).OfType<NCharacterSelectScreen>().Single();
        var construct = Descendants(screen).OfType<NCharacterSelectButton>().Single(b => b.Character is ConstructCharacter);
        var ironclad = Descendants(screen).OfType<NCharacterSelectButton>().Single(b => b.Character is Ironclad);
        screen.SelectCharacter(construct, ModelDb.Character<ConstructCharacter>());
        await Frames(90);
        // The native screen renames each root to its character model ID.
        Control FindPoster() => Descendants(screen).OfType<Control>().Single(n => n.HasMethod("animation_state") && n.IsVisibleInTree());
        var poster = FindPoster();
        Check(poster.HasMethod("animation_state") && PosterState(poster)["ready"].AsBool(), "Exported GDScript and motion shader initialize inside the real selection screen");
        Check(!poster.GetNode<TextureRect>("Poster").Visible && poster.ClipContents, "Animated scene takes over only after initialization; static fallback and clipping remain available");
        var backdrop = poster.GetNode<Sprite2D>("AnimatedPortrait/Backdrop");
        var actor = poster.GetNode<MeshInstance2D>("AnimatedPortrait/ConstructPortrait");
        var ambient = poster.GetNode<Node2D>("AnimatedPortrait/HarborMotes");
        var sparks = poster.GetNode<Node2D>("AnimatedPortrait/GloveSparks");
        Check(backdrop.Texture.GetSize() == new Vector2(1920, 1080) && actor.Texture.GetSize() == new Vector2(1920, 1080), "Separate full-size background and alpha foreground preserve approved framing");
        Check(actor.Mesh is ArrayMesh && actor.Material is ShaderMaterial, "Foreground uses a subdivided mesh; background is independent");
        Check(PosterState(poster)["particles"].AsInt32() == 34 && ambient.Visible && sparks.Visible,
            "Sparse harbor motes and glove sparks are separate visible particle layers");
        using (var alpha = actor.Texture.GetImage())
        {
            Check(alpha.GetPixel(50, 100).A == 0 && alpha.GetPixel(1300, 300).A > .99, "Foreground is genuinely transparent outside the character and opaque on its face");
        }
        double before = PosterState(poster)["time"].AsDouble();
        int ticks = PosterState(poster)["ticks"].AsInt32();
        await Frames(40);
        Check(PosterState(poster)["time"].AsDouble() > before && PosterState(poster)["ticks"].AsInt32() > ticks,
            "Actual frame processing advances the selection animation");
        var backdropTransform = backdrop.GlobalTransform;
        using (var first = await PosterFrame(poster, 0))
        using (var inhale = await PosterFrame(poster, 2.4))
        using (var loop = await PosterFrame(poster, 9.6))
        {
            var face = new Rect2I(first.GetWidth() / 2, first.GetHeight() / 10, first.GetWidth() * 45 / 100, first.GetHeight() / 2);
            int movement = PixelDifferences(first, inhale, face);
            int background = PixelDifferences(first, inhale, new Rect2I(15, 15, 170, 130));
            int seam = PixelDifferences(first, loop, face);
            GD.Print($"[Construct POSTER PIXELS] actor={movement} background={background} loop={seam}");
            Check(movement > 1000, "Rendered face/shoulders actually move between phases");
            Check(backdrop.GlobalTransform == backdropTransform, "Background layer stays fixed while character and particles move");
            Check(seam < 100, "Nine-point-six-second motion cycle returns without a visible jump");
            ambient.Visible = false;
            sparks.Visible = false;
            using var withoutParticles = await PosterFrame(poster, 0);
            int particlePixels = PixelDifferences(first, withoutParticles, new Rect2I(0, 0, first.GetWidth(), first.GetHeight()));
            GD.Print($"[Construct POSTER PARTICLES] visiblePixels={particlePixels}");
            Check(particlePixels > 50, "Actual renderer draws both particle layers; these are not only configured nodes");
            ambient.Visible = true;
            sparks.Visible = true;
            var canvas = poster.GetNode<Node2D>("AnimatedPortrait");
            canvas.Visible = false;
            using var withoutPortrait = await PosterFrame(poster, 0);
            var title = (Control)AccessTools.Field(typeof(NCharacterSelectScreen), "_name").GetValue(screen)!;
            var titleRect = title.GetGlobalRect();
            // UI coordinates are logical; viewport captures use the native
            // internal render resolution (which can be larger than the window).
            var pixelsPerUnit = (Vector2)first.GetSize() / poster.GetViewportRect().Size;
            var titlePixels = new Rect2I((Vector2I)(titleRect.Position * pixelsPerUnit), (Vector2I)(titleRect.Size * pixelsPerUnit))
                .Intersection(new Rect2I(0, 0, first.GetWidth(), first.GetHeight()));
            int readable = 0;
            for (int y = titlePixels.Position.Y; y < titlePixels.End.Y; ++y)
                for (int x = titlePixels.Position.X; x < titlePixels.End.X; ++x)
                {
                    var ink = withoutPortrait.GetPixel(x, y);
                    var painted = first.GetPixel(x, y);
                    if (ink.R > .7 && ink.G > .5 && ink.B < .4 && Math.Abs(ink.R - painted.R) < .008 &&
                        Math.Abs(ink.G - painted.G) < .008 && Math.Abs(ink.B - painted.B) < .008) ++readable;
                }
            canvas.Visible = true;
            first.SavePng(OS.GetExecutablePath().GetBaseDir() + "/qa-poster-ui.png");
            withoutPortrait.SavePng(OS.GetExecutablePath().GetBaseDir() + "/qa-poster-ui-baseline.png");
            GD.Print($"[Construct POSTER UI] visibleTitlePixels={readable} rect={titleRect} viewport={poster.GetViewportRect().Size} image={first.GetSize()} canvasTopLevel={canvas.TopLevel}");
            Check(readable > 50 && !canvas.TopLevel, "Actual native title renders above the animated artwork; UI is not covered");
        }
        for (int i = 0; i < 128; ++i)
        {
            using var image = await PosterFrame(poster, i * 9.6 / 128);
            image.Resize(1280, 720, Godot.Image.Interpolation.Lanczos);
            var error = image.SaveWebp(OS.GetExecutablePath().GetBaseDir() + $"/qa-poster-frame-{i:D3}.webp", false, .88f);
            if (error != Error.Ok) throw new InvalidOperationException("Poster frame write: " + error);
        }
        Check(true, "Captured 128 actual selection-screen frames for a looping animation preview");
        poster.Call("resume_animation");
        double hiddenTime = PosterState(poster)["time"].AsDouble();
        screen.SelectCharacter(ironclad, ModelDb.Character<Ironclad>());
        await Frames(40);
        Check(!Descendants(screen).OfType<Control>().Any(n => n.HasMethod("animation_state") && n.IsVisibleInTree()), "Selecting another character hides/frees the construct poster");
        if (GodotObject.IsInstanceValid(poster))
            Check(PosterState(poster)["time"].AsDouble() == hiddenTime, "Inactive portrait clock does not keep running");
        Check(!Descendants(screen).OfType<MeshInstance2D>().Any(n => n.Name == "ConstructPortrait" && n.IsVisibleInTree()), "No construct mesh leaks into the native character selection");
        screen.SelectCharacter(construct, ModelDb.Character<ConstructCharacter>());
        await Frames(40);
        poster = FindPoster();
        poster.Call("resume_animation");
        before = PosterState(poster)["time"].AsDouble();
        await Frames(30);
        Check(PosterState(poster)["time"].AsDouble() > before, "Re-selecting the construct resumes one working animation");
        var duplicate = ResourceLoader.Load<PackedScene>("res://PunchConstruct/scenes/character_select.tscn").Instantiate<Control>();
        duplicate.Visible = false;
        screen.AddChild(duplicate);
        await Frames(5);
        Check(PosterState(duplicate)["ready"].AsBool(), "Second scene instance initializes independently");
        var secondMaterial = duplicate.GetNode<MeshInstance2D>("AnimatedPortrait/ConstructPortrait").Material;
        Check(secondMaterial.GetInstanceId() != poster.GetNode<MeshInstance2D>("AnimatedPortrait/ConstructPortrait").Material.GetInstanceId(), "Each poster owns a separate shader material and clock");
        Check(PosterState(duplicate)["ticks"].AsInt32() == 0, "Hidden duplicate performs no animation updates");
        duplicate.QueueFree();
        await Frames(3);
        var window = _tree.Root;
        var initialSize = window.Size;
        foreach (var size in new[] { new Vector2I(960, 720), new Vector2I(1680, 720) })
        {
            window.Size = size;
            await Frames(30);
            var visibleSize = poster.GetViewportRect().Size;
            float expected = Math.Max(visibleSize.X / 1920, visibleSize.Y / 1080);
            var paintedCanvas = poster.GetNode<Node2D>("AnimatedPortrait");
            Check(Math.Abs(paintedCanvas.GlobalScale.X - expected) < .001 && Math.Abs(paintedCanvas.GlobalScale.Y - expected) < .001,
                "Shared viewport cover transform compensates native overscan after resize " + size);
            await Capture("qa-poster-aspect-" + size.X);
        }
        window.Size = initialSize;
        await Frames(20);
        await Capture("qa-poster-selected");
        Check(screen.Lobby.LocalPlayer.character is ConstructCharacter, "Selection, lobby and character buttons remain usable without starting a run");
    }
}
