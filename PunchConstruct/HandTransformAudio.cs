using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Vfx.Cards;

namespace PunchConstruct;

// A pending local replacement is consumed by the actual visual model update,
// not by selection completion or a timer. A cancelled/interrupted visual is silent.
internal static class HandTransformAudio
{
    private static readonly HashSet<CardModel> Pending = [];

    internal static async Task Transform(CardModel original, CardModel replacement)
    {
        bool local = LocalContext.IsMine(original);
        if (local) Pending.Add(replacement);
        try { await CardCmd.Transform(original, replacement); }
        finally { if (local) Pending.Remove(replacement); }
    }

    internal static void Reveal(NCard card, CardModel replacement)
    {
        if (!ReferenceEquals(card.Model, replacement) || !card.IsVisibleInTree() || !Pending.Remove(replacement)) return;
        SfxCmd.Play("event:/sfx/ui/cards/card_transform");
        Diagnostics.Write("info", "card.transform.audio", new { card = replacement.Id.ToString(),
            phase = "visual_reveal", frame = Engine.GetProcessFrames() });
    }
}

[HarmonyPatch(typeof(NCardTransformShineVfx), "UpdateCard")]
internal static class FervorTransformRevealSound
{
    private static void Postfix(NCard __0, CardModel __1) => HandTransformAudio.Reveal(__0, __1);
}
