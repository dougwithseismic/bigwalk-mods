using BepInEx;
using BepInEx.Configuration;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;

namespace BigWalk.SkipIntro;

/// <summary>
/// Startup gates you clear identically every launch: the splash screen and the
/// microphone check. Both are dismissed through the game's own ActionContinue
/// path rather than by disabling the menu objects, so whatever bookkeeping those
/// methods do still happens.
/// </summary>
[BepInPlugin(Guid, "Big Walk — Skip Intro", "1.0.0")]
public class Plugin : BasePlugin
{
    public const string Guid = "com.bigwalk.skipintro";

    internal static ConfigEntry<bool> SkipSplash;
    internal static ConfigEntry<bool> SkipMicCheck;
    internal static BepInEx.Logging.ManualLogSource Trace;

    public override void Load()
    {
        Trace = Log;

        SkipSplash = Config.Bind("General", "SkipSplash", true,
            "Skip the splash screen on startup.");
        SkipMicCheck = Config.Bind("General", "SkipMicCheck", true,
            "Skip the microphone check screen on startup.");

        var harmony = new Harmony(Guid);

        if (SkipSplash.Value)
        {
            harmony.PatchAll(typeof(SplashMenuPatch));
            Log.LogInfo("Patched SplashMenu.");
        }

        if (SkipMicCheck.Value)
        {
            harmony.PatchAll(typeof(MicCheckMenuPatch));
            Log.LogInfo("Patched MicCheckMenu.");
        }

        if (!SkipSplash.Value && !SkipMicCheck.Value)
            Log.LogInfo("Both skips disabled by config; nothing patched.");
    }
}

// Postfix rather than prefix throughout: let each menu finish its own OnEnable
// (device enumeration, button wiring, fade setup) before we press continue for
// the user. Skipping OnEnable outright leaves those references unassigned.

[HarmonyPatch(typeof(SplashMenu), nameof(SplashMenu.OnEnable))]
internal static class SplashMenuPatch
{
    private static void Postfix(SplashMenu __instance)
    {
        try
        {
            __instance.ActionContinue();
            Plugin.Trace.LogInfo("Splash auto-continued.");
        }
        catch (System.Exception e)
        {
            // Never let a skip brick startup - a visible splash beats no title screen.
            Plugin.Trace.LogError($"Failed to skip splash: {e}");
        }
    }
}

[HarmonyPatch(typeof(MicCheckMenu), nameof(MicCheckMenu.OnEnable))]
internal static class MicCheckMenuPatch
{
    private static void Postfix(MicCheckMenu __instance)
    {
        try
        {
            __instance.ActionContinue();
            Plugin.Trace.LogInfo("Mic check auto-continued.");
        }
        catch (System.Exception e)
        {
            Plugin.Trace.LogError($"Failed to skip mic check: {e}");
        }
    }
}
