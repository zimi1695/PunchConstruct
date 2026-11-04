using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.TreasureRelicPicking;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static float HandInkDistance(Texture2D texture, Vector2 pivot)
    {
        using var pixels = RelicIcons.ReadPixels(texture);
        pixels.Convert(Image.Format.Rgba8);
        var data = pixels.GetData();
        int width = pixels.GetWidth();
        float nearest = float.MaxValue;
        for (int y = 0; y < pixels.GetHeight(); ++y)
            for (int x = 0; x < width; ++x)
                if (data[(y * width + x) * 4 + 3] >= 64)
                    nearest = Math.Min(nearest, new Vector2(x, y).DistanceTo(pivot));
        return nearest;
    }

    private static async Task TreasureCursorChecks(RunState run)
    {
        SaveManager.Instance.Progress.EnableFtues = false;
        var character = ModelDb.Character<ConstructCharacter>();
        var native = ModelDb.Character<Defect>();
        Check(character.ArmPointingTexture.ResourcePath == native.ArmPointingTexture.ResourcePath &&
              character.ArmPaperTexture.ResourcePath == native.ArmPaperTexture.ResourcePath &&
              character.ArmScissorsTexture.ResourcePath == native.ArmScissorsTexture.ResourcePath,
            "Construct uses the correctly calibrated native pointing, paper and scissors textures");
        Check(new[] { character.ArmPointingTexture, character.ArmRockTexture, character.ArmPaperTexture, character.ArmScissorsTexture }
            .Select(t => t.ResourcePath).Distinct().Count() == 4, "Four hand states have four distinct correct assets");
        var pivot = new Vector2(163, 10);
        float proper = HandInkDistance(character.ArmPointingTexture, pivot);
        float broken = HandInkDistance(character.ArmRockTexture, pivot);
        float nativeGap = HandInkDistance(native.ArmPointingTexture, pivot);
        GD.Print($"[Construct TREASURE PIVOT] pointing={proper:F2}px native={nativeGap:F2}px old-rock={broken:F2}px pointSize={character.ArmPointingTexture.GetSize()} rockSize={character.ArmRockTexture.GetSize()}");
        Check(Math.Abs(proper - nativeGap) < .01 && broken > proper + 50,
            "Corrected image matches native pointing calibration; old rock mapping adds a larger visual gap");

        await RunManager.Instance.EnterRoomDebug(RoomType.Treasure, showTransition: false);
        await CoopBarrier("treasure-room");
        var room = Descendants(_tree.Root).OfType<NTreasureRoom>().Single();
        var opening = (Task)AccessTools.Method(typeof(NTreasureRoom), "OpenChest").Invoke(room, [])!;
        await CoopWait(() => Descendants(room).OfType<NTreasureRoomRelicHolder>().Count(h => h.IsVisibleInTree()) == run.Players.Count,
            "Native shared treasure holders appear");
        var hands = Descendants(room).OfType<NHandImageCollection>().Single();
        var sync = (PeerInputSynchronizer)AccessTools.Field(typeof(NHandImageCollection), "_synchronizer").GetValue(hands)!;
        await Frames(90);
        await CoopBarrier("treasure-opened");
        var holders = Descendants(room).OfType<NTreasureRoomRelicHolder>().Where(h => h.IsVisibleInTree()).OrderBy(h => h.Index).ToArray();
        var me = LocalContext.GetMe(run)!;
        int localIndex = run.Players.ToList().IndexOf(me);
        var localHand = hands.GetHand(me.NetId) ?? throw new InvalidOperationException("Local native chest hand was not created");
        var handTexture = localHand.GetNode<TextureRect>("TextureRect");
        Check(handTexture.Texture.ResourcePath == me.Character.ArmPointingTexture.ResourcePath,
            "Actual local treasure hand is initialized with the character's pointing texture");
        foreach (var gesture in Enum.GetValues<RelicPickingFightMove>())
        {
            AccessTools.Method(typeof(NHandImage), "SetTextureToFightMove").Invoke(localHand, [gesture]);
            var expected = gesture switch {
                RelicPickingFightMove.Rock => me.Character.ArmRockTexture,
                RelicPickingFightMove.Paper => me.Character.ArmPaperTexture,
                _ => me.Character.ArmScissorsTexture
            };
            Check(handTexture.Texture.ResourcePath == expected.ResourcePath, "Native fight gesture loads the matching texture: " + gesture);
        }
        handTexture.Texture = me.Character.ArmPointingTexture;
        int caseIndex = 0;
        foreach (var windowSize in new[] { new Vector2I(1280, 720), new Vector2I(960, 720), new Vector2I(1680, 720) })
        {
            _tree.Root.Size = localIndex % 2 == 0 ? windowSize : new Vector2I(1280, 800);
            await Frames(20);
            var targetHolder = holders[localIndex];
            var target = targetHolder.GetGlobalRect().GetCenter();
            sync.SyncLocalIsUsingController(false);
            sync.SyncLocalMousePos(hands.GetGlobalTransformWithCanvas().AffineInverse() * target, hands);
            await CoopBarrier("treasure-pointer-sent-" + caseIndex);
            await Frames(70);
            foreach (var player in run.Players)
            {
                var hand = hands.GetHand(player.NetId) ?? throw new InvalidOperationException("Remote native chest hand was not created");
                var position = sync.GetControlSpaceFocusPosition(player.NetId, hands);
                float difference = hand.GlobalPosition.DistanceTo(position);
                GD.Print($"[Construct TREASURE POINTER] case={caseIndex} player={player.NetId} delta={difference:F2}");
                Check(difference < 4, "Native local/remote hand anchor follows synchronized cursor in window " + windowSize + ", slot " + hand.Index);
                Check(hand.GetNode<TextureRect>("TextureRect").Texture.ResourcePath == player.Character.ArmPointingTexture.ResourcePath,
                    "Each player's displayed pointing image matches its own character");
            }
            if (caseIndex == 0 && me.Character is ConstructCharacter)
            {
                handTexture.Texture = character.ArmRockTexture;
                await Capture("qa-treasure-" + _coopRole + "-old-mapping");
                handTexture.Texture = character.ArmPointingTexture;
            }
            await Capture("qa-treasure-" + _coopRole + "-" + caseIndex);
            var viewportPoint = targetHolder.GetGlobalTransformWithCanvas() * (targetHolder.Size / 2);
            _tree.Root.PushInput(new InputEventMouseMotion { Position = viewportPoint, GlobalPosition = viewportPoint }, true);
            await Frames(3);
            var hover = _tree.Root.GuiGetHoveredControl();
            Check(hover != null && (hover == targetHolder || targetHolder.IsAncestorOf(hover)),
                "Real viewport mouse event hits the visible relic holder at its center");
            await CoopBarrier("treasure-pointer-checked-" + caseIndex++);
        }
        var selected = holders[localIndex];
        var point = selected.GetGlobalTransformWithCanvas() * (selected.Size / 2);
        var relicId = selected.Relic.Model.Id;
        _tree.Root.PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = true }, true);
        _tree.Root.PushInput(new InputEventMouseButton { Position = point, GlobalPosition = point, ButtonIndex = MouseButton.Left, Pressed = false }, true);
        await CoopWait(() => me.Relics.Any(r => r.Id == relicId), "Native click awards the exact relic displayed at the clicked holder");
        Check(me.Relics.Any(r => r.Id == relicId), "Actual mouse press/release selects the visible relic without a shifted click area");
        await CoopWait(() => opening.IsCompleted, "Native chest picking and all hand/relic animations complete");
        await opening;
        await CoopSnapshot(run, "treasure");
    }
}
