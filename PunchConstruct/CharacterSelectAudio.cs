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

