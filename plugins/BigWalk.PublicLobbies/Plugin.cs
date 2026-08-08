using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace BigWalk.PublicLobbies;

/// <summary>
/// Big Walk's join menu only ever lists friends' games, but the lobby search that
/// would back a public browser already ships in retail: EOSLobbyManager exposes
/// FindPublicLobbies() and the raw FindLobbies(maxResults, feedSearch). This plugin
/// surfaces those as a native section in the existing JoinMenu.
///
/// Note the lobby stack lives in Mirror.Transports.dll, not Assembly-CSharp - the
/// game vendored the EOS transport sample and grew EOSLobbyManager inside it.
/// </summary>
[BepInPlugin(Guid, "Better Public Lobbies", "1.0.0")]
public class Plugin : BasePlugin
{
    public const string Guid = "com.bigwalk.publiclobbies";

    internal static Plugin Instance { get; private set; }
    internal static BepInEx.Logging.ManualLogSource Trace;
    internal static NativeLobbyBrowser Browser { get; private set; }

    internal ConfigEntry<KeyCode> ProbeKey;
    internal ConfigEntry<KeyCode> DumpUiKey;
    internal ConfigEntry<KeyCode> WideProbeKey;
    internal ConfigEntry<uint> MaxResults;
    internal ConfigEntry<bool> CompactRows;
    internal ConfigEntry<float> RowScale;
    internal ConfigEntry<float> ScrollSpeed;
    internal ConfigEntry<KeyCode> BrowserKey;
    internal ConfigEntry<bool> EnableNativeSection;
    internal ConfigEntry<bool> EnableOverlay;

    public override void Load()
    {
        Instance = this;
        Trace = Log;

        ProbeKey = Config.Bind("Diagnostics", "ProbeKey", KeyCode.F7,
            "Runs a public lobby search and dumps every field of every result to the log. " +
            "This is the reconnaissance pass that the UI is built from.");
        DumpUiKey = Config.Bind("Diagnostics", "DumpUiKey", KeyCode.F8,
            "Dumps the JoinMenu hierarchy and the friend-card prefab tree to the log, so " +
            "public lobby cards can be cloned from the game's own widgets.");
        WideProbeKey = Config.Bind("Diagnostics", "WideProbeKey", KeyCode.F9,
            "Runs the search at MaxResults through the game's own public filter and " +
            "reports the deduped world list - the shape the real UI will show.");
        MaxResults = Config.Bind("Search", "MaxResults", 200u,
            "Upper bound passed to the EOS lobby search. 200 is EOS_LOBBY_MAX_SEARCH_RESULTS; " +
            "the SDK rejects anything larger. The stock browser asks for far fewer.");

        Browser = new NativeLobbyBrowser();

        new HarmonyLib.Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

        CompactRows = Config.Bind("Appearance", "CompactRows", true,
            "Scales public lobby rows down from the friends-list card size. The friends " +
            "list shows a few cards; a public list is dozens, and at full size only two " +
            "or three fit on screen.");
        RowScale = Config.Bind("Appearance", "RowScale", 0.5f,
            "How much to scale a compact row (0.3 - 1.0). Applies to every text element " +
            "on the row and to the row height.");
        ScrollSpeed = Config.Bind("Appearance", "ScrollSpeed", 120f,
            "Pixels of list movement per mouse wheel notch.");

        BrowserKey = Config.Bind("Keys", "BrowserKey", KeyCode.F6,
            "Opens/closes the public lobby browser.");
        EnableNativeSection = Config.Bind("Appearance", "EnableNativeSection", true,
            "Renders the public lobby list inside the game's own join menu, styled like " +
            "the friends list.");
        EnableOverlay = Config.Bind("Appearance", "EnableOverlay", false,
            "Also registers a standalone IMGUI browser on BrowserKey. Off by default: it " +
            "has crashed the game (0xC0000005) and is not yet trusted.");

        ClassInjector.RegisterTypeInIl2Cpp<LobbyProbe>();

        var host = new GameObject("BigWalk.PublicLobbies");
        host.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(host);
        host.AddComponent<LobbyProbe>();

        if (EnableOverlay.Value)
        {
            ClassInjector.RegisterTypeInIl2Cpp<LobbyOverlay>();
            host.AddComponent<LobbyOverlay>();
        }

        Log.LogInfo($"Loaded. {ProbeKey.Value} = probe lobbies, {DumpUiKey.Value} = dump join menu UI, " +
                    $"{WideProbeKey.Value} = wide deduped search.");
    }
}
