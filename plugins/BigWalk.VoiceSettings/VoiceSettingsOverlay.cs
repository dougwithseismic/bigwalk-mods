using System;
using System.Globalization;
using BepInEx.Configuration;
using UnityEngine;

namespace BigWalk.VoiceSettings;

public class VoiceSettingsOverlay : MonoBehaviour
{
    private enum ShortcutSlot
    {
        None,
        Menu,
        AudioMode,
        Range,
        Hud,
    }

    private const float ApplyInterval = 1f;
    private static readonly float[] RangePresets = { 1f, 2f, 5f, 10f, 20f };

    private bool _visible;
    private Rect _window = new Rect(24f, 24f, 620f, 480f);
    private Vector2 _scroll;
    private CursorLockMode _previousLockState = CursorLockMode.Locked;
    private bool _previousCursorVisible;
    private float _nextApply;
    private string _rangeText;
    private string _status = "Waiting for a world…";
    private string _toastTitle;
    private string _toastDetail;
    private float _toastUntil;
    private ShortcutSlot _capturingShortcut;
    private SettingsMenu _returnToSettingsMenu;
    private Texture2D _panelTexture;
    private Texture2D _accentTexture;
    private GUIStyle _windowStyle;
    private GUIStyle _headingStyle;
    private GUIStyle _labelStyle;
    private GUIStyle _mutedLabelStyle;
    private GUIStyle _buttonStyle;
    private GUIStyle _selectedButtonStyle;
    private GUIStyle _toggleStyle;
    private GUIStyle _textFieldStyle;
    private GUIStyle _toastTitleStyle;

    public VoiceSettingsOverlay(IntPtr pointer) : base(pointer) { }

    private void Start()
    {
        ApplySettings();
    }

    private void Update()
    {
        NativeSettingsIntegration.Tick();

        // Captured keys belong to the rebinding UI and must not also fire the old
        // action on the same frame (especially the menu shortcut, which would close it).
        if (_capturingShortcut == ShortcutSlot.None)
        {
            if (_visible && Input.GetKeyDown(KeyCode.Escape))
            {
                SetVisible(false);
                return;
            }

            if (ShortcutBinding.IsDown(Plugin.Instance.MenuShortcut.Value))
                SetVisible(!_visible);

            if (ShortcutBinding.IsDown(Plugin.Instance.ToggleTwoDShortcut.Value))
                SetTwoD(!Plugin.Instance.TwoDVoice.Value, true);

            if (ShortcutBinding.IsDown(Plugin.Instance.CycleRangeShortcut.Value))
                CycleRange();

            if (ShortcutBinding.IsDown(Plugin.Instance.ToggleHudShortcut.Value))
            {
                Plugin.Instance.SpeakerIndicators.Value = !Plugin.Instance.SpeakerIndicators.Value;
                ShowToast(
                    Plugin.Instance.SpeakerIndicators.Value ? "SPEAKER HUD ON" : "SPEAKER HUD OFF",
                    Plugin.Instance.SpeakerIndicators.Value
                        ? "Named talker indicators are visible."
                        : "All speaker overlays are hidden.");
            }
        }

        if (_visible && Plugin.Instance.FreeCursor.Value)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (Time.unscaledTime < _nextApply) return;
        _nextApply = Time.unscaledTime + ApplyInterval;
        NativeSettingsIntegration.Ensure();
        ApplySettings();
    }

    private void OnGUI()
    {
        if (Plugin.Instance.SpeakerIndicators.Value && Event.current.type == EventType.Repaint)
            SpeakerIndicators.Draw();

        if (!_visible && Time.unscaledTime >= _toastUntil) return;
        EnsureStyles();

        if (Time.unscaledTime < _toastUntil)
            DrawToast();

        if (!_visible) return;
        _window = GUI.Window(0xB176, _window, (GUI.WindowFunction)DrawWindow,
            "Better Proximity Voice", _windowStyle);
    }

    private void DrawWindow(int windowId)
    {
        var previousColour = GUI.color;
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(5f, 24f, _window.width - 10f, _window.height - 29f), _panelTexture);

        HandleShortcutCapture(Event.current);

        _scroll = GUILayout.BeginScrollView(_scroll);

        GUILayout.Label("Audio mode", _headingStyle);
        GUILayout.Label("Choose positional awareness or maximum intelligibility.", _mutedLabelStyle);

        GUILayout.BeginHorizontal();
        GUIStyle spatialStyle = Plugin.Instance.TwoDVoice.Value ? _buttonStyle : _selectedButtonStyle;
        GUIStyle twoDStyle = Plugin.Instance.TwoDVoice.Value ? _selectedButtonStyle : _buttonStyle;
        if (GUILayout.Button("3D SPATIAL\nDirection + distance", spatialStyle, GUILayout.Height(52f)))
            SetTwoD(false, true);
        if (GUILayout.Button("2D EVERYWHERE\nCentred + no distance fade", twoDStyle, GUILayout.Height(52f)))
            SetTwoD(true, true);
        GUILayout.EndHorizontal();

        GUILayout.Label("Hearing range", _headingStyle);
        GUILayout.Label("Client-side only. Other players need no mod.", _mutedLabelStyle);

        float current = VoiceRangeScaler.Scale;
        float logMax = Mathf.Log(VoiceRangeScaler.MaxScale);
        float normalized = Mathf.Log(Mathf.Max(current, 1f)) / logMax;

        GUILayout.BeginHorizontal();
        GUILayout.Label("Range ×", _labelStyle, GUILayout.Width(85f));
        float moved = GUILayout.HorizontalSlider(normalized, 0f, 1f, GUILayout.Width(350f));
        GUILayout.Label(current.ToString("0.##"), _labelStyle, GUILayout.Width(65f));
        GUILayout.EndHorizontal();

        if (!Mathf.Approximately(moved, normalized))
            SetRange(Mathf.Exp(moved * logMax));

        GUILayout.BeginHorizontal();
        if (PresetButton("VANILLA", 1f)) SetRange(1f, true);
        if (PresetButton("SOCIAL", 2f)) SetRange(2f, true);
        if (PresetButton("WIDE", 5f)) SetRange(5f, true);
        if (PresetButton("FAR", 10f)) SetRange(10f, true);
        if (PresetButton("MAX", 20f)) SetRange(20f, true);
        GUILayout.EndHorizontal();

        GUILayout.Label(EffectiveRangeDescription(), _mutedLabelStyle);

        GUILayout.BeginHorizontal();
        GUILayout.Label("Exact", _labelStyle, GUILayout.Width(85f));
        _rangeText = GUILayout.TextField(
            _rangeText ?? current.ToString("0.##", CultureInfo.InvariantCulture),
            _textFieldStyle, GUILayout.Width(110f));
        if (GUILayout.Button("Apply", _buttonStyle, GUILayout.Width(80f)))
        {
            if (float.TryParse(_rangeText, NumberStyles.Float, CultureInfo.InvariantCulture, out float exact))
                SetRange(exact, true);
            else
                _status = $"'{_rangeText}' is not a number.";
        }
        GUILayout.EndHorizontal();

        GUILayout.Label("Speaker HUD", _headingStyle);
        bool indicators = GUILayout.Toggle(Plugin.Instance.SpeakerIndicators.Value,
            "Enabled", _toggleStyle);
        if (indicators != Plugin.Instance.SpeakerIndicators.Value)
            Plugin.Instance.SpeakerIndicators.Value = indicators;

        bool names = GUILayout.Toggle(Plugin.Instance.SpeakerNames.Value,
            "Names above talking players", _toggleStyle);
        if (names != Plugin.Instance.SpeakerNames.Value)
            Plugin.Instance.SpeakerNames.Value = names;

        bool roster = GUILayout.Toggle(Plugin.Instance.ActiveSpeakerRoster.Value,
            "Active-speaker roster", _toggleStyle);
        if (roster != Plugin.Instance.ActiveSpeakerRoster.Value)
            Plugin.Instance.ActiveSpeakerRoster.Value = roster;

        bool distances = GUILayout.Toggle(Plugin.Instance.SpeakerDistances.Value,
            "Show distance in metres", _toggleStyle);
        if (distances != Plugin.Instance.SpeakerDistances.Value)
            Plugin.Instance.SpeakerDistances.Value = distances;

        GUILayout.BeginHorizontal();
        GUILayout.Label("HUD size", _labelStyle, GUILayout.Width(85f));
        float hudScale = GUILayout.HorizontalSlider(Plugin.Instance.HudScale.Value, 0.75f, 1.5f,
            GUILayout.Width(350f));
        GUILayout.Label($"{hudScale:0.00}×", _labelStyle, GUILayout.Width(65f));
        GUILayout.EndHorizontal();
        if (!Mathf.Approximately(hudScale, Plugin.Instance.HudScale.Value))
            Plugin.Instance.HudScale.Value = hudScale;

        const float thresholdMin = 0.0005f;
        const float thresholdMax = 0.02f;
        float threshold = Mathf.Clamp(Plugin.Instance.TalkThreshold.Value, thresholdMin, thresholdMax);
        float sensitivity = 1f - Mathf.Log(threshold / thresholdMin) / Mathf.Log(thresholdMax / thresholdMin);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Sensitivity", _labelStyle, GUILayout.Width(85f));
        float movedSensitivity = GUILayout.HorizontalSlider(sensitivity, 0f, 1f, GUILayout.Width(350f));
        GUILayout.Label(SensitivityLabel(movedSensitivity), _mutedLabelStyle, GUILayout.Width(65f));
        GUILayout.EndHorizontal();
        if (!Mathf.Approximately(movedSensitivity, sensitivity))
            Plugin.Instance.TalkThreshold.Value = thresholdMin
                * Mathf.Pow(thresholdMax / thresholdMin, 1f - movedSensitivity);

        GUILayout.Label("Quick controls", _headingStyle);
        GUILayout.Label("Click a binding, then press the new shortcut. Escape cancels; Delete clears.",
            _mutedLabelStyle);
        DrawShortcutRow(ShortcutSlot.Menu);
        DrawShortcutRow(ShortcutSlot.AudioMode);
        DrawShortcutRow(ShortcutSlot.Range);
        DrawShortcutRow(ShortcutSlot.Hud);

        string shortcutConflict = FindShortcutConflict();
        if (shortcutConflict != null)
            GUILayout.Label($"⚠ {shortcutConflict}", _labelStyle);
        else
            GUILayout.Label("Optional shortcuts are unbound by default to avoid mod conflicts.", _mutedLabelStyle);

        GUILayout.Space(10f);
        DrawTalkers();

        GUILayout.EndScrollView();
        GUILayout.Space(5f);
        GUILayout.BeginHorizontal();
        GUILayout.Label(_status, _labelStyle);
        if (GUILayout.Button("Reset", _buttonStyle, GUILayout.Width(80f))) ResetDefaults();
        if (GUILayout.Button("Close", _buttonStyle, GUILayout.Width(80f))) SetVisible(false);
        GUILayout.EndHorizontal();

        GUI.color = previousColour;
        GUI.DragWindow();
    }

    private void DrawTalkers()
    {
        var controls = PlayerVoicePlaybackControl.controls;
        if (controls == null || controls.Count == 0)
        {
            GUILayout.Label("Players", _headingStyle);
            GUILayout.Label("No remote voice players yet.", _labelStyle);
            return;
        }

        GUILayout.Label($"Players ({controls.Count})", _headingStyle);
        int index = 0;
        foreach (var control in controls)
        {
            if (control == null) continue;
            index++;

            string playerName = PlayerVoiceInfo.DisplayName(control, index);
            bool talking = SpeakerIndicators.IsTalking(control);
            float distance = PlayerVoiceInfo.DistanceFromCamera(control);
            string distanceText = distance >= 0f ? $"{distance:0} m" : "—";

            GUILayout.BeginHorizontal();
            GUILayout.Label(talking ? "● TALKING" : "○ quiet", _labelStyle, GUILayout.Width(110f));
            GUILayout.Label(playerName, _labelStyle);
            GUILayout.Label(distanceText, _mutedLabelStyle, GUILayout.Width(70f));
            GUILayout.EndHorizontal();
        }
    }

    private void EnsureStyles()
    {
        if (_labelStyle != null) return;

        _panelTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        _panelTexture.SetPixel(0, 0, new Color(0.055f, 0.065f, 0.075f, 0.97f));
        _panelTexture.Apply();
        _panelTexture.hideFlags = HideFlags.HideAndDontSave;

        _accentTexture = new Texture2D(1, 1, TextureFormat.RGBA32, false);
        _accentTexture.SetPixel(0, 0, new Color(0.12f, 0.42f, 0.27f, 1f));
        _accentTexture.Apply();
        _accentTexture.hideFlags = HideFlags.HideAndDontSave;

        _windowStyle = new GUIStyle(GUI.skin.window)
        {
            fontSize = 17,
            fontStyle = FontStyle.Bold,
        };
        SetTextColour(_windowStyle, Color.white);

        _labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 16,
            wordWrap = true,
        };
        SetTextColour(_labelStyle, new Color(0.93f, 0.95f, 0.97f));

        _mutedLabelStyle = new GUIStyle(_labelStyle)
        {
            fontSize = 14,
        };
        SetTextColour(_mutedLabelStyle, new Color(0.67f, 0.72f, 0.76f));

        _headingStyle = new GUIStyle(_labelStyle)
        {
            fontSize = 18,
            fontStyle = FontStyle.Bold,
            margin = new RectOffset(4, 4, 8, 3),
        };

        _buttonStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize = 15,
            fixedHeight = 0f,
        };
        SetTextColour(_buttonStyle, Color.white);

        _selectedButtonStyle = new GUIStyle(_buttonStyle);
        _selectedButtonStyle.normal.background = _accentTexture;
        _selectedButtonStyle.hover.background = _accentTexture;
        _selectedButtonStyle.active.background = _accentTexture;
        SetTextColour(_selectedButtonStyle, Color.white);

        _toggleStyle = new GUIStyle(GUI.skin.toggle)
        {
            fontSize = 16,
            fixedHeight = 28f,
        };
        SetTextColour(_toggleStyle, Color.white);

        _textFieldStyle = new GUIStyle(GUI.skin.textField)
        {
            fontSize = 16,
            fixedHeight = 28f,
        };
        SetTextColour(_textFieldStyle, Color.white);

        _toastTitleStyle = new GUIStyle(_headingStyle)
        {
            alignment = TextAnchor.MiddleLeft,
            fontSize = 16,
            margin = new RectOffset(),
        };
    }

    private static void SetTextColour(GUIStyle style, Color colour)
    {
        style.normal.textColor = colour;
        style.hover.textColor = colour;
        style.active.textColor = colour;
        style.focused.textColor = colour;
        style.onNormal.textColor = colour;
        style.onHover.textColor = colour;
        style.onActive.textColor = colour;
        style.onFocused.textColor = colour;
    }

    private bool PresetButton(string label, float value)
    {
        bool selected = Mathf.Abs(VoiceRangeScaler.Scale - value) < 0.01f;
        return GUILayout.Button($"{label}\n×{value:0}", selected ? _selectedButtonStyle : _buttonStyle,
            GUILayout.Height(46f));
    }

    private static string SensitivityLabel(float value)
    {
        if (value < 0.25f) return "Low";
        if (value < 0.7f) return "Normal";
        return "High";
    }

    private static string ShortcutName(string shortcut)
    {
        return ShortcutBinding.Display(shortcut);
    }

    private void DrawShortcutRow(ShortcutSlot slot)
    {
        string label = ShortcutActionName(slot);
        ConfigEntry<string> entry = ShortcutEntry(slot);
        bool capturing = _capturingShortcut == slot;

        GUILayout.BeginHorizontal();
        GUILayout.Label(label, _labelStyle, GUILayout.Width(120f));
        if (GUILayout.Button(
                capturing ? "Press a shortcut…" : ShortcutName(entry.Value),
                capturing ? _selectedButtonStyle : _buttonStyle,
                GUILayout.Height(30f)))
        {
            _capturingShortcut = slot;
            GUI.FocusControl(null);
            _status = $"Listening for {label.ToLowerInvariant()} shortcut…";
        }

        if (GUILayout.Button("Clear", _buttonStyle, GUILayout.Width(65f), GUILayout.Height(30f)))
        {
            entry.Value = "Not bound";
            if (capturing) _capturingShortcut = ShortcutSlot.None;
            _status = $"{label} shortcut cleared.";
        }
        GUILayout.EndHorizontal();
    }

    private void HandleShortcutCapture(Event currentEvent)
    {
        if (_capturingShortcut == ShortcutSlot.None
            || currentEvent == null
            || currentEvent.type != EventType.KeyDown)
            return;

        KeyCode key = currentEvent.keyCode;
        if (IsModifier(key)) return;

        if (key == KeyCode.Escape)
        {
            _status = "Shortcut change cancelled.";
            _capturingShortcut = ShortcutSlot.None;
            currentEvent.Use();
            return;
        }

        ConfigEntry<string> entry = ShortcutEntry(_capturingShortcut);
        string action = ShortcutActionName(_capturingShortcut);

        if (key == KeyCode.Delete || key == KeyCode.Backspace)
        {
            entry.Value = "Not bound";
            _status = $"{action} shortcut cleared.";
        }
        else if (key == KeyCode.None)
        {
            return;
        }
        else
        {
            string shortcut = ShortcutFromEvent(currentEvent, key);
            entry.Value = shortcut;
            _status = $"{action}: {ShortcutName(shortcut)}";
        }

        _capturingShortcut = ShortcutSlot.None;
        currentEvent.Use();
    }

    private static string ShortcutFromEvent(Event currentEvent, KeyCode mainKey)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (currentEvent.control) parts.Add(nameof(KeyCode.LeftControl));
        if (currentEvent.shift) parts.Add(nameof(KeyCode.LeftShift));
        if (currentEvent.alt) parts.Add(nameof(KeyCode.LeftAlt));
        if (currentEvent.command) parts.Add(nameof(KeyCode.LeftCommand));
        parts.Add(mainKey.ToString());
        return string.Join(" + ", parts);
    }

    private static bool IsModifier(KeyCode key)
    {
        return key == KeyCode.LeftAlt || key == KeyCode.RightAlt
               || key == KeyCode.LeftControl || key == KeyCode.RightControl
               || key == KeyCode.LeftShift || key == KeyCode.RightShift
               || key == KeyCode.LeftCommand || key == KeyCode.RightCommand
               || key == KeyCode.LeftWindows || key == KeyCode.RightWindows;
    }

    private static ConfigEntry<string> ShortcutEntry(ShortcutSlot slot)
    {
        return slot switch
        {
            ShortcutSlot.Menu => Plugin.Instance.MenuShortcut,
            ShortcutSlot.AudioMode => Plugin.Instance.ToggleTwoDShortcut,
            ShortcutSlot.Range => Plugin.Instance.CycleRangeShortcut,
            ShortcutSlot.Hud => Plugin.Instance.ToggleHudShortcut,
            _ => throw new ArgumentOutOfRangeException(nameof(slot)),
        };
    }

    private static string ShortcutActionName(ShortcutSlot slot)
    {
        return slot switch
        {
            ShortcutSlot.Menu => "Open menu",
            ShortcutSlot.AudioMode => "2D / 3D",
            ShortcutSlot.Range => "Cycle range",
            ShortcutSlot.Hud => "Toggle HUD",
            _ => "Shortcut",
        };
    }

    private static string FindShortcutConflict()
    {
        var bindings = new[]
        {
            (Name: "Menu", Shortcut: Plugin.Instance.MenuShortcut.Value),
            (Name: "2D/3D", Shortcut: Plugin.Instance.ToggleTwoDShortcut.Value),
            (Name: "Cycle range", Shortcut: Plugin.Instance.CycleRangeShortcut.Value),
            (Name: "HUD", Shortcut: Plugin.Instance.ToggleHudShortcut.Value),
        };

        for (int left = 0; left < bindings.Length; left++)
        {
            if (!ShortcutBinding.IsBound(bindings[left].Shortcut)) continue;
            for (int right = left + 1; right < bindings.Length; right++)
            {
                if (!ShortcutBinding.IsBound(bindings[right].Shortcut)) continue;
                if (ShortcutBinding.Equivalent(bindings[left].Shortcut, bindings[right].Shortcut))
                    return $"{bindings[left].Name} and {bindings[right].Name} use the same shortcut.";
            }
        }

        foreach (var binding in bindings)
            if (!ShortcutBinding.IsValid(binding.Shortcut))
                return $"{binding.Name} has an invalid shortcut: '{binding.Shortcut}'.";

        return null;
    }

    private string EffectiveRangeDescription()
    {
        var controls = PlayerVoicePlaybackControl.controls;
        if (Plugin.Instance.TwoDVoice.Value)
            return "2D mode bypasses positional distance fade.";
        if (controls == null) return "Effective distance appears after another player joins.";

        foreach (var control in controls)
        {
            if (control == null) continue;
            float range = PlayerVoiceInfo.AudibleRange(control);
            if (range > 0f) return $"Current received-voice edge: approximately {range:0} metres.";
        }

        return "Effective distance appears after another player speaks.";
    }

    private void SetRange(float value, bool notify = false)
    {
        float clamped = Mathf.Clamp(value, 1f, VoiceRangeScaler.MaxScale);
        Plugin.Instance.RangeMultiplier.Value = clamped;
        _rangeText = clamped.ToString("0.##", CultureInfo.InvariantCulture);
        ApplySettings();
        if (notify)
            ShowToast($"VOICE RANGE  ×{clamped:0.##}", RangePresetDescription(clamped));
    }

    private void SetTwoD(bool enabled, bool notify)
    {
        Plugin.Instance.TwoDVoice.Value = enabled;
        ApplySettings();
        if (notify)
            ShowToast(
                enabled ? "2D VOICE" : "3D SPATIAL VOICE",
                enabled ? "Voices are centred with no distance fade." : "Direction and distance are restored.");
    }

    private void CycleRange()
    {
        float current = Plugin.Instance.RangeMultiplier.Value;
        float next = RangePresets[0];
        foreach (float preset in RangePresets)
        {
            if (preset > current + 0.01f)
            {
                next = preset;
                break;
            }
        }

        SetRange(next, true);
    }

    private static string RangePresetDescription(float range)
    {
        if (range <= 1.01f) return "Vanilla received-voice distance.";
        if (range <= 2.01f) return "A comfortable social boost.";
        if (range <= 5.01f) return "Wide-area conversation.";
        if (range <= 10.01f) return "Far-reaching conversation.";
        return "Maximum received-voice distance.";
    }

    private void ShowToast(string title, string detail)
    {
        _toastTitle = title;
        _toastDetail = detail;
        _toastUntil = Time.unscaledTime + 2.6f;
    }

    private void ResetDefaults()
    {
        Plugin.Instance.RangeMultiplier.Value = 1f;
        Plugin.Instance.TwoDVoice.Value = false;
        Plugin.Instance.SpeakerIndicators.Value = true;
        Plugin.Instance.SpeakerNames.Value = true;
        Plugin.Instance.ActiveSpeakerRoster.Value = true;
        Plugin.Instance.SpeakerDistances.Value = true;
        Plugin.Instance.HudScale.Value = 1f;
        Plugin.Instance.TalkThreshold.Value = 0.004f;
        _rangeText = "1";
        ApplySettings();
        ShowToast("VOICE SETTINGS RESET", "Vanilla range, 3D audio, and the full speaker HUD.");
    }

    private void DrawToast()
    {
        float width = Mathf.Min(460f, Screen.width - 32f);
        float height = 72f;
        float x = (Screen.width - width) * 0.5f;
        float y = 26f;

        GUI.color = new Color(0.025f, 0.03f, 0.035f, 0.94f);
        GUI.DrawTexture(new Rect(x, y, width, height), _panelTexture);
        GUI.color = Color.white;
        GUI.DrawTexture(new Rect(x, y, 5f, height), _accentTexture);
        GUI.color = Color.white;
        GUI.Label(new Rect(x + 18f, y + 8f, width - 30f, 26f), _toastTitle, _toastTitleStyle);
        GUI.Label(new Rect(x + 18f, y + 34f, width - 30f, 25f), _toastDetail, _mutedLabelStyle);
    }

    private void ApplySettings()
    {
        try
        {
            float scale = Mathf.Clamp(Plugin.Instance.RangeMultiplier.Value, 1f, VoiceRangeScaler.MaxScale);
            VoiceRangeScaler.Scale = scale;
            VoiceRangeScaler.Apply();

            var controls = PlayerVoicePlaybackControl.controls;
            if (controls != null)
                foreach (var control in controls)
                    if (control != null) control.TwoDMode = Plugin.Instance.TwoDVoice.Value;

            _status = VoiceRangeScaler.LastApplied == 0
                ? "Waiting for remote players…"
                : $"Applied ×{scale:0.##} to {VoiceRangeScaler.LastApplied} player(s).";
        }
        catch (Exception exception)
        {
            _status = "Could not apply voice settings; check the BepInEx log.";
            Plugin.Trace.LogError($"Applying voice settings failed: {exception}");
        }
    }

    private void SetVisible(bool visible)
    {
        if (_visible == visible) return;
        _visible = visible;

        if (!Plugin.Instance.FreeCursor.Value) return;

        if (visible)
        {
            _previousLockState = Cursor.lockState;
            _previousCursorVisible = Cursor.visible;
        }
        else
        {
            Cursor.lockState = _previousLockState;
            Cursor.visible = _previousCursorVisible;

            SettingsMenu returnMenu = _returnToSettingsMenu;
            _returnToSettingsMenu = null;
            if (returnMenu != null)
            {
                returnMenu.gameObject.SetActive(true);
                returnMenu.SetToAudio();
                NativeSettingsIntegration.Refresh();
            }
        }
    }

    internal void ApplyImmediately()
    {
        ApplySettings();
    }

    internal void OpenFromNativeSettings(SettingsMenu settingsMenu)
    {
        _returnToSettingsMenu = settingsMenu;
        settingsMenu?.gameObject.SetActive(false);
        SetVisible(true);
    }
}
