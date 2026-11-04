using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Saves;

namespace PunchConstruct;

internal static partial class PlaytestRunner
{
    private static async Task SelectionChecks(NMainMenu menu)
    {
        Check(!OS.GetCmdlineArgs().Contains("--headless"), "Selection test uses native UI and real audio driver");
        SaveManager.Instance.Progress.EnableFtues = false;
        var progress = SaveManager.Instance.Progress;
        var stats = progress.GetOrCreateCharacterStats(ModelDb.GetId<ConstructCharacter>());
        stats.MaxAscension = 0;
        stats.PreferredAscension = 0;
        var native = progress.GetOrCreateCharacterStats(ModelDb.GetId<Ironclad>());
        int nativeMax = native.MaxAscension;
        int globalMax = progress.MaxMultiplayerAscension;
        int wins = stats.TotalWins, losses = stats.TotalLosses;
        var sub = menu.OpenSingleplayerSubmenu();
        AccessTools.Method(typeof(NSingleplayerSubmenu), "OpenCharacterSelect").Invoke(sub, [null]);
        await Frames(30);
        var screen = Descendants(menu).OfType<NCharacterSelectScreen>().Single();
        var button = Descendants(screen).OfType<NCharacterSelectButton>().Single(b => b.Character is ConstructCharacter);
        button.Deselect();

        var settings = SaveManager.Instance.SettingsSave;
        float oldMaster = settings.VolumeMaster, oldSfx = settings.VolumeSfx;
        int master = AudioServer.GetBusIndex("Master"), sfx = AudioServer.GetBusIndex("SFX");
        Check(master >= 0 && sfx >= 0, "Native Master and SFX buses exist");
        bool muted = AudioServer.IsBusMute(master);
        float masterDb = AudioServer.GetBusVolumeDb(master), sfxDb = AudioServer.GetBusVolumeDb(sfx);
        var capture = new AudioEffectCapture { BufferLength = 1 };
        int effect = AudioServer.GetBusEffectCount(master);
        var nativeMaster = Engine.GetSingleton("FmodServer").Call("get_bus", "bus:/").AsGodotObject();
        Check(nativeMaster != null && nativeMaster.Call("is_valid").AsBool(), "Native output bus is available for silent diagnostic capture");
        bool nativeMuted = nativeMaster!.Call("get_mute").AsBool();
        nativeMaster.Call("set_mute", true);
        AudioServer.SetBusMute(master, true);
        AudioServer.AddBusEffect(master, capture);
        try
        {
            settings.VolumeMaster = settings.VolumeSfx = .5f;
            NAudioManager.Instance!.SetMasterVol(.5f);
            NAudioManager.Instance.SetSfxVol(.5f);
            var backend = (Node)AccessTools.Field(typeof(NAudioManager), "_audioNode").GetValue(NAudioManager.Instance)!;
            GD.Print($"[Construct AUDIO ROUTE] backend={backend.GetClass()}; script={(backend.GetScript().AsGodotObject() as Resource)?.ResourcePath}");
            for (int i = 0; i < AudioServer.BusCount; i++)
                GD.Print($"[Construct AUDIO BUS] {AudioServer.GetBusName(i)} mute={AudioServer.IsBusMute(i)} db={AudioServer.GetBusVolumeDb(i)} send={AudioServer.GetBusSend(i)}");
            foreach (var cue in new[] { ConstructPresentation.SingleSfx, ConstructPresentation.DoubleSfx, ConstructPresentation.GuardSfx })
            {
                capture.ClearBuffer();
                SfxCmd.Play(cue);
                float nativePeak = 0;
                for (int frame = 0; frame < 45; frame++)
                {
                    await Frames(1);
                    foreach (var sample in capture.GetBuffer(capture.GetFramesAvailable()))
                        nativePeak = Math.Max(nativePeak, Math.Max(Math.Abs(sample.X), Math.Abs(sample.Y)));
                }
                GD.Print($"[Construct AUDIO OUTPUT] cue={cue}; masterPeak={nativePeak:F6}");
            }
            capture.ClearBuffer();
            button.Select(); // Real native button -> screen -> native compiled audio call.
            var sounds = NGame.Instance!.GetChildren().OfType<AudioStreamPlayer>()
                .Where(p => p.Name == "ConstructSelectSound" && !p.IsQueuedForDeletion()).ToArray();
            Check(sounds.Length == 1 && sounds[0].Playing && sounds[0].Bus == "SFX", "Actual character button starts exactly one custom selection sound");
            float peak = 0;
            for (int frame = 0; frame < 90 && peak < .0001f; frame++)
            {
                await Frames(1);
                foreach (var sample in capture.GetBuffer(capture.GetFramesAvailable()))
                    peak = Math.Max(peak, Math.Max(Math.Abs(sample.X), Math.Abs(sample.Y)));
            }
            Check(peak > .0001f && AudioServer.IsBusMute(master), "Actual selection button reaches the final Master mix under native 50-percent volume settings while speakers remain muted");
            Check(Math.Abs(AudioServer.GetBusVolumeDb(master) - Mathf.LinearToDb(.25f)) < .001f &&
                Math.Abs(AudioServer.GetBusVolumeDb(sfx) - Mathf.LinearToDb(.25f)) < .001f,
                "Custom playback initializes the real Godot buses from saved sliders without artificial QA-only bus overrides");
            GD.Print($"[Construct SELECTION AUDIO] actual_button_peak={peak:F6}; driver={AudioServer.GetDriverName()}; masterMuted=true");
            NAudioManager.Instance!.PlayOneShot(CharacterSelectAudio.Cue);
            SfxCmd.Play(CharacterSelectAudio.Cue);
            Check(NGame.Instance.GetChildren().OfType<AudioStreamPlayer>().Count(p => p.Name == "ConstructSelectSound" && !p.IsQueuedForDeletion()) == 1,
                "Native audio-manager and command routes deduplicate the same custom one-shot");
            foreach (var sound in NGame.Instance.GetChildren().OfType<AudioStreamPlayer>().Where(p => p.Name == "ConstructSelectSound"))
            { sound.Stop(); sound.QueueFree(); }
            await Frames(2);
            settings.VolumeSfx = 0;
            button.Deselect(); button.Select();
            Check(!NGame.Instance.GetChildren().OfType<AudioStreamPlayer>().Any(p => p.Name == "ConstructSelectSound" && !p.IsQueuedForDeletion()), "Actual button respects zero SFX setting");
            settings.VolumeSfx = .5f; settings.VolumeMaster = 0;
            button.Deselect(); button.Select();
            Check(!NGame.Instance.GetChildren().OfType<AudioStreamPlayer>().Any(p => p.Name == "ConstructSelectSound" && !p.IsQueuedForDeletion()), "Actual button respects zero master setting");
        }
        finally
        {
            settings.VolumeMaster = oldMaster; settings.VolumeSfx = oldSfx;
            NAudioManager.Instance!.SetMasterVol(oldMaster);
            NAudioManager.Instance.SetSfxVol(oldSfx);
            AudioServer.RemoveBusEffect(master, effect);
            AudioServer.SetBusVolumeDb(master, masterDb); AudioServer.SetBusVolumeDb(sfx, sfxDb);
            AudioServer.SetBusMute(master, muted);
            nativeMaster.Call("set_mute", nativeMuted);
        }

        var lobby = (StartRunLobby)AccessTools.Field(typeof(NCharacterSelectScreen), "_lobby").GetValue(screen)!;
        Check(ConstructDifficulty.Maximum == 10 && lobby.MaxAscension == 10 && stats.MaxAscension == 10,
            "Fresh zero-progress Construct immediately exposes all native ascensions through level ten");
        Check(lobby.Ascension == 0 && stats.PreferredAscension == 0 && stats.TotalWins == wins && stats.TotalLosses == losses,
            "Unlocking does not select the hardest difficulty or fabricate run records");
        var panel = (NAscensionPanel)AccessTools.Field(typeof(NCharacterSelectScreen), "_ascensionPanel").GetValue(screen)!;
        for (int level = 1; level <= 10; level++)
        {
            AccessTools.Method(typeof(NAscensionPanel), "IncrementAscension").Invoke(panel, []);
            Check(lobby.Ascension == level, "Actual ascension panel can select level " + level);
        }
        AccessTools.Method(typeof(NAscensionPanel), "IncrementAscension").Invoke(panel, []);
        Check(lobby.Ascension == 10, "Ascension arrows retain the native maximum");
        var ironButton = Descendants(screen).OfType<NCharacterSelectButton>().Single(b => b.Character is Ironclad);
        ironButton.Select();
        Check(native.MaxAscension == nativeMax && lobby.MaxAscension == nativeMax, "Switching to a native character restores that character's actual difficulty limit");
        button.Select();
        Check(lobby.MaxAscension == 10 && lobby.Ascension == 10, "Switching back keeps Construct's selected difficulty");
        Check(progress.MaxMultiplayerAscension == globalMax, "Account-wide multiplayer progression remains unchanged");

        StartRunLobbyPlayer Construct(int slot) => new() { id = (ulong)(slot + 1), slotId = slot, character = ModelDb.Character<ConstructCharacter>(), maxMultiplayerAscensionUnlocked = 0 };
        var group = new[] { Construct(0), Construct(1) };
        Check(ConstructDifficulty.LobbyMaximum(group) == 10, "Fresh Construct party has all difficulties without account multiplayer progress");
        group[1].character = ModelDb.Character<Ironclad>(); group[1].maxMultiplayerAscensionUnlocked = 3;
        Check(ConstructDifficulty.LobbyMaximum(group) == 3, "Mixed party retains native teammate's difficulty limit");

        Descendants(menu).OfType<NSubmenuStack>().Single().Pop();
        await Frames(20);
        AccessTools.Method(typeof(NSingleplayerSubmenu), "OpenCustomScreen").Invoke(sub, [null]);
        await Frames(20);
        var custom = Descendants(menu).OfType<NCustomRunScreen>().Single();
        var customButton = Descendants(custom).OfType<NCharacterSelectButton>().Single(b => b.Character is ConstructCharacter);
        customButton.Select();
        Check(custom.Lobby.MaxAscension == 10, "Seed and challenge setup also exposes all Construct difficulties");
        custom.Lobby.SyncAscensionChange(10);
        Check(custom.Lobby.Ascension == 10, "Custom seeded lobby accepts native maximum difficulty");
        await Capture("qa-selection-ascension-ten");
        await QaStartupPreload.Wait(_tree);
    }
}
