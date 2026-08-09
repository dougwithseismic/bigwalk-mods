using System;
using System.Collections.Generic;
using System.Text;
using Dissonance;
using Dissonance.Integrations.MirrorIgnorance;
using UnityEngine;

namespace BigWalk.DevMenu;

/// <summary>
/// IMGUI overlay standing in for the dev menu that was compiled out of the retail
/// build. Drives the cheat components that survived the strip, exposes the live
/// player tuning values, and reports the proximity-voice state we need in order to
/// design a chat-range mod.
/// </summary>
public class DevOverlay : MonoBehaviour
{
    public DevOverlay(IntPtr ptr) : base(ptr) { }

    private enum Tab { Player, Voice, Proximity, Camera, World }

    private bool _visible;
    private CursorLockMode _priorLockState = CursorLockMode.Locked;
    private bool _priorCursorVisible;
    private Rect _window = new Rect(24f, 24f, 520f, 620f);
    private Vector2 _scroll;
    private Tab _tab = Tab.Player;
    private string _status = "";
    private string _error;

    // Refreshing FindObjects every frame is wasteful and stutters; once a second
    // is plenty for a diagnostic readout.
    private const float RefreshInterval = 1f;
    private float _nextRefresh;
    private float _nextScaleTick;
    private string _rangeText;
    private bool _markersWereOn;
    private bool _pinsWereOn;

    private VoiceProximityBroadcastTrigger _broadcast;
    private VoiceProximityReceiptTrigger _receipt;
    private MirrorIgnorancePlayer[] _players = Array.Empty<MirrorIgnorancePlayer>();
    private CameraCheatMover _camMover;
    private bool _freeCam;
    private string _trainDistance = "0";

    // PlayerTunings is a plain serializable class on the character, so edits stick
    // for the lifetime of that character and nothing restores them. Snapshot the
    // originals on first sight so Reset always has somewhere to go back to.
    private PlayerTunings _tunings;
    private readonly Dictionary<string, float> _tuningDefaults = new Dictionary<string, float>();

    private void Update()
    {
        if (Input.GetKeyDown(Plugin.Instance.MenuKey.Value))
            SetVisible(!_visible);

        if (Input.GetKeyDown(Plugin.Instance.FreeCamKey.Value))
            ToggleFreeCam();

        if (Input.GetKeyDown(Plugin.Instance.SpeakerIconsKey.Value))
            Plugin.Instance.SpeakerIcons.Value = !Plugin.Instance.SpeakerIcons.Value;

        // The game re-locks the cursor every frame while you're playing, so a
        // one-shot unlock on open gets immediately undone. Reassert it.
        if (_visible && Plugin.Instance.FreeCursor.Value)
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        if (_visible && Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
        }

        // In-world markers have to track heads every frame, so they run here rather
        // than on the once-a-second diagnostic budget. Toggling off tears them down
        // instead of leaving orphaned GameObjects in the scene.
        if (Plugin.Instance.SpeakerMarkers3D.Value || SpeakerMarkers.TestActive) SpeakerMarkers.Tick();
        else if (SpeakerMarkers.Active > 0 || _markersWereOn) { SpeakerMarkers.Clear(); }
        _markersWereOn = Plugin.Instance.SpeakerMarkers3D.Value;

        if (Plugin.Instance.MapPins.Value) MapPins.Tick();
        else if (_pinsWereOn) { MapPins.Clear(); }
        _pinsWereOn = Plugin.Instance.MapPins.Value;

        // Voice controls spawn as players join, and a fresh one starts on vanilla
        // curves - so the scale has to be reasserted whether the menu is open or
        // not. Runs on the same once-a-second budget.
        if (VoiceRangeScaler.Scale > 1f && Time.unscaledTime >= _nextScaleTick)
        {
            _nextScaleTick = Time.unscaledTime + RefreshInterval;
            try { VoiceRangeScaler.Apply(); }
            catch (Exception e) { Plugin.Trace.LogError($"Voice range apply failed: {e}"); }
        }
    }

    /// <summary>
    /// Opens/closes the overlay, taking the cursor with it. The previous lock state
    /// is captured on open and put back on close, so we hand control back to whatever
    /// the game was doing rather than guessing it wanted the cursor locked.
    /// </summary>
    private void SetVisible(bool visible)
    {
        if (visible == _visible) return;
        _visible = visible;

        if (!Plugin.Instance.FreeCursor.Value) return;

        if (visible)
        {
            _priorLockState = Cursor.lockState;
            _priorCursorVisible = Cursor.visible;
        }
        else
        {
            Cursor.lockState = _priorLockState;
            Cursor.visible = _priorCursorVisible;
        }
    }

    private void Refresh()
    {
        try
        {
            _error = null;

            var broadcasts = UnityEngine.Object.FindObjectsByType<VoiceProximityBroadcastTrigger>(FindObjectsSortMode.None);
            _broadcast = broadcasts != null && broadcasts.Length > 0 ? broadcasts[0] : null;

            var receipts = UnityEngine.Object.FindObjectsByType<VoiceProximityReceiptTrigger>(FindObjectsSortMode.None);
            _receipt = receipts != null && receipts.Length > 0 ? receipts[0] : null;

            _players = UnityEngine.Object.FindObjectsByType<MirrorIgnorancePlayer>(FindObjectsSortMode.None)
                       ?? Array.Empty<MirrorIgnorancePlayer>();

            if (_camMover == null)
            {
                var movers = UnityEngine.Object.FindObjectsByType<CameraCheatMover>(FindObjectsSortMode.None);
                if (movers != null && movers.Length > 0) _camMover = movers[0];
            }

            var me = WorldManager.localPlayerCharacter;
            if (me != null && me.tunings != null && !ReferenceEquals(_tunings, me.tunings))
            {
                _tunings = me.tunings;
                SnapshotTunings();
            }
        }
        catch (Exception e)
        {
            // A throwing overlay is worse than a blank one - surface it in-panel.
            _error = e.ToString();
            Plugin.Trace.LogError($"Refresh failed: {e}");
        }
    }

    private void OnGUI()
    {
        // Indicators are deliberately outside the _visible gate - they are a HUD
        // element, not part of the menu, and Repaint-only because DrawTexture in
        // a layout pass just burns frames.
        if (Plugin.Instance.SpeakerIcons.Value && Event.current.type == EventType.Repaint)
            SpeakerIndicators.Draw();

        if (!_visible) return;
        _window = GUI.Window(0x8127, _window, (GUI.WindowFunction)DrawWindow, "Big Walk — Dev Menu");
    }

    private void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        foreach (Tab t in new[] { Tab.Player, Tab.Voice, Tab.Proximity, Tab.Camera, Tab.World })
            if (GUILayout.Toggle(_tab == t, t.ToString(), GUI.skin.button)) _tab = t;
        GUILayout.EndHorizontal();
        GUILayout.Space(6f);

        _scroll = GUILayout.BeginScrollView(_scroll);

        if (_error != null)
        {
            GUILayout.Label("<error>");
            GUILayout.TextArea(_error, GUILayout.Height(80f));
        }

        switch (_tab)
        {
            case Tab.Player:    DrawPlayer();    break;
            case Tab.Voice:     DrawVoice();     break;
            case Tab.Proximity: DrawProximity(); break;
            case Tab.Camera:    DrawCamera();    break;
            case Tab.World:     DrawWorld();     break;
        }

        GUILayout.EndScrollView();

        if (!string.IsNullOrEmpty(_status))
        {
            GUILayout.Space(4f);
            GUILayout.Label(_status);
        }

        GUI.DragWindow();
    }

    // ── Player ─────────────────────────────────────────────────────────────

    private void SnapshotTunings()
    {
        _tuningDefaults.Clear();
        _tuningDefaults["forwardSpeed"] = _tunings.forwardSpeed;
        _tuningDefaults["forwardSprintSpeed"] = _tunings.forwardSprintSpeed;
        _tuningDefaults["crouchForwardSpeed"] = _tunings.crouchForwardSpeed;
        _tuningDefaults["swimForwardSpeed"] = _tunings.swimForwardSpeed;
        _tuningDefaults["forwardSprintGhostSpeed"] = _tunings.forwardSprintGhostSpeed;
        _tuningDefaults["jumpForce"] = _tunings.jumpForce;
        _tuningDefaults["maxUpwardsVelocity"] = _tunings.maxUpwardsVelocity;
        _tuningDefaults["mouseLookSpeed"] = _tunings.mouseLookSpeed;
        Plugin.Trace.LogInfo("Captured baseline player tunings.");
    }

    private void DrawPlayer()
    {
        if (_tunings == null)
        {
            GUILayout.Label("No local player character yet.");
            GUILayout.Label("(These appear once you're in a world.)");
            return;
        }

        GUILayout.Label("── Movement ──");
        GUILayout.Label("Client-side physics tuning. Movement is driven locally and");
        GUILayout.Label("the result is synced, so these take effect immediately.");
        GUILayout.Space(4f);

        _tunings.forwardSpeed            = Slider("walk",        _tunings.forwardSpeed,            0f, 60f);
        _tunings.forwardSprintSpeed      = Slider("sprint",      _tunings.forwardSprintSpeed,      0f, 80f);
        _tunings.crouchForwardSpeed      = Slider("crouch",      _tunings.crouchForwardSpeed,      0f, 40f);
        _tunings.swimForwardSpeed        = Slider("swim",        _tunings.swimForwardSpeed,        0f, 40f);
        _tunings.forwardSprintGhostSpeed = Slider("ghost",       _tunings.forwardSprintGhostSpeed, 0f, 120f);
        _tunings.jumpForce               = Slider("jump",        _tunings.jumpForce,               0f, 60f);
        _tunings.maxUpwardsVelocity      = Slider("maxUpVel",    _tunings.maxUpwardsVelocity,      0f, 80f);
        _tunings.mouseLookSpeed          = Slider("mouseLook",   _tunings.mouseLookSpeed,          0f, 20f);

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Reset to defaults")) ResetTunings();
        if (GUILayout.Button("Speed x2")) Guard(() =>
        {
            _tunings.forwardSpeed *= 2f;
            _tunings.forwardSprintSpeed *= 2f;
            _status = "Doubled walk and sprint.";
        });
        GUILayout.EndHorizontal();
    }

    private void ResetTunings()
    {
        Guard(() =>
        {
            if (_tuningDefaults.Count == 0) { _status = "No baseline captured."; return; }
            _tunings.forwardSpeed = _tuningDefaults["forwardSpeed"];
            _tunings.forwardSprintSpeed = _tuningDefaults["forwardSprintSpeed"];
            _tunings.crouchForwardSpeed = _tuningDefaults["crouchForwardSpeed"];
            _tunings.swimForwardSpeed = _tuningDefaults["swimForwardSpeed"];
            _tunings.forwardSprintGhostSpeed = _tuningDefaults["forwardSprintGhostSpeed"];
            _tunings.jumpForce = _tuningDefaults["jumpForce"];
            _tunings.maxUpwardsVelocity = _tuningDefaults["maxUpwardsVelocity"];
            _tunings.mouseLookSpeed = _tuningDefaults["mouseLookSpeed"];
            _status = "Tunings restored.";
        });
    }

    private float Slider(string label, float value, float min, float max)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(label, GUILayout.Width(90f));
        float v = GUILayout.HorizontalSlider(value, min, max, GUILayout.Width(240f));
        GUILayout.Label(v.ToString("0.##"), GUILayout.Width(60f));
        GUILayout.EndHorizontal();
        return v;
    }

    // ── Voice ──────────────────────────────────────────────────────────────

    /// <summary>
    /// The game's voice attenuation is NOT plain Unity AudioSource rolloff:
    /// PlayerVoicePlaybackControl evaluates AttenuationCurve (plus filter and spatial
    /// curves) per listener, client-side. Where that curve reaches zero is the real
    /// audible ceiling, and it decides whether a host-only routing mod can work at
    /// all - the host cannot change a vanilla listener's curve.
    /// </summary>
    private void DrawVoice()
    {
        GUILayout.Label("── Global ──");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Toggle mute")) Guard(() => { WorldManager.ToggleMute(); _status = "Toggled mute."; });
        if (GUILayout.Button("Voice ON")) Guard(() => { WorldManager.ToggleVoiceChatFully(true); _status = "Voice chat enabled."; });
        if (GUILayout.Button("Voice OFF")) Guard(() => { WorldManager.ToggleVoiceChatFully(false); _status = "Voice chat disabled."; });
        GUILayout.EndHorizontal();

        if (GUILayout.Button("Toggle audibility debug GUI"))
            Guard(() =>
            {
                var dbg = PlayerVoicePlaybackControl.AudibilityDebugGUI;
                if (dbg == null) { _status = "No AudibilityDebug instance in scene."; return; }
                dbg.ToggleGUIDebug();
                _status = "Toggled the game's own audibility debug overlay.";
            });

        GUILayout.Space(8f);
        DrawRangeScaler();

        GUILayout.Space(8f);
        GUILayout.Label("── Speaker icons ──");
        bool icons = GUILayout.Toggle(Plugin.Instance.SpeakerIcons.Value,
            $"Show who's talking ({Plugin.Instance.SpeakerIconsKey.Value})");
        if (icons != Plugin.Instance.SpeakerIcons.Value)
            Plugin.Instance.SpeakerIcons.Value = icons;
        GUILayout.Label("Green = close, red = edge of audibility; fades with distance.");
        GUILayout.Label("Off-screen speakers clamp to the screen edge.");

        GUILayout.Space(6f);
        bool markers = GUILayout.Toggle(Plugin.Instance.SpeakerMarkers3D.Value, "In-world markers over heads");
        if (markers != Plugin.Instance.SpeakerMarkers3D.Value)
            Plugin.Instance.SpeakerMarkers3D.Value = markers;

        bool through = GUILayout.Toggle(Plugin.Instance.MarkersThroughWalls.Value, "  └ draw through walls");
        if (through != Plugin.Instance.MarkersThroughWalls.Value)
        {
            Plugin.Instance.MarkersThroughWalls.Value = through;
            // Depth testing is baked into the material, so existing markers have to
            // be rebuilt for the change to take.
            SpeakerMarkers.Clear();
        }

        bool lights = GUILayout.Toggle(Plugin.Instance.MarkerLights.Value, "  └ point lights (costs FPS)");
        if (lights != Plugin.Instance.MarkerLights.Value)
        {
            Plugin.Instance.MarkerLights.Value = lights;
            SpeakerMarkers.Clear();
        }

        GUILayout.Label($"    shader: {MarkerVisuals.ResolvedShader}");
        GUILayout.Label($"    live markers: {SpeakerMarkers.Active}");
        GUILayout.Label($"    built: {SpeakerMarkers.Diagnostic}");
        GUILayout.Label($"    auto-calibrated peak ARV: {SpeakerMarkers.ObservedPeak:0.######}");
        GUILayout.Label($"    path: {SpeakerMarkers.SkipReport}");

        bool force = GUILayout.Toggle(SpeakerMarkers.ForceShow, "  └ force show (ignore who's talking)");
        if (force != SpeakerMarkers.ForceShow) SpeakerMarkers.ForceShow = force;

        if (GUILayout.Button(SpeakerMarkers.TestActive
                ? "Stop marker self-test"
                : "Self-test: put a marker in front of me"))
            Guard(() =>
            {
                SpeakerMarkers.ToggleTest();
                _status = SpeakerMarkers.TestActive
                    ? "Test marker spawned 3m ahead — colour-cycling quad + light."
                    : "Test marker removed.";
            });

        GUILayout.Space(6f);
        bool pins = GUILayout.Toggle(Plugin.Instance.MapPins.Value, "Player pins on the paper map");
        if (pins != Plugin.Instance.MapPins.Value)
            Plugin.Instance.MapPins.Value = pins;

        GUILayout.Label($"    fit: {MapPins.Diagnostic}");
        GUILayout.Label($"    live pins: {MapPins.Active}");

        GUILayout.Space(8f);

        var controls = PlayerVoicePlaybackControl.controls;
        if (controls == null || controls.Count == 0)
        {
            GUILayout.Label("No PlayerVoicePlaybackControl instances.");
            GUILayout.Label("(Join a world with another player.)");
            return;
        }

        GUILayout.Label($"── Voice playback ({controls.Count}) ──");

        foreach (var c in controls)
        {
            if (c == null) continue;

            var who = c.playerCharacter != null ? c.playerCharacter.name : "<no character>";
            GUILayout.Space(4f);

            GUILayout.BeginHorizontal();
            GUILayout.Label(who, GUILayout.Width(180f));
            bool twoD = GUILayout.Toggle(c.TwoDMode, "2D (non-positional)");
            if (twoD != c.TwoDMode)
            {
                Guard(() => { c.TwoDMode = twoD; _status = $"{who}: 2D mode {(twoD ? "on" : "off")}"; });
            }
            GUILayout.EndHorizontal();

            // The live amplitude, because TalkThreshold and BarGain were guesses
            // and markers that never light up are almost certainly those guesses
            // being wrong rather than anything failing to render.
            GUILayout.Label($"    ARV smoothed={c.SmoothedARV:0.######}  peak={c.PeakARV:0.######}");

            DescribeCurve("attenuation", c.AttenuationCurve);
            DescribeCurve("spatialVol", c.SpatialVolCurve);
            DescribeCurve("filterDist", c.FilterDistanceCurve);
        }
    }

    /// <summary>
    /// The global range multiplier. Unlike the Proximity tab's Range nudges this
    /// is purely local - it rescales the client-side attenuation curves, so no
    /// other client's view of the room grid changes and nothing desyncs.
    /// </summary>
    private void DrawRangeScaler()
    {
        GUILayout.Label("── Global proximity range ──");
        GUILayout.Label("Client-side only: stretches the attenuation curves so the");
        GUILayout.Label("volume you used to hear at d you now hear at d × scale.");
        GUILayout.Label("Safe with vanilla players — nothing is sent to anyone.");

        // Logarithmic, because the useful range is not evenly distributed: the
        // difference between x1 and x1.2 is audible and the difference between
        // x15 and x16 is not. A linear 1..20 slider spent 95% of its travel on
        // the half nobody needs and made small values unselectable.
        float current = VoiceRangeScaler.Scale;
        float logMax = Mathf.Log(RangeMax);
        float norm = Mathf.Log(Mathf.Max(current, 1f)) / logMax;

        GUILayout.BeginHorizontal();
        GUILayout.Label("range ×", GUILayout.Width(90f));
        float moved = GUILayout.HorizontalSlider(norm, 0f, 1f, GUILayout.Width(240f));
        GUILayout.Label(current.ToString("0.###"), GUILayout.Width(60f));
        GUILayout.EndHorizontal();

        if (!Mathf.Approximately(moved, norm))
            SetScale(Mathf.Exp(moved * logMax));

        // Nudges, for when even a log slider is coarser than the ear.
        GUILayout.BeginHorizontal();
        GUILayout.Label("", GUILayout.Width(90f));
        if (GUILayout.Button("−0.10", GUILayout.Width(56f))) SetScale(current - 0.10f);
        if (GUILayout.Button("−0.01", GUILayout.Width(56f))) SetScale(current - 0.01f);
        if (GUILayout.Button("+0.01", GUILayout.Width(56f))) SetScale(current + 0.01f);
        if (GUILayout.Button("+0.10", GUILayout.Width(56f))) SetScale(current + 0.10f);
        GUILayout.EndHorizontal();

        // And a typed value, for when you know the number you want.
        GUILayout.BeginHorizontal();
        GUILayout.Label("set exactly", GUILayout.Width(90f));
        _rangeText = GUILayout.TextField(_rangeText ?? current.ToString("0.###"), GUILayout.Width(80f));
        if (GUILayout.Button("Apply", GUILayout.Width(60f)))
        {
            if (float.TryParse(_rangeText, out float typed)) SetScale(typed);
            else _status = $"'{_rangeText}' is not a number.";
        }
        if (GUILayout.Button("Reset ×1", GUILayout.Width(80f)))
        {
            Guard(() =>
            {
                VoiceRangeScaler.Reset();
                _rangeText = "1";
                _status = "Voice range restored to vanilla.";
            });
        }
        GUILayout.EndHorizontal();
    }

    private const float RangeMax = 20f;

    private void SetScale(float value)
    {
        Guard(() =>
        {
            VoiceRangeScaler.Scale = value;
            _rangeText = VoiceRangeScaler.Scale.ToString("0.###");
            VoiceRangeScaler.Apply();
            _status = $"Voice range ×{VoiceRangeScaler.Scale:0.###} on {VoiceRangeScaler.LastApplied} voice(s).";
        });
    }

    private void DescribeCurve(string label, AnimationCurve curve)
    {
        if (curve == null) { GUILayout.Label($"    {label}: <null>"); return; }

        var keys = curve.keys;
        if (keys == null || keys.Length == 0) { GUILayout.Label($"    {label}: <empty>"); return; }

        float first = keys[0].time;
        float last = keys[keys.Length - 1].time;

        // The distance at which the curve first reaches (near) zero is the number
        // that actually matters - that is where a voice stops being audible.
        float silentAt = -1f;
        for (int i = 0; i < keys.Length; i++)
        {
            if (Mathf.Abs(keys[i].value) < 0.0001f) { silentAt = keys[i].time; break; }
        }

        var silence = silentAt >= 0f ? $"  zero@{silentAt:0.#}" : "  (never zero)";
        GUILayout.Label($"    {label}: {keys.Length} keys  x={first:0.#}..{last:0.#}{silence}");
    }

    // ── Proximity ──────────────────────────────────────────────────────────

    private void DrawProximity()
    {
        if (_broadcast == null && _receipt == null)
        {
            GUILayout.Label("No proximity triggers found (are you in a world yet?)");
            return;
        }

        DrawTrigger("Broadcast", _broadcast);
        DrawTrigger("Receipt", _receipt);

        GUILayout.Space(4f);
        GUILayout.Label("Changing Range desyncs you from everyone else —");
        GUILayout.Label("room names are keyed by cell coords, so a mismatched");
        GUILayout.Label("Range means you hear NOBODY. Test solo only.");

        GUILayout.BeginHorizontal();
        if (GUILayout.Button("Range −1")) NudgeRange(-1);
        if (GUILayout.Button("Range +1")) NudgeRange(+1);
        if (GUILayout.Button("Range +10")) NudgeRange(+10);
        GUILayout.EndHorizontal();

        GUILayout.Space(8f);
        DrawPlayers();
    }

    private void DrawTrigger(string label, VoiceProximityBroadcastTrigger t)
    {
        if (t == null) { GUILayout.Label($"{label}: <none>"); return; }
        DrawTriggerCommon(label, t.Range, t.RoomName, t.transform.position);
    }

    private void DrawTrigger(string label, VoiceProximityReceiptTrigger t)
    {
        if (t == null) { GUILayout.Label($"{label}: <none>"); return; }
        DrawTriggerCommon(label, t.Range, t.RoomName, t.transform.position);
    }

    private void DrawTriggerCommon(string label, int range, string roomName, Vector3 pos)
    {
        // Recovered from disassembly: Size == Range * 2, CellPos == floor(pos / Size).
        float size = range * 2f;
        var cell = new Vector3Int(
            Mathf.FloorToInt(pos.x / size),
            Mathf.FloorToInt(pos.y / size),
            Mathf.FloorToInt(pos.z / size));

        GUILayout.Label($"{label}: range={range}  cellSize={size:0.#}  room=\"{roomName}\"");
        GUILayout.Label($"    pos=({pos.x:0.#}, {pos.y:0.#}, {pos.z:0.#})  cell={cell}");
    }

    private void NudgeRange(int delta)
    {
        Guard(() =>
        {
            if (_broadcast != null) _broadcast.Range = Mathf.Max(1, _broadcast.Range + delta);
            if (_receipt != null) _receipt.Range = Mathf.Max(1, _receipt.Range + delta);
            _status = $"Range {(delta > 0 ? "+" : "")}{delta} — private grid unless everyone matches.";
            Plugin.Trace.LogWarning(_status);
        });
    }

    private void DrawPlayers()
    {
        GUILayout.Label($"── Players ({_players.Length}) ──");

        Vector3 me = Vector3.zero;
        bool haveMe = false;
        if (_broadcast != null) { me = _broadcast.transform.position; haveMe = true; }

        var sb = new StringBuilder();
        foreach (var p in _players)
        {
            if (p == null) continue;
            sb.Clear();
            sb.Append(p.PlayerId ?? "<null id>");
            sb.Append(p.IsTracking ? "  [tracking]" : "  [not tracking]");
            if (haveMe)
                sb.Append($"  {Vector3.Distance(me, p.Position):0.#}m");
            GUILayout.Label(sb.ToString());
        }
    }

    // ── Camera ─────────────────────────────────────────────────────────────

    private void DrawCamera()
    {
        GUILayout.Label($"CameraCheatMover: {(_camMover != null ? "found" : "not in scene")}");

        if (GUILayout.Button($"Free cam ({Plugin.Instance.FreeCamKey.Value}): {(_freeCam ? "ON" : "OFF")}"))
            ToggleFreeCam();

        if (_camMover != null)
        {
            GUILayout.Space(6f);
            _camMover.movingSpeed = Slider("moveSpeed", _camMover.movingSpeed, 0.1f, 60f);
            _camMover.sensitivity = Slider("sensitivity", _camMover.sensitivity, 0.1f, 20f);
            _camMover.maxPitch = Slider("maxPitch", _camMover.maxPitch, 0f, 90f);
        }
    }

    private void ToggleFreeCam()
    {
        if (_camMover == null)
        {
            _status = "No CameraCheatMover in scene.";
            Plugin.Trace.LogWarning(_status);
            return;
        }

        Guard(() =>
        {
            if (_freeCam) _camMover.Attach();
            else _camMover.Detach();
            _freeCam = !_freeCam;
            _status = _freeCam ? "Free cam ON" : "Free cam OFF";
            Plugin.Trace.LogInfo(_status);
        });
    }

    // ── World ──────────────────────────────────────────────────────────────

    private void DrawWorld()
    {
        GUILayout.Label("── Input mode ──");
        GUILayout.BeginHorizontal();
        if (GUILayout.Button("UI mode")) Guard(() => { WorldManager.SetToUIMode(); _status = "UI mode."; });
        if (GUILayout.Button("Game mode")) Guard(() => { WorldManager.SetToGameMode(); _status = "Game mode."; });
        GUILayout.EndHorizontal();
        GUILayout.Label("(UI mode frees the cursor — Game mode puts it back.)");

        GUILayout.Space(10f);

        if (!Plugin.Instance.AllowWorldCheats.Value)
        {
            GUILayout.Label("World cheats disabled in config.");
            GUILayout.Label("Set AllowWorldCheats = true to enable.");
            return;
        }

        GUILayout.Label("── Shared world state ──");
        GUILayout.Label("Everyone in the lobby sees these.");
        GUILayout.Space(4f);

        if (GUILayout.Button("Spawn 'em  (SpawnEmCheat.Spawn)"))
            Guard(() => { SpawnEmCheat.Spawn(); _status = "Spawned."; });

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Train dist:", GUILayout.Width(70f));
        _trainDistance = GUILayout.TextField(_trainDistance, GUILayout.Width(90f));
        if (GUILayout.Button("Set"))
        {
            if (float.TryParse(_trainDistance, out var d))
                Guard(() => { TrainCheater.SetDistance(d); _status = $"Train -> {d}"; });
            else
                _status = "Train distance must be a number.";
        }
        GUILayout.EndHorizontal();
    }

    // ── helpers ────────────────────────────────────────────────────────────

    /// <summary>
    /// Runs a cheat action, turning any exception into an in-panel status line.
    /// These call into stripped-adjacent game code, so a null field somewhere is a
    /// realistic outcome and must not take the whole overlay down with it.
    /// </summary>
    private void Guard(Action action)
    {
        try { action(); }
        catch (Exception e)
        {
            _status = e.Message;
            Plugin.Trace.LogError(e.ToString());
        }
    }
}
