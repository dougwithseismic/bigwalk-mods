using System;
using System.Text;
using Dissonance;
using Dissonance.Integrations.MirrorIgnorance;
using UnityEngine;

namespace BigWalk.DevMenu;

/// <summary>
/// IMGUI overlay standing in for the dev menu that was compiled out of the retail
/// build. Covers proximity-voice diagnostics, free camera, and the world cheats
/// whose components survived the strip.
/// </summary>
public class DevOverlay : MonoBehaviour
{
    public DevOverlay(IntPtr ptr) : base(ptr) { }

    private enum Tab { Proximity, Voice, Camera, World }

    private bool _visible;
    private Rect _window = new Rect(24f, 24f, 470f, 560f);
    private Vector2 _scroll;
    private Tab _tab = Tab.Proximity;
    private string _status = "";

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
    private string _error;

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
        if (GUILayout.Toggle(_tab == Tab.Proximity, "Proximity", GUI.skin.button)) _tab = Tab.Proximity;
        if (GUILayout.Toggle(_tab == Tab.Voice, "Voice", GUI.skin.button)) _tab = Tab.Voice;
        if (GUILayout.Toggle(_tab == Tab.Camera, "Camera", GUI.skin.button)) _tab = Tab.Camera;
        if (GUILayout.Toggle(_tab == Tab.World, "World", GUI.skin.button)) _tab = Tab.World;
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
            case Tab.Proximity: DrawProximity(); break;
            case Tab.Voice:     DrawVoice();     break;
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
        try
        {
            if (_broadcast != null) _broadcast.Range = Mathf.Max(1, _broadcast.Range + delta);
            if (_receipt != null) _receipt.Range = Mathf.Max(1, _receipt.Range + delta);
            _status = $"Range {(delta > 0 ? "+" : "")}{delta} — you are now on a private grid unless everyone matches.";
            Plugin.Trace.LogWarning(_status);
        }
        catch (Exception e)
        {
            _status = e.Message;
            Plugin.Trace.LogError(e.ToString());
        }
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

    // ── Voice ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Reads the game's own voice attenuation, which is NOT plain Unity AudioSource
    /// rolloff: PlayerVoicePlaybackControl evaluates AttenuationCurve (plus filter and
    /// spatial curves) per listener, client-side. Where that curve reaches zero is the
    /// real audible ceiling, and it decides whether a host-only routing mod can work
    /// at all - the host cannot change a vanilla listener's curve.
    /// </summary>
    private void DrawVoice()
    {
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
            GUILayout.Label($"{who}   2D={c.TwoDMode}");
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

        var span = $"{first:0.#}..{last:0.#}";
        var silence = silentAt >= 0f ? $"  zero@{silentAt:0.#}" : "  (never zero)";
        GUILayout.Label($"    {label}: {keys.Length} keys  x={span}{silence}");
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
            GUILayout.Label($"sensitivity: {_camMover.sensitivity:0.##}");
            GUILayout.Label($"movingSpeed: {_camMover.movingSpeed:0.##}");

            GUILayout.BeginHorizontal();
            if (GUILayout.Button("Speed −")) _camMover.movingSpeed = Mathf.Max(0.1f, _camMover.movingSpeed - 1f);
            if (GUILayout.Button("Speed +")) _camMover.movingSpeed += 1f;
            GUILayout.EndHorizontal();
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

        try
        {
            if (_freeCam) _camMover.Attach();
            else _camMover.Detach();
            _freeCam = !_freeCam;
            _status = _freeCam ? "Free cam ON" : "Free cam OFF";
            Plugin.Trace.LogInfo(_status);
        }
        catch (Exception e)
        {
            _status = $"Free cam failed: {e.Message}";
            Plugin.Trace.LogError(e.ToString());
        }
    }

    // ── World ──────────────────────────────────────────────────────────────

    private void DrawWorld()
    {
        if (!Plugin.Instance.AllowWorldCheats.Value)
        {
            GUILayout.Label("World cheats disabled in config.");
            GUILayout.Label("Set AllowWorldCheats = true to enable.");
            return;
        }

        GUILayout.Label("These mutate shared world state —");
        GUILayout.Label("everyone in the lobby sees them.");
        GUILayout.Space(6f);

        if (GUILayout.Button("Spawn 'em  (SpawnEmCheat.Spawn)"))
        {
            try { SpawnEmCheat.Spawn(); _status = "Spawned."; }
            catch (Exception e) { _status = e.Message; Plugin.Trace.LogError(e.ToString()); }
        }

        GUILayout.Space(6f);
        GUILayout.BeginHorizontal();
        GUILayout.Label("Train dist:", GUILayout.Width(70f));
        _trainDistance = GUILayout.TextField(_trainDistance, GUILayout.Width(90f));
        if (GUILayout.Button("Set"))
        {
            if (float.TryParse(_trainDistance, out var d))
            {
                try { TrainCheater.SetDistance(d); _status = $"Train -> {d}"; }
                catch (Exception e) { _status = e.Message; Plugin.Trace.LogError(e.ToString()); }
            }
            else _status = "Train distance must be a number.";
        }
        GUILayout.EndHorizontal();
    }
}
