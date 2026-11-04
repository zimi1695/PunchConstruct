using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Saves;
using Godot;

namespace PunchConstruct;

internal static class CustomAudioVolumes
{
    // The game's sliders update BOTH the native audio proxy and Godot's audio
    // buses. Custom streams use the latter; do not assume proxy updates do this.
    // Apply the user's existing values through the same native Godot setters.
    // No settings, mute flags or native FMOD bus values are changed here.
    internal static void Sync()
    {
        var settings = SaveManager.Instance.SettingsSave;
        if (NDebugAudioManager.Instance is { } native)
        {
            native.SetMasterAudioVolume(settings.VolumeMaster);
            native.SetSfxAudioVolume(settings.VolumeSfx);
        }
        else
        {
            int master = AudioServer.GetBusIndex("Master"), sfx = AudioServer.GetBusIndex("SFX");
            if (master >= 0) AudioServer.SetBusVolumeDb(master, Mathf.LinearToDb(settings.VolumeMaster * settings.VolumeMaster));
            if (sfx >= 0) AudioServer.SetBusVolumeDb(sfx, Mathf.LinearToDb(settings.VolumeSfx * settings.VolumeSfx));
        }
    }
}
