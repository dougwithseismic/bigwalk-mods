using UnityEngine;

namespace BigWalk.VoiceSettings;

internal static class PlayerVoiceInfo
{
    public static string DisplayName(PlayerVoicePlaybackControl control, int fallbackIndex = 0)
    {
        try
        {
            var networking = control?.playerCharacter?.playerNetworking;
            if (networking != null)
            {
                string name = FirstUseful(
                    networking.moderationNameSanitized,
                    networking.moderationName,
                    networking.username);
                if (name != null) return name;
            }

            string providerName = control?.XProviderIdentifier;
            if (!string.IsNullOrWhiteSpace(providerName)) return providerName;
        }
        catch
        {
            // Identity can be briefly incomplete while a network player spawns.
        }

        return fallbackIndex > 0 ? $"Player {fallbackIndex}" : "Unknown player";
    }

    public static Transform Anchor(PlayerVoicePlaybackControl control)
    {
        if (control == null) return null;
        return control.playerCharacter != null ? control.playerCharacter.transform : control.transform;
    }

    public static float DistanceFromCamera(PlayerVoicePlaybackControl control)
    {
        var camera = Camera.main;
        var anchor = Anchor(control);
        return camera != null && anchor != null
            ? Vector3.Distance(camera.transform.position, anchor.position)
            : -1f;
    }

    public static float AudibleRange(PlayerVoicePlaybackControl control)
    {
        var curve = control?.AttenuationCurve;
        if (curve == null) return 0f;

        var keys = curve.keys;
        if (keys == null || keys.Length == 0) return 0f;

        for (int i = 0; i < keys.Length; i++)
            if (Mathf.Abs(keys[i].value) < 0.0001f && keys[i].time > 0f)
                return keys[i].time;

        return keys[keys.Length - 1].time;
    }

    private static string FirstUseful(params string[] candidates)
    {
        foreach (string candidate in candidates)
            if (!string.IsNullOrWhiteSpace(candidate)) return candidate.Trim();
        return null;
    }
}
