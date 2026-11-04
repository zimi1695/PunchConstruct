using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;

namespace PunchConstruct;

[HarmonyPatch(typeof(NCharacterSelectButton), nameof(NCharacterSelectButton.Init))]
internal static class CharacterPortrait
{
    private static void Postfix(NCharacterSelectButton __instance, CharacterModel character)
    {
        if (character is not ConstructCharacter) return;
        var portrait = ResourceLoader.Load<Texture2D>("res://PunchConstruct/art/character_select_icon.ctex");
        foreach (var field in new[] { "_icon", "_iconAdd" })
        {
            var icon = (TextureRect)AccessTools.Field(typeof(NCharacterSelectButton), field).GetValue(__instance)!;
            icon.Texture = portrait;
            icon.TextureFilter = CanvasItem.TextureFilterEnum.Linear;
            icon.ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize;
            icon.StretchMode = TextureRect.StretchModeEnum.Scale;
        }
    }
}
