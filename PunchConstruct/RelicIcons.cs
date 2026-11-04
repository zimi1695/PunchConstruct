using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Nodes.Relics;

namespace PunchConstruct;

// Derived textures live only in memory. Original files and atlas resources are never overwritten.
[HarmonyPatch]
internal static class RelicIcons
{
    private static readonly Dictionary<(ulong, int), Texture2D> Cache = [];
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods() => new[] { "Icon", "IconOutline", "BigIcon" }
        .Select(name => AccessTools.PropertyGetter(typeof(RelicModel), name));
    private static void Postfix(RelicModel __instance, System.Reflection.MethodBase __originalMethod, ref Texture2D? __result)
    {
        if (__result != null) __result = Adapt(__instance, __result, __originalMethod.Name == "get_IconOutline");
    }

    internal static Texture2D Adapt(RelicModel relic, Texture2D source, bool outline = false)
    {
        bool own = relic is ConstructDart or ConstructHelmet;
        if (!own && !CharacterScope.IsConstruct(relic)) return source;
        int operation = relic switch {
            RippleBasin => 1,
            ConstructDart or HelicalDart => 2,
            ConstructHelmet or RuinedHelmet => 3,
            DingyRug => 4,
            PrismaticGem => 5,
            _ => 0
        };
        if (operation == 0 || DisplayServer.GetName() == "headless") return source;
        // Recolor the metal, preserving the native white hover outline and orientation.
        if (operation is 2 or 3 && outline) return source;
        var key = (source.GetInstanceId(), operation);
        if (Cache.TryGetValue(key, out var cached)) return cached;
        using var pixels = ReadPixels(source);
        if (pixels == null || pixels.IsEmpty()) return source;
        if (pixels.IsCompressed()) pixels.Decompress();
        switch (operation)
        {
            case 1: pixels.FlipY(); break;
            case 2: SilverTint(pixels, 1f); break;
            case 3: SilverTint(pixels, .8f); break;
            case 4: pixels.FlipX(); break;
            case 5: pixels.FlipX(); pixels.FlipY(); break;
        }
        Texture2D texture = ImageTexture.CreateFromImage(pixels);
        // The atlas packs only the cropped subject. Its Margin restores the
        // native 85x85 icon canvas and offset. Dropping it moves/scales the
        // transformed subject in the real inventory control.
        if (source is AtlasTexture atlas)
            texture = new AtlasTexture { Atlas = texture, Region = new Rect2(Vector2.Zero, pixels.GetSize()),
                Margin = atlas.Margin, FilterClip = atlas.FilterClip };
        Cache[key] = texture;
        return texture;
    }

    private static void SilverTint(Image pixels, float strength)
    {
        for (int y = 0; y < pixels.GetHeight(); y++)
        for (int x = 0; x < pixels.GetWidth(); x++)
        {
            var original = pixels.GetPixel(x, y);
            if (original.A == 0) continue;
            float light = original.R * .2126f + original.G * .7152f + original.B * .0722f;
            var silver = new Color(light * .97f, light, Mathf.Min(light * 1.025f, 1f), original.A);
            pixels.SetPixel(x, y, original.Lerp(silver, strength));
        }
    }

    // AtlasTexture.GetImage tries to blit the compressed atlas before callers can decompress it.
    internal static Image ReadPixels(Texture2D texture)
    {
        if (texture is AtlasTexture atlas)
        {
            using var source = ReadPixels(atlas.Atlas);
            var region = atlas.Region;
            if (region.Size == Vector2.Zero) return (Image)source.Duplicate();
            return source.GetRegion(new Rect2I((int)region.Position.X, (int)region.Position.Y, (int)region.Size.X, (int)region.Size.Y));
        }
        var pixels = texture.GetImage();
        if (pixels.IsCompressed()) pixels.Decompress();
        return pixels;
    }
}

// The native small Icon getters can be inlined into NRelic.Reload before Harmony
// patches them. Refresh the actual control from original resources as well: never
// transform its current texture, which could already be adapted on a later reload.
[HarmonyPatch(typeof(NRelic), "Reload")]
internal static class RelicControlIcons
{
    private static readonly AccessTools.FieldRef<NRelic, NRelic.IconSize> Size =
        AccessTools.FieldRefAccess<NRelic, NRelic.IconSize>("_iconSize");
    private static readonly AccessTools.FieldRef<NRelic, RelicModel?> Model =
        AccessTools.FieldRefAccess<NRelic, RelicModel?>("_model");
    private static readonly System.Reflection.MethodInfo OutlinePath =
        AccessTools.PropertyGetter(typeof(RelicModel), "PackedIconOutlinePath");

    private static void Postfix(NRelic __instance)
    {
        if (!__instance.IsNodeReady() || __instance.Icon == null || Size(__instance) != NRelic.IconSize.Small) return;
        var relic = Model(__instance);
        if (relic is not (RippleBasin or ConstructDart or HelicalDart or ConstructHelmet or RuinedHelmet)) return;
        var icon = ResourceLoader.Load<Texture2D>(relic.PackedIconPath);
        __instance.Icon.Texture = RelicIcons.Adapt(relic, icon);
        var outline = ResourceLoader.Load<Texture2D>((string)OutlinePath.Invoke(relic, null)!);
        __instance.Outline.Texture = RelicIcons.Adapt(relic, outline, outline: true);
    }
}
