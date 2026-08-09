using System;
using UnityEngine;

namespace BigWalk.DevMenu;

/// <summary>
/// Screen-space "who is talking" markers, drawn over each speaking player's world
/// position (and clamped to the screen edge for anyone off-screen or behind you).
///
/// Talking detection needs no patching: PlayerVoicePlaybackControl publishes a live
/// SmoothedARV (average rectified value) per remote player, which is exactly the
/// amplitude the game itself uses to drive mouth animation.
///
/// Colour encodes distance - green up close fading through amber to red at the edge
/// of audibility - and alpha fades with the same ratio, so a wall of distant speakers
/// stays readable instead of covering the screen. The "edge of audibility" is taken
/// from the attenuation curve's own last keyframe, so it tracks VoiceRangeScaler
/// automatically rather than needing its own tuning number.
/// </summary>
internal static class SpeakerIndicators
{
    /// <summary>Below this ARV a player counts as silent.</summary>
    private const float TalkThreshold = 0.004f;

    /// <summary>ARV is small; scale it up before treating it as a 0..1 bar level.</summary>
    private const float BarGain = 12f;

    private const float HeadHeight = 2.4f;
    private const float EdgeMargin = 28f;

    private static Texture2D _px;

    private static Texture2D Px
    {
        get
        {
            if (_px != null) return _px;
            _px = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _px.SetPixel(0, 0, Color.white);
            _px.Apply();
            _px.hideFlags = HideFlags.HideAndDontSave;
            return _px;
        }
    }

    /// <summary>Call from OnGUI (Repaint only). Never throws out to the caller.</summary>
    public static void Draw()
    {
        try
        {
            var cam = Camera.main;
            if (cam == null) return;

            var controls = PlayerVoicePlaybackControl.controls;
            if (controls == null) return;

            var eye = cam.transform.position;
            var prior = GUI.color;

            foreach (var c in controls)
            {
                if (c == null) continue;

                var character = c.playerCharacter;
                if (character == null) continue;

                float arv = c.SmoothedARV;
                if (arv < TalkThreshold) continue;

                var world = character.transform.position + Vector3.up * HeadHeight;
                float distance = Vector3.Distance(eye, world);
                float far = AudibleRange(c);
                float t = far > 0f ? Mathf.Clamp01(distance / far) : 0f;

                DrawMarker(cam, world, t, Mathf.Clamp01(arv * BarGain));
            }

            GUI.color = prior;
        }
        catch (Exception e)
        {
            Plugin.Trace.LogError($"Speaker indicators failed: {e}");
        }
    }

    /// <summary>
    /// The distance at which this voice goes silent, read off the live attenuation
    /// curve. Prefer the first keyframe that hits zero; fall back to the last key.
    /// </summary>
    private static float AudibleRange(PlayerVoicePlaybackControl c)
    {
        var curve = c.AttenuationCurve;
        if (curve == null) return 0f;

        var keys = curve.keys;
        if (keys == null || keys.Length == 0) return 0f;

        for (int i = 0; i < keys.Length; i++)
            if (Mathf.Abs(keys[i].value) < 0.0001f && keys[i].time > 0f)
                return keys[i].time;

        return keys[keys.Length - 1].time;
    }

    private static void DrawMarker(Camera cam, Vector3 world, float t, float level)
    {
        var sp = cam.WorldToScreenPoint(world);
        bool behind = sp.z <= 0f;

        // Behind the camera the projection mirrors through the origin, so flip it
        // back before clamping or the arrow points to the wrong side.
        float x = behind ? Screen.width - sp.x : sp.x;
        float y = behind ? 0f : Screen.height - sp.y;

        bool offScreen = behind
                         || x < EdgeMargin || x > Screen.width - EdgeMargin
                         || y < EdgeMargin || y > Screen.height - EdgeMargin;

        x = Mathf.Clamp(x, EdgeMargin, Screen.width - EdgeMargin);
        y = Mathf.Clamp(y, EdgeMargin, Screen.height - EdgeMargin);

        // Green near, amber mid, red at the edge of audibility. Two-stage lerp
        // keeps the midpoint yellow instead of the muddy brown a direct
        // green->red lerp passes through.
        var colour = t < 0.5f
            ? Color.Lerp(new Color(0.30f, 1f, 0.35f), new Color(1f, 0.85f, 0.20f), t * 2f)
            : Color.Lerp(new Color(1f, 0.85f, 0.20f), new Color(1f, 0.25f, 0.20f), (t - 0.5f) * 2f);

        colour.a = Mathf.Lerp(1f, 0.25f, t);

        DrawSpeaker(x, y, colour, level, offScreen);
    }

    /// <summary>
    /// A speaker glyph built from filled rects - IMGUI gives us no polygon fill, and
    /// a staircase reads correctly as a cone at this size. Sound waves double as the
    /// volume meter: how many light up follows the amplitude.
    /// </summary>
    private static void DrawSpeaker(float cx, float cy, Color colour, float level, bool offScreen)
    {
        GUI.color = new Color(0f, 0f, 0f, colour.a * 0.55f);
        Fill(cx - 11f, cy - 10f, 26f, 20f);

        GUI.color = colour;

        // Body.
        Fill(cx - 8f, cy - 3f, 4f, 6f);

        // Cone, widening left to right.
        Fill(cx - 4f, cy - 5f, 2f, 10f);
        Fill(cx - 2f, cy - 7f, 2f, 14f);
        Fill(cx, cy - 9f, 2f, 18f);

        // Three waves; each needs progressively more amplitude to light up.
        for (int i = 0; i < 3; i++)
        {
            float need = 0.15f + i * 0.28f;
            var wave = colour;
            if (level < need) wave.a *= 0.18f;
            GUI.color = wave;

            float h = 6f + i * 4f;
            Fill(cx + 4f + i * 3f, cy - h * 0.5f, 1.5f, h);
        }

        if (!offScreen) return;

        // Off-screen: a tick under the glyph so it reads as "over there", not "here".
        GUI.color = colour;
        Fill(cx - 6f, cy + 12f, 12f, 2f);
    }

    private static void Fill(float x, float y, float w, float h)
    {
        GUI.DrawTexture(new Rect(x, y, w, h), Px);
    }
}
