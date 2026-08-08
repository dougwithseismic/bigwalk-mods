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
    private Rect _window = new Rect(24f, 24f, 520f, 620f);
    private Vector2 _scroll;
    private Tab _tab = Tab.Player;
    private string _status = "";
    private string _error;

    // Refreshing FindObjects every frame is wasteful and stutters; once a second
    // is plenty for a diagnostic readout.
    private const float RefreshInterval = 1f;
    private float _nextRefresh;

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
            _visible = !_visible;

        if (Input.GetKeyDown(Plugin.Instance.FreeCamKey.Value))
            ToggleFreeCam();

        if (_visible && Time.unscaledTime >= _nextRefresh)
        {
            _nextRefresh = Time.unscaledTime + RefreshInterval;
            Refresh();
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

            DescribeCurve("attenuation", c.AttenuationCurve);
            DescribeCurve("spatialVol", c.SpatialVolCurve);
            DescribeCurve("filterDist", c.FilterDistanceCurve);
        }
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
