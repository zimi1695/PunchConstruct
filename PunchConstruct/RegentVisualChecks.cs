using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace PunchConstruct;

// Actual card controls in the menu only: no combat, progression or user saves.
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class RegentVisualChecks
{
    private static void Postfix(NMainMenu __instance)
    {
        if (OS.GetCmdlineArgs().Contains("--punchconstruct-regentvisualcheck"))
            Callable.From(() => { _ = Run(__instance.GetTree()); }).CallDeferred();
    }
    private static async Task Run(SceneTree tree)
    {
        try
        {
            await QaStartupPreload.Wait(tree);
            if (DisplayServer.GetName() == "headless") throw new InvalidOperationException("Real renderer required");
            int checks = 0;
            void Check(bool ok, string message)
            {
                if (!ok) throw new InvalidOperationException(message);
                GD.Print($"[Construct QA {checks++:D3}] {message}");
            }
            var unlocks = new UnlockState([], [], 0);
            var player = Player.CreateForNewRun<ConstructCharacter>(unlocks, 7);
            var run = RunState.CreateForTest([player]);
            var regent = Player.CreateForNewRun<Regent>(unlocks, 8);
            var nativeRun = RunState.CreateForTest([regent]);
            var layer = new Control();
            tree.Root.AddChild(layer);
            var size = tree.Root.GetVisibleRect().Size;
            var background = new ColorRect { Color = new Color(0.04f, 0.06f, 0.08f), Size = size };
            layer.AddChild(background);
            var heading = new Label { Text = "拳击构装体：取消星费角标（右下为储君原版对照）", Position = new Vector2(40, 20) };
            heading.AddThemeFontSizeOverride("font_size", 28);
            layer.AddChild(heading);
            int index = 0;
            foreach (var (type, change) in RegentAdaptations.Changes)
            {
                var canonical = ModelDb.AllCards.Single(c => c.GetType() == type);
                var card = run.CreateCard(canonical, player);
                var native = nativeRun.CreateCard(canonical, regent);
                var view = NCard.Create(card) ?? throw new InvalidOperationException("Card control unavailable");
                layer.AddChild(view);
                view.Position = new Vector2(size.X * (0.125f + index % 4 * 0.25f), size.Y * (0.29f + index / 4 * 0.46f));
                view.Scale = new Vector2(0.7f, 0.7f);
                var stars = view.GetNode<TextureRect>("%StarIcon");
                var starLabel = view.GetNode<Label>("%StarLabel");
                var energy = view.GetNode<TextureRect>("%EnergyIcon");
                var energyLabel = view.GetNode<Label>("%EnergyLabel");
                view.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
                Check(!stars.Visible && !starLabel.IsVisibleInTree() && energy.Visible && energyLabel.Text == change.Cost.ToString() &&
                    card.CurrentStarCost == 0, "Converted card shows energy only: " + type.Name);
                view.Model = native;
                view.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
                Check(stars.Visible && starLabel.IsVisibleInTree() && native.CurrentStarCost == canonical.CurrentStarCost,
                    "Same control restores native Regent star cost: " + type.Name);
                view.Model = card;
                view.UpdateVisuals(PileType.Deck, CardPreviewMode.Upgrade);
                Check(!stars.Visible, "Upgrade preview hides the removed star badge: " + type.Name);
                card.UpgradeInternal(); card.FinalizeUpgradeInternal();
                view.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
                AccessTools.Method(typeof(NCard), "UpdateStarCostText").Invoke(view, [0]);
                Check(!stars.Visible && energy.Visible && energyLabel.Text == card.EnergyCost.GetWithModifiers(CostModifiers.All).ToString(),
                    "Upgraded card and resource-text refresh keep energy without zero stars: " + type.Name);
                card.DowngradeInternal(); card.FinalizeUpgradeInternal();
                view.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
                Check(!stars.Visible && energyLabel.Text == change.Cost.ToString(), "Downgrade keeps the adapted presentation: " + type.Name);
                index++;
            }
            var comparison = NCard.Create(nativeRun.CreateCard<GammaBlast>(regent))!;
            layer.AddChild(comparison);
            comparison.Position = new Vector2(size.X * 0.875f, size.Y * 0.75f);
            comparison.Scale = new Vector2(0.7f, 0.7f);
            comparison.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
            var zeroStarNative = nativeRun.CreateCard<GammaBlast>(regent);
            AccessTools.Field(typeof(CardModel), "_baseStarCost").SetValue(zeroStarNative, 0);
            AccessTools.Field(typeof(CardModel), "_starCostSet").SetValue(zeroStarNative, true);
            comparison.Model = zeroStarNative;
            comparison.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
            Check(comparison.GetNode<TextureRect>("%StarIcon").Visible,
                "A native Regent card temporarily costing zero stars still displays its resource badge");
            comparison.Model = nativeRun.CreateCard<GammaBlast>(regent);
            comparison.UpdateVisuals(PileType.Deck, CardPreviewMode.Normal);
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            await tree.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            tree.Root.GetTexture().GetImage().SavePng(OS.GetExecutablePath().GetBaseDir() + "/qa-regent-costs.png");
            GD.Print($"[Construct REGENT VISUAL PASS] {checks} checks; seven converted cards and native controls, menu only");
            tree.Quit();
        }
        catch (Exception e)
        {
            GD.PrintErr("[Construct QA FAIL] " + e);
            tree.Quit(1);
        }
    }
}
