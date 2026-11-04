using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace PunchConstruct;

// Rendered controls only; no run is launched and no combat is played.
[HarmonyPatch(typeof(NMainMenu), nameof(NMainMenu._Ready))]
internal static class RelicVisualChecks
{
    private static void Postfix(NMainMenu __instance)
    {
        if (OS.GetCmdlineArgs().Contains("--punchconstruct-relicvisualcheck"))
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
            bool Equal(Texture2D actual, Texture2D original, bool flip, bool flipX = false)
            {
                using var expected = RelicIcons.ReadPixels(original);
                using var image = RelicIcons.ReadPixels(actual);
                if (flip) expected.FlipY();
                if (flipX) expected.FlipX();
                expected.Convert(Image.Format.Rgba8);
                image.Convert(Image.Format.Rgba8);
                return expected.GetSize() == image.GetSize() && expected.GetData().SequenceEqual(image.GetData());
            }
            void CheckCanvas(Texture2D actual, Texture2D original, string label)
            {
                Check(actual.GetSize() == original.GetSize() && (original is not AtlasTexture originalAtlas ||
                    actual is AtlasTexture adapted && adapted.Margin == originalAtlas.Margin && adapted.FilterClip == originalAtlas.FilterClip),
                    "Actual control preserves native canvas and subject placement: " + label);
            }
            Image Canvas(Texture2D texture)
            {
                var content = RelicIcons.ReadPixels(texture);
                if (texture is not AtlasTexture atlas) return content;
                using (content)
                {
                    var size = texture.GetSize();
                    var result = Image.CreateEmpty((int)size.X, (int)size.Y, false, Image.Format.Rgba8);
                    result.Fill(Colors.Transparent);
                    result.BlitRect(content, new Rect2I(Vector2I.Zero, content.GetSize()), (Vector2I)atlas.Margin.Position);
                    return result;
                }
            }

            var unlocks = new UnlockState([], [], 0);
            var player = Player.CreateForNewRun<ConstructCharacter>(unlocks, 7);
            _ = RunState.CreateForTest([player]);
            var vanilla = Player.CreateForNewRun<Ironclad>(unlocks, 8);
            _ = RunState.CreateForTest([vanilla]);
            var nativeBasin = ModelDb.Relic<RippleBasin>();
            var basin = nativeBasin.ToMutable();
            player.AddRelicInternal(basin);
            using var holder = NRelicInventoryHolder.Create(basin) ?? throw new InvalidOperationException("Inventory holder unavailable");
            tree.Root.AddChild(holder);
            Check(Equal(holder.Relic.Icon.Texture, nativeBasin.Icon, true), "Owned basin in actual inventory holder is vertically flipped");
            Check(Equal(holder.Relic.Outline.Texture, nativeBasin.IconOutline, true), "Inventory hover outline is flipped with the basin");
            CheckCanvas(holder.Relic.Icon.Texture, nativeBasin.Icon, "basin icon");
            CheckCanvas(holder.Relic.Outline.Texture, nativeBasin.IconOutline, "basin hover outline");
            for (int i = 0; i < 3; i++) holder.Relic.Call("Reload");
            Check(Equal(holder.Relic.Icon.Texture, nativeBasin.Icon, true), "Repeated reload does not flip basin back");
            using (var icon = Canvas(holder.Relic.Icon.Texture))
                icon.SavePng(OS.GetExecutablePath().GetBaseDir() + "/basin-inventory-fixed.png");
            using (var icon = Canvas(nativeBasin.Icon))
                icon.SavePng(OS.GetExecutablePath().GetBaseDir() + "/basin-inventory-native.png");

            foreach (var size in new[] { NRelic.IconSize.Small, NRelic.IconSize.Large })
            {
                var preview = nativeBasin.ToMutable();
                RelicPreviewContext.Register(preview, player);
                using var view = NRelic.Create(preview, size) ?? throw new InvalidOperationException("Preview control unavailable");
                tree.Root.AddChild(view);
                Check(Equal(view.Icon.Texture, size == NRelic.IconSize.Small ? nativeBasin.Icon : nativeBasin.BigIcon, true),
                    "Unowned reward/shop preview is flipped: " + size);
                CheckCanvas(view.Icon.Texture, size == NRelic.IconSize.Small ? nativeBasin.Icon : nativeBasin.BigIcon, "basin preview " + size);
            }

            var nativeOwned = nativeBasin.ToMutable();
            vanilla.AddRelicInternal(nativeOwned);
            holder.Relic.Model = nativeOwned;
            Check(Equal(holder.Relic.Icon.Texture, nativeBasin.Icon, false) && Equal(holder.Relic.Outline.Texture, nativeBasin.IconOutline, false),
                "Switching same control to another character restores original icon and outline");
            holder.Relic.Model = basin;
            Check(Equal(holder.Relic.Icon.Texture, nativeBasin.Icon, true), "Switching back restores construct adaptation");

            foreach (var original in new RelicModel[] { ModelDb.Relic<HelicalDart>(), ModelDb.Relic<RuinedHelmet>() })
            {
                var owned = original.ToMutable();
                player.AddRelicInternal(owned);
                holder.Relic.Model = owned;
                Check(!Equal(holder.Relic.Icon.Texture, original.Icon, false), "Inventory also uses silver recolor: " + original.Id);
                Check(Equal(holder.Relic.Outline.Texture, original.IconOutline, false), "Silver recolor retains native white outline: " + original.Id);
                CheckCanvas(holder.Relic.Icon.Texture, original.Icon, "silver icon " + original.Id);
            }
            var rug = ModelDb.Relic<DingyRug>().ToMutable(); player.AddRelicInternal(rug);
            holder.Relic.Model = rug;
            Check(Equal(holder.Relic.Icon.Texture, ModelDb.Relic<DingyRug>().Icon, false, true), "Adapted rug uses a horizontally mirrored actual inventory icon");
            Check(Equal(holder.Relic.Outline.Texture, ModelDb.Relic<DingyRug>().IconOutline, false, true), "Adapted rug inventory outline follows its mirrored icon");
            Check(Equal(rug.BigIcon, ModelDb.Relic<DingyRug>().BigIcon, false, true), "Adapted rug preview uses the same horizontal mirror");
            var nativeRug = ModelDb.Relic<DingyRug>().ToMutable(); vanilla.AddRelicInternal(nativeRug);
            holder.Relic.Model = nativeRug;
            Check(Equal(holder.Relic.Icon.Texture, ModelDb.Relic<DingyRug>().Icon, false), "Other characters retain the original rug inventory icon");
            var gem = ModelDb.Relic<PrismaticGem>().ToMutable(); player.AddRelicInternal(gem);
            holder.Relic.Model = gem;
            Check(Equal(holder.Relic.Icon.Texture, ModelDb.Relic<PrismaticGem>().Icon, true, true), "Construct Gem inventory icon is turned 180 degrees");
            Check(Equal(holder.Relic.Outline.Texture, ModelDb.Relic<PrismaticGem>().IconOutline, true, true), "Gem outline follows the turned icon");
            CheckCanvas(holder.Relic.Icon.Texture, ModelDb.Relic<PrismaticGem>().Icon, "Gem canvas");
            Check(Equal(gem.BigIcon, ModelDb.Relic<PrismaticGem>().BigIcon, true, true), "Gem big preview is also turned");
            for (int i = 0; i < 3; i++) holder.Relic.Call("Reload");
            Check(Equal(holder.Relic.Icon.Texture, ModelDb.Relic<PrismaticGem>().Icon, true, true), "Repeated Gem refresh retains one rotation");
            var nativeGem = ModelDb.Relic<PrismaticGem>().ToMutable(); vanilla.AddRelicInternal(nativeGem);
            holder.Relic.Model = nativeGem;
            Check(Equal(holder.Relic.Icon.Texture, ModelDb.Relic<PrismaticGem>().Icon, false), "Other characters retain the original Gem icon");
            await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            GD.Print($"[Construct RELIC VISUAL PASS] {checks} checks; actual rendered inventory/preview controls, no combat");
            tree.Quit();
        }
        catch (Exception ex)
        {
            GD.Print("[Construct QA FAIL] " + ex);
            tree.Quit(1);
        }
    }
}
