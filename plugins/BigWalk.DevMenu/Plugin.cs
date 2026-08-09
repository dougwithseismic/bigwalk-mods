using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace BigWalk.DevMenu;

/// <summary>
/// Big Walk ships its dev/cheat components with working logic but with the input
/// path that drove them compiled out (PlayerCheater.Update is an empty `ret`, and
/// nothing constructs DevMenuRow). This plugin supplies a new front end for the
/// parts that survived, plus proximity-voice diagnostics.
/// </summary>
[BepInPlugin(Guid, "Big Walk — Dev Menu", "0.6.2")]
public class Plugin : BasePlugin
{
    public const string Guid = "com.bigwalk.devmenu";

    internal static Plugin Instance { get; private set; }
    internal static BepInEx.Logging.ManualLogSource Trace;

    internal ConfigEntry<KeyCode> MenuKey;
    internal ConfigEntry<KeyCode> FreeCamKey;
    internal ConfigEntry<bool> AllowWorldCheats;
    internal ConfigEntry<bool> FreeCursor;
    internal ConfigEntry<KeyCode> SpeakerIconsKey;
    internal ConfigEntry<bool> SpeakerIcons;
    internal ConfigEntry<bool> SpeakerMarkers3D;
    internal ConfigEntry<bool> MarkersThroughWalls;
    internal ConfigEntry<bool> MarkerLights;
    internal ConfigEntry<bool> MapPins;

    public override void Load()
    {
        Instance = this;
        Trace = Log;

        MenuKey = Config.Bind("Keys", "MenuKey", KeyCode.F1,
            "Shows/hides the dev menu.");
        FreeCamKey = Config.Bind("Keys", "FreeCamKey", KeyCode.F3,
            "Toggles free camera (CameraCheatMover detach/attach).");
        AllowWorldCheats = Config.Bind("Safety", "AllowWorldCheats", true,
            "Enables cheats that mutate shared world state (prop spawning, train position). " +
            "These affect everyone in the lobby - set false to hide them.");

        FreeCursor = Config.Bind("General", "FreeCursorWhenOpen", true,
            "Unlock and show the mouse cursor while the menu is open, so its controls " +
            "are clickable. Your previous cursor state is restored when it closes.");

        SpeakerIconsKey = Config.Bind("Keys", "SpeakerIconsKey", KeyCode.F4,
            "Toggles the on-screen speaker icons over players who are talking.");
        SpeakerIcons = Config.Bind("Voice", "SpeakerIcons", false,
            "Draw a speaker icon over each talking player, coloured green (near) to " +
            "red (edge of audibility) and faded by distance. Off-screen speakers are " +
            "clamped to the screen edge.");

        SpeakerMarkers3D = Config.Bind("Voice", "SpeakerMarkers3D", false,
            "Float an in-world marker over the head of whoever is talking, coloured " +
            "green (near) to red (edge of audibility). Includes a point light, which " +
            "is also the fallback if no usable shader survived the build's stripping.");
        MarkersThroughWalls = Config.Bind("Voice", "MarkersThroughWalls", true,
            "Draw in-world speaker markers through terrain and geometry.");
        MarkerLights = Config.Bind("Voice", "MarkerLights", false,
            "Add a point light to each in-world speaker marker. Off by default: " +
            "realtime lights are the expensive part of the effect. Forced on anyway " +
            "if no usable shader was found, since the light is then the only visual.");
        MapPins = Config.Bind("Voice", "MapPins", false,
            "Pin every tracked player onto the in-game paper map, lighting up while " +
            "they talk. Needs the map prop loaded; the world-to-map transform is " +
            "fitted from GourdMap's own landmark anchors.");

        // MonoBehaviours must be registered with the IL2CPP domain before Unity
        // will accept them via AddComponent.
        ClassInjector.RegisterTypeInIl2Cpp<DevOverlay>();

        var host = new GameObject("BigWalk.DevMenu");
        host.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(host);
        host.AddComponent<DevOverlay>();

        Log.LogInfo($"Loaded. {MenuKey.Value} = menu, {FreeCamKey.Value} = free cam.");
    }
}
