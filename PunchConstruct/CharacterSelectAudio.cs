using Godot;
using HarmonyLib;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Saves;

namespace PunchConstruct;

internal static class CharacterSelectAudio
{
    internal const string Cue = "punchconstruct:character_select";
    internal const string Path = "res://PunchConstruct/audio/character_select.wav";
    internal static AudioStreamWav Load() => AudioStreamWav.LoadFromFile(Path)
        ?? throw new InvalidOperationException("Cannot load Construct selection cue.");
    internal static AudioStreamPlayer? Play(float volume)
    {
        var settings = SaveManager.Instance.SettingsSave;
        if (settings.VolumeMaster <= 0 || settings.VolumeSfx <= 0 || volume <= 0 || NGame.Instance == null) return null;
        CustomAudioVolumes.Sync();
        // One local one-shot; native buses already apply the user's volume sliders.
        foreach (var previous in NGame.Instance.GetChildren().OfType<AudioStreamPlayer>().Where(p => p.Name == "ConstructSelectSound"))
        {
            previous.Stop(); NGame.Instance.RemoveChild(previous); previous.QueueFree();
        }
        var sound = new AudioStreamPlayer { Name = "ConstructSelectSound", Stream = Load(), Bus = "SFX", VolumeLinear = volume,
            ProcessMode = Node.ProcessModeEnum.Always };
        NGame.Instance.AddChild(sound);
        sound.Finished += sound.QueueFree;
        sound.Play();
        Diagnostics.Write("info", "character_select.audio", new { cue = Cue, seconds = sound.Stream.GetLength(), bus = "SFX",
            masterDb = AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("Master")),
            sfxDb = AudioServer.GetBusVolumeDb(AudioServer.GetBusIndex("SFX")), driver = AudioServer.GetDriverName() });
        return sound;
    }
}

// Both direct selection and the native random-character path call this overload.
[HarmonyPatch(typeof(SfxCmd), nameof(SfxCmd.Play), [typeof(string), typeof(float)])]
internal static class ConstructSelectionCue
{
    private static bool Prefix(string sfx, float volume)
    {
        if (sfx != CharacterSelectAudio.Cue) return true;
        CharacterSelectAudio.Play(volume);
        return false;
    }
}

// Native release code may inline SfxCmd.Play into its caller. Intercept the
// actual audio-manager boundary too, including real character-selection UI.
[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.PlayOneShot), [typeof(string), typeof(float)])]
internal static class ConstructSelectionAudioOutput
{
    private static bool Prefix(string path, float volume)
    {
        if (path != CharacterSelectAudio.Cue) return true;
        CharacterSelectAudio.Play(volume);
        return false;
    }
}

// Those native callers may already contain an inlined audio command/manager
// body. Recompile their original IL with an explicit non-inlineable dispatch.
[HarmonyPatch]
internal static class ConstructSelectionUiAudio
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(NCharacterSelectScreen), nameof(NCharacterSelectScreen.SelectCharacter));
        yield return AccessTools.Method(typeof(NCharacterSelectScreen), "OnLocalCharacterChangedForRandom");
        yield return AccessTools.Method(typeof(NCustomRunScreen), nameof(NCustomRunScreen.SelectCharacter));
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void Play(string sfx, float volume)
    {
        if (sfx == CharacterSelectAudio.Cue) CharacterSelectAudio.Play(volume);
        else SfxCmd.Play(sfx, volume);
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var original = AccessTools.Method(typeof(SfxCmd), nameof(SfxCmd.Play), [typeof(string), typeof(float)]);
        var replacement = AccessTools.Method(typeof(ConstructSelectionUiAudio), nameof(Play));
        foreach (var instruction in instructions)
        {
            if (instruction.Calls(original)) { instruction.opcode = OpCodes.Call; instruction.operand = replacement; }
            yield return instruction;
        }
    }
}
