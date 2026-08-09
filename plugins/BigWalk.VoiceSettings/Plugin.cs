using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace BigWalk.VoiceSettings;

[BepInPlugin(Guid, "Better Proximity Voice", Version)]
public class Plugin : BasePlugin
{
    public const string Guid = "com.bigwalk.voicesettings";
    public const string Version = "1.4.0";

    internal static Plugin Instance { get; private set; }
    internal static BepInEx.Logging.ManualLogSource Trace { get; private set; }

    internal ConfigEntry<string> MenuShortcut { get; private set; }
    internal ConfigEntry<string> ToggleTwoDShortcut { get; private set; }
    internal ConfigEntry<string> CycleRangeShortcut { get; private set; }
    internal ConfigEntry<string> ToggleHudShortcut { get; private set; }
    internal ConfigEntry<bool> FreeCursor { get; private set; }
    internal ConfigEntry<float> RangeMultiplier { get; private set; }
    internal ConfigEntry<bool> TwoDVoice { get; private set; }
    internal ConfigEntry<bool> SpeakerIndicators { get; private set; }
    internal ConfigEntry<bool> SpeakerNames { get; private set; }
    internal ConfigEntry<bool> ActiveSpeakerRoster { get; private set; }
    internal ConfigEntry<bool> SpeakerDistances { get; private set; }
    internal ConfigEntry<float> HudScale { get; private set; }
    internal ConfigEntry<float> TalkThreshold { get; private set; }
    internal VoiceSettingsOverlay Overlay { get; private set; }

    public override void Load()
    {
        Instance = this;
        Trace = Log;

        MenuShortcut = Config.Bind("Shortcuts", "OpenMenu", "LeftAlt + V",
            "Shows or hides Advanced Voice Settings. Core controls also appear in Settings > Audio.");
        ToggleTwoDShortcut = Config.Bind("Shortcuts", "ToggleAudioMode", "Not bound",
            "Optionally switches between positional 3D voice and non-positional 2D voice.");
        CycleRangeShortcut = Config.Bind("Shortcuts", "CycleRange", "Not bound",
            "Optionally cycles the voice-range presets: x1, x2, x5, x10, and x20.");
        ToggleHudShortcut = Config.Bind("Shortcuts", "ToggleHud", "Not bound",
            "Optionally shows or hides all speaker HUD elements.");
        FreeCursor = Config.Bind("Menu", "FreeCursorWhenOpen", true,
            "Unlocks and shows the mouse cursor while the voice settings menu is open.");

        RangeMultiplier = Config.Bind("Voice", "RangeMultiplier", 1f,
            "Multiplies the distance over which you hear other players (1-20). " +
            "This changes only your local playback.");
        TwoDVoice = Config.Bind("Voice", "TwoDVoice", false,
            "Plays every received voice non-positionally and without distance attenuation.");
        SpeakerIndicators = Config.Bind("Indicators", "Enabled", true,
            "Draws an icon over every remote player currently speaking.");
        SpeakerNames = Config.Bind("Indicators", "ShowNames", true,
            "Shows each speaking player's display name beside their world indicator.");
        ActiveSpeakerRoster = Config.Bind("Indicators", "ActiveSpeakerRoster", true,
            "Shows a compact roster of current speakers in the lower-right corner.");
        SpeakerDistances = Config.Bind("Indicators", "ShowDistance", true,
            "Shows distance in metres beside each speaking player's name.");
        HudScale = Config.Bind("Indicators", "HudScale", 1f,
            "Scales speaker indicators and the active-speaker roster (0.75-1.5).");
        TalkThreshold = Config.Bind("Indicators", "TalkThreshold", 0.004f,
            "Minimum smoothed voice amplitude treated as speech. Lower this if quiet speakers " +
            "do not light up; raise it if icons appear because of background noise.");

        RangeMultiplier.Value = Mathf.Clamp(RangeMultiplier.Value, 1f, VoiceRangeScaler.MaxScale);
        HudScale.Value = Mathf.Clamp(HudScale.Value, 0.75f, 1.5f);
        TalkThreshold.Value = Mathf.Clamp(TalkThreshold.Value, 0.0001f, 0.1f);

        ClassInjector.RegisterTypeInIl2Cpp<VoiceSettingsOverlay>();
        new Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

        var host = new GameObject("BigWalk.VoiceSettings");
        host.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(host);
        Overlay = host.AddComponent<VoiceSettingsOverlay>();

        Log.LogInfo($"Loaded. Native controls appear in Settings > Audio; {MenuShortcut.Value} opens advanced settings.");
    }
}
