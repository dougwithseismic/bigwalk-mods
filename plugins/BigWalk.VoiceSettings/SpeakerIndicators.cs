using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigWalk.VoiceSettings;

/// <summary>
/// Draws named, distance-coloured world markers and an active-speaker roster.
/// Everything is immediate-mode and allocation-light outside active speech.
/// </summary>
internal static class SpeakerIndicators
{
    private sealed class ActiveSpeaker
    {
        public string Name;
        public float Level;
        public float Distance;
    }

    private const float BarGain = 12f;
    private const float HeadHeight = 2.4f;

    private static readonly List<ActiveSpeaker> ActiveSpeakers = new List<ActiveSpeaker>();
    private static Texture2D _pixel;
    private static GUIStyle _nameStyle;
    private static GUIStyle _rosterNameStyle;
    private static GUIStyle _rosterHeadingStyle;
    private static float _styledScale = -1f;

    private static Texture2D Pixel
    {
        get
        {
            if (_pixel != null) return _pixel;
            _pixel = new Texture2D(1, 1, TextureFormat.RGBA32, false);
            _pixel.SetPixel(0, 0, Color.white);
            _pixel.Apply();
            _pixel.hideFlags = HideFlags.HideAndDontSave;
            return _pixel;
        }
    }

    public static bool IsTalking(PlayerVoicePlaybackControl control)
    {
        if (control == null) return false;
        float threshold = Mathf.Clamp(Plugin.Instance.TalkThreshold.Value, 0.0001f, 0.1f);
        return control.SmoothedARV >= threshold;
    }

    public static void Draw()
    {
        var previousColour = GUI.color;
        try
        {
            var camera = Camera.main;
            var controls = PlayerVoicePlaybackControl.controls;
            if (camera == null || controls == null) return;

            float scale = Mathf.Clamp(Plugin.Instance.HudScale.Value, 0.75f, 1.5f);
            EnsureStyles(scale);
            ActiveSpeakers.Clear();

            var eye = camera.transform.position;
            int fallbackIndex = 0;

            foreach (var control in controls)
            {
                if (control == null) continue;
                fallbackIndex++;
                if (!IsTalking(control)) continue;

                Transform anchor = PlayerVoiceInfo.Anchor(control);
                if (anchor == null) continue;

                float amplitude = control.SmoothedARV;
                float level = Mathf.Clamp01(amplitude * BarGain);
                float distance = Vector3.Distance(eye, anchor.position);
                string name = PlayerVoiceInfo.DisplayName(control, fallbackIndex);
                var world = anchor.position + Vector3.up * HeadHeight;
                float audibleRange = PlayerVoiceInfo.AudibleRange(control);
                float distanceRatio = audibleRange > 0f
                    ? Mathf.Clamp01(distance / audibleRange)
                    : 0f;

                DrawMarker(camera, world, distanceRatio, level, name, distance, scale);
                ActiveSpeakers.Add(new ActiveSpeaker
                {
                    Name = name,
                    Level = level,
                    Distance = distance,
                });
            }

            if (Plugin.Instance.ActiveSpeakerRoster.Value && ActiveSpeakers.Count > 0)
            {
                ActiveSpeakers.Sort((left, right) => right.Level.CompareTo(left.Level));
                DrawRoster(scale);
            }
        }
        catch (Exception exception)
        {
            Plugin.Trace.LogError($"Speaker indicators failed: {exception}");
        }
        finally
        {
            GUI.color = previousColour;
        }
    }

    private static void DrawMarker(
        Camera camera,
        Vector3 world,
        float distanceRatio,
        float level,
        string name,
        float distance,
        float scale)
    {
        var screen = camera.WorldToScreenPoint(world);
        bool behind = screen.z <= 0f;
        float edgeMargin = 32f * scale;
        float x = behind ? Screen.width - screen.x : screen.x;
        float y = behind ? 0f : Screen.height - screen.y;

        bool offScreen = behind
                         || x < edgeMargin || x > Screen.width - edgeMargin
                         || y < edgeMargin || y > Screen.height - edgeMargin;

        x = Mathf.Clamp(x, edgeMargin, Screen.width - edgeMargin);
        y = Mathf.Clamp(y, edgeMargin, Screen.height - edgeMargin);

        Color colour = DistanceColour(distanceRatio);
        colour.a = Mathf.Lerp(1f, 0.42f, distanceRatio);

        DrawSpeaker(x, y, colour, level, offScreen, scale);
        if (Plugin.Instance.SpeakerNames.Value)
            DrawName(x, y, name, distance, colour, scale);
    }

    private static void DrawSpeaker(
        float centreX,
        float centreY,
        Color colour,
        float level,
        bool offScreen,
        float scale)
    {
        GUI.color = new Color(0.025f, 0.03f, 0.035f, colour.a * 0.92f);
        Fill(centreX - 14f * scale, centreY - 12f * scale, 31f * scale, 24f * scale);

        GUI.color = colour;
        Fill(centreX - 10f * scale, centreY - 3f * scale, 4f * scale, 6f * scale);
        Fill(centreX - 6f * scale, centreY - 5f * scale, 2f * scale, 10f * scale);
        Fill(centreX - 4f * scale, centreY - 7f * scale, 2f * scale, 14f * scale);
        Fill(centreX - 2f * scale, centreY - 9f * scale, 2f * scale, 18f * scale);

        for (int i = 0; i < 3; i++)
        {
            var wave = colour;
            if (level < 0.15f + i * 0.28f) wave.a *= 0.18f;
            GUI.color = wave;

            float height = (6f + i * 4f) * scale;
            Fill(centreX + (3f + i * 4f) * scale, centreY - height * 0.5f, 2f * scale, height);
        }

        if (!offScreen) return;
        GUI.color = colour;
        Fill(centreX - 8f * scale, centreY + 15f * scale, 16f * scale, 2f * scale);
    }

    private static void DrawName(
        float markerX,
        float markerY,
        string name,
        float distance,
        Color colour,
        float scale)
    {
        string text = Plugin.Instance.SpeakerDistances.Value
            ? $"{name}  ·  {distance:0} m"
            : name;
        var content = new GUIContent(text);
        Vector2 measured = _nameStyle.CalcSize(content);
        float width = Mathf.Min(measured.x + 18f * scale, 260f * scale);
        float height = 27f * scale;
        float x = markerX + 21f * scale;
        if (x + width > Screen.width - 12f * scale)
            x = markerX - width - 21f * scale;
        float y = markerY - height * 0.5f;

        GUI.color = new Color(0.025f, 0.03f, 0.035f, 0.88f);
        Fill(x, y, width, height);
        GUI.color = colour;
        Fill(x, y, 3f * scale, height);
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 10f * scale, y, width - 14f * scale, height), content, _nameStyle);
    }

    private static void DrawRoster(float scale)
    {
        int rows = Mathf.Min(ActiveSpeakers.Count, 6);
        float width = 270f * scale;
        float headingHeight = 29f * scale;
        float rowHeight = 34f * scale;
        float height = headingHeight + rows * rowHeight + 8f * scale;
        float x = Screen.width - width - 24f * scale;
        float y = Screen.height - height - 24f * scale;

        GUI.color = new Color(0.025f, 0.03f, 0.035f, 0.88f);
        Fill(x, y, width, height);
        GUI.color = new Color(0.35f, 0.9f, 0.58f, 1f);
        Fill(x, y, 4f * scale, height);
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 13f * scale, y, width - 20f * scale, headingHeight),
            "NOW SPEAKING", _rosterHeadingStyle);

        for (int i = 0; i < rows; i++)
        {
            ActiveSpeaker speaker = ActiveSpeakers[i];
            float rowY = y + headingHeight + i * rowHeight;
            string distance = Plugin.Instance.SpeakerDistances.Value ? $"{speaker.Distance:0} m" : "";

            GUI.color = Color.white;
            GUI.Label(new Rect(x + 13f * scale, rowY, width - 75f * scale, 22f * scale),
                speaker.Name, _rosterNameStyle);
            GUI.Label(new Rect(x + width - 65f * scale, rowY, 52f * scale, 22f * scale),
                distance, _rosterNameStyle);

            GUI.color = new Color(1f, 1f, 1f, 0.15f);
            Fill(x + 13f * scale, rowY + 23f * scale, width - 26f * scale, 3f * scale);
            GUI.color = DistanceColour(1f - speaker.Level);
            Fill(x + 13f * scale, rowY + 23f * scale,
                (width - 26f * scale) * Mathf.Max(0.05f, speaker.Level), 3f * scale);
        }
    }

    private static Color DistanceColour(float ratio)
    {
        ratio = Mathf.Clamp01(ratio);
        return ratio < 0.5f
            ? Color.Lerp(new Color(0.30f, 1f, 0.35f), new Color(1f, 0.85f, 0.20f), ratio * 2f)
            : Color.Lerp(new Color(1f, 0.85f, 0.20f), new Color(1f, 0.25f, 0.20f), (ratio - 0.5f) * 2f);
    }

    private static void EnsureStyles(float scale)
    {
        if (_nameStyle != null && Mathf.Approximately(scale, _styledScale)) return;
        _styledScale = scale;

        _nameStyle = NewLabel(Mathf.RoundToInt(14f * scale), FontStyle.Bold, TextAnchor.MiddleLeft);
        _rosterNameStyle = NewLabel(Mathf.RoundToInt(13f * scale), FontStyle.Normal, TextAnchor.UpperLeft);
        _rosterHeadingStyle = NewLabel(Mathf.RoundToInt(12f * scale), FontStyle.Bold, TextAnchor.MiddleLeft);
    }

    private static GUIStyle NewLabel(int fontSize, FontStyle fontStyle, TextAnchor alignment)
    {
        var style = new GUIStyle(GUI.skin.label)
        {
            fontSize = fontSize,
            fontStyle = fontStyle,
            alignment = alignment,
            clipping = TextClipping.Clip,
        };
        style.normal.textColor = Color.white;
        return style;
    }

    private static void Fill(float x, float y, float width, float height)
    {
        GUI.DrawTexture(new Rect(x, y, width, height), Pixel);
    }
}
