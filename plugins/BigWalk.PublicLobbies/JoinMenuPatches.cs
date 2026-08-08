using HarmonyLib;

namespace BigWalk.PublicLobbies;

/// <summary>
/// Hooks the join menu's own lifecycle rather than polling for it, so the section is
/// built exactly once per menu and refreshed each time the player opens it.
/// </summary>
[HarmonyPatch(typeof(JoinMenu))]
internal static class JoinMenuPatches
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(JoinMenu.OnEnable))]
    private static void OnEnable(JoinMenu __instance)
    {
        if (!Plugin.Instance.EnableNativeSection.Value) return;
        Plugin.Browser?.Attach(__instance);
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(JoinMenu.OnDisable))]
    private static void OnDisable() =>
        Plugin.Browser?.Detach();
}
