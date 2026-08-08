using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace BigWalk.PublicLobbies;

/// <summary>
/// Big Walk's join menu only ever lists friends' games and a 6-digit code, but the
/// lobby search that would back a public browser already ships in retail:
/// EOSLobbyManager exposes FindPublicLobbies() and the raw FindLobbies(). This plugin
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

    internal ConfigEntry<uint> MaxResults;
    internal ConfigEntry<int> SearchRounds;
    internal ConfigEntry<bool> CompactRows;
    internal ConfigEntry<float> RowScale;
    internal ConfigEntry<float> ScrollSpeed;

    public override void Load()
    {
        Instance = this;
        Trace = Log;

        MaxResults = Config.Bind("Search", "MaxResults", 200u,
            "Upper bound passed to each EOS lobby search. 200 is EOS_LOBBY_MAX_SEARCH_RESULTS; " +
            "the SDK rejects anything larger.");
        SearchRounds = Config.Bind("Search", "SearchRounds", 12,
            "How many searches a refresh runs. EOS returns a varying subset of matching " +
            "lobbies per search rather than a stable page, so one query never sees every " +
            "room; the rounds are unioned together. Higher finds more but takes longer, " +
            "and past roughly 30 EOS starts rate-limiting and returns nothing new.");

        CompactRows = Config.Bind("Appearance", "CompactRows", true,
            "Scales public lobby rows down from the friends-list card size. The friends " +
            "list shows a few cards; a public list is dozens, and at full size only two " +
            "or three fit on screen.");
        RowScale = Config.Bind("Appearance", "RowScale", 0.5f,
            "How much to scale a compact row (0.3 - 1.0). Applies to every text element " +
            "on the row and to the row height.");
        ScrollSpeed = Config.Bind("Appearance", "ScrollSpeed", 120f,
            "Pixels of list movement per mouse wheel notch.");

        Browser = new NativeLobbyBrowser();

        new HarmonyLib.Harmony(Guid).PatchAll(typeof(Plugin).Assembly);

        // MonoBehaviours must be registered with the IL2CPP domain before Unity will
        // accept them via AddComponent.
        ClassInjector.RegisterTypeInIl2Cpp<BrowserHost>();

        var host = new GameObject("BigWalk.PublicLobbies");
        host.hideFlags = HideFlags.HideAndDontSave;
        Object.DontDestroyOnLoad(host);
        host.AddComponent<BrowserHost>();

        Log.LogInfo("Better Public Lobbies loaded.");
    }
}
