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
[BepInPlugin(Guid, "Big Walk — Dev Menu", "0.2.0")]
public class Plugin : BasePlugin
{
    public const string Guid = "com.bigwalk.devmenu";

    internal static Plugin Instance { get; private set; }
    internal static BepInEx.Logging.ManualLogSource Trace;

    internal ConfigEntry<KeyCode> MenuKey;
    internal ConfigEntry<KeyCode> FreeCamKey;
    internal ConfigEntry<bool> AllowWorldCheats;

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
