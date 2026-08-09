using System;
using System.Collections.Generic;
using Dissonance.Integrations.MirrorIgnorance;
using UnityEngine;

namespace BigWalk.DevMenu;

/// <summary>
/// Player pins on the in-game paper map, riding on the marker system the gourd
/// collectibles already use.
///
/// GourdMap exposes worldScaleOffset/scale, but we deliberately do not use them:
/// we do not know the order of operations the game applies them in, and a patch
/// could retune them. What we use instead is GourdMapReference[], which pairs a
/// world-space `landmark` Transform with the `flagAnchor` Transform where its pin
/// sits on the map. Those pairs are ground truth - three or more non-collinear
/// ones determine the world->map transform exactly, by least squares, including
/// any rotation between the two frames. That fit is immune both to my guessing
/// the formula wrong and to the studio changing it.
///
/// Pins are parented under the GourdMap transform, so they fold, move and hide
/// with the map prop rather than floating in space when it is put away.
/// </summary>
internal static class MapPins
{
    private const float RediscoverInterval = 2f;

    // Pins sit on a prop the player holds; they do not need frame-rate updates,
    // and a scene-wide FindObjectsByType per frame was costing real milliseconds.
    private const float PinInterval = 0.1f;

    private sealed class Pin
    {
        public GameObject Root;
        public Material Mat;
        public bool Seen;
    }

    private static readonly Dictionary<string, Pin> Pins = new Dictionary<string, Pin>();

    private static GourdMap _map;
    private static float _nextDiscover;
    private static float _nextPin;
    private static MirrorIgnorancePlayer[] _players = Array.Empty<MirrorIgnorancePlayer>();

    // The fitted transform: mapLocal[axisA] = Ax*wx + Az*wz + A0, likewise for B.
    private static bool _fitted;
    private static float _ax, _az, _a0;
    private static float _bx, _bz, _b0;
    private static int _axisA, _axisB, _axisN;
    private static float _planeN;      // constant offset along the map's normal axis
    private static float _pinSize;

    public static bool Ready => _fitted;
    public static int Active { get; private set; }
    public static string Diagnostic { get; private set; } = "not initialised";

    public static void Tick()
    {
        try
        {
            if (Time.unscaledTime < _nextPin) return;
            _nextPin = Time.unscaledTime + PinInterval;

            if (Time.unscaledTime >= _nextDiscover)
            {
                _nextDiscover = Time.unscaledTime + RediscoverInterval;
                Discover();

                // The player roster only changes on join/leave, so scan for it on
                // the slow tick rather than with the pins.
                _players = UnityEngine.Object.FindObjectsByType<MirrorIgnorancePlayer>(FindObjectsSortMode.None)
                           ?? Array.Empty<MirrorIgnorancePlayer>();
            }

            if (!_fitted || _map == null) { Active = 0; return; }

            foreach (var kv in Pins) kv.Value.Seen = false;

            var players = _players;
            int active = 0;

            if (players != null)
            {
                foreach (var p in players)
                {
                    if (p == null || !p.IsTracking) continue;

                    string id = p.PlayerId;
                    if (string.IsNullOrEmpty(id)) continue;

                    var pin = Get(id);
                    if (pin == null) continue;

                    pin.Seen = true;
                    pin.Root.transform.localPosition = ToMapLocal(p.Position);

                    MarkerVisuals.Tint(pin.Mat, ColourFor(id));
                    if (!pin.Root.activeSelf) pin.Root.SetActive(true);
                    active++;
                }
            }

            Active = active;
            Sweep();
        }
        catch (Exception e)
        {
            Plugin.Trace.LogError($"Map pins failed: {e}");
        }
    }

    public static void Clear()
    {
        foreach (var kv in Pins)
            if (kv.Value.Root != null)
                UnityEngine.Object.Destroy(kv.Value.Root);

        Pins.Clear();
        Active = 0;
    }

    /// <summary>
    /// A pin is muted blue-grey when idle and takes the speaker colour ramp while
    /// its owner talks, so the map doubles as a "who is chatting" view.
    /// </summary>
    private static Color ColourFor(string playerId)
    {
        var idle = new Color(0.55f, 0.65f, 0.80f, 0.85f);

        PlayerVoicePlaybackControl control = null;
        try { control = PlayerVoicePlaybackControl.FindByPlayerName(playerId); }
        catch { /* local player has no playback control - stays idle-coloured */ }

        if (control == null) return idle;

        float level = Mathf.Clamp01(control.SmoothedARV * 12f);
        if (level <= 0.05f) return idle;

        var talking = MarkerVisuals.DistanceColour(0f);   // bright green while speaking
        var c = Color.Lerp(idle, talking, level);
        c.a = Mathf.Lerp(0.85f, 1f, level);
        return c;
    }

    private static void Discover()
    {
        if (_fitted) return;

        // Keep retrying the fit against a map we already found: the flags we need
        // do not exist until the game has initialised and placed them.
        if (_map != null) { Fit(); return; }

        var maps = UnityEngine.Object.FindObjectsByType<GourdMap>(FindObjectsSortMode.None);
        if (maps == null || maps.Length == 0)
        {
            Diagnostic = "no GourdMap in scene (map prop not loaded yet)";
            return;
        }

        _map = maps[0];
        Fit();
    }

    /// <summary>
    /// Least-squares fit of world XZ to map-local coordinates using the landmark /
    /// flagAnchor correspondences.
    /// </summary>
    private static void Fit()
    {
        _fitted = false;

        // Correspondences must come from flags the game has ALREADY PLACED on the
        // map, not from gourdMapReferences. flagAnchor turned out to be a
        // world-space anchor sitting at its landmark, so fitting against it fits
        // world->world and yields the identity - which showed up in game as pins
        // hovering over the players themselves. A placed GourdFlag's transform is
        // genuinely on the map, so it is the real other half of the pair.
        if (!_map.initialized)
        {
            Diagnostic = "GourdMap not initialised yet (open the map once)";
            return;
        }

        var flags = _map.flags;
        if (flags == null || flags.Count < 3)
        {
            Diagnostic = $"only {(flags == null ? 0 : flags.Count)} placed flags; need 3+";
            return;
        }

        var world = new List<Vector3>();
        var local = new List<Vector3>();

        var frame = _map.transform;
        foreach (var f in flags)
        {
            if (f == null || f.gourdMapReference == null || f.gourdMapReference.landmark == null) continue;
            world.Add(f.gourdMapReference.landmark.position);
            local.Add(frame.InverseTransformPoint(f.transform.position));
        }

        if (world.Count < 3)
        {
            Diagnostic = $"only {world.Count} usable flag pairs; need 3+";
            return;
        }

        // The map is flat, so one local axis barely varies - that is the normal.
        // Pick the two axes that actually carry the spread rather than assuming
        // the prop is laid out on any particular plane.
        var spread = Spread(local);
        _axisN = spread.x <= spread.y && spread.x <= spread.z ? 0 : (spread.y <= spread.z ? 1 : 2);
        _axisA = _axisN == 0 ? 1 : 0;
        _axisB = _axisN == 2 ? 1 : 2;
        if (_axisA == _axisB) _axisB = 3 - _axisA - _axisN;

        if (!Solve(world, local, _axisA, out _ax, out _az, out _a0) ||
            !Solve(world, local, _axisB, out _bx, out _bz, out _b0))
        {
            Diagnostic = "landmarks are collinear - cannot solve the transform";
            return;
        }

        float n = 0f;
        foreach (var l in local) n += Axis(l, _axisN);
        _planeN = n / local.Count;

        // Size pins relative to how big the map actually is, so this works whether
        // the prop is a metre wide or a centimetre.
        float extent = Mathf.Max(Axis(spread, _axisA), Axis(spread, _axisB));
        _pinSize = Mathf.Max(extent * 0.03f, 0.001f);

        // A map is a scaled-down world, so the fit MUST shrink things. If it comes
        // back near-identity we have fitted world->world again, and pins would sit
        // on top of the players instead of on the map. Refuse it rather than draw
        // markers in the sky and call it a map feature.
        float gain = Mathf.Sqrt(Mathf.Abs(_ax * _bz - _az * _bx));
        if (gain > 0.5f)
        {
            Diagnostic = $"fit rejected: scale {gain:0.###} is near 1:1, so these are " +
                          "world coords, not map coords";
            Plugin.Trace.LogWarning($"Map pin fit rejected ({Diagnostic}).");
            return;
        }

        _fitted = true;

        float residual = Residual(world, local);
        Diagnostic = $"fitted from {world.Count} flags, scale {gain:0.#####}, " +
                     $"mean error {residual:0.####} (map units), pin {_pinSize:0.###}";
        Plugin.Trace.LogInfo($"Map pin transform {Diagnostic}");
    }

    /// <summary>
    /// Solves [wx wz 1] * [c0 c1 c2]^T = localAxis by normal equations. Returns
    /// false when the 3x3 is singular, which means the landmarks are collinear.
    /// </summary>
    private static bool Solve(List<Vector3> world, List<Vector3> local, int axis,
                              out float c0, out float c1, out float c2)
    {
        c0 = c1 = c2 = 0f;

        double sxx = 0, sxz = 0, sx1 = 0, szz = 0, sz1 = 0, s11 = 0;
        double bx = 0, bz = 0, b1 = 0;

        for (int i = 0; i < world.Count; i++)
        {
            double x = world[i].x, z = world[i].z, v = Axis(local[i], axis);
            sxx += x * x; sxz += x * z; sx1 += x;
            szz += z * z; sz1 += z; s11 += 1;
            bx += x * v; bz += z * v; b1 += v;
        }

        double det = sxx * (szz * s11 - sz1 * sz1)
                   - sxz * (sxz * s11 - sz1 * sx1)
                   + sx1 * (sxz * sz1 - szz * sx1);

        if (Math.Abs(det) < 1e-9) return false;

        double d0 = bx * (szz * s11 - sz1 * sz1)
                  - sxz * (bz * s11 - sz1 * b1)
                  + sx1 * (bz * sz1 - szz * b1);

        double d1 = sxx * (bz * s11 - sz1 * b1)
                  - bx * (sxz * s11 - sz1 * sx1)
                  + sx1 * (sxz * b1 - bz * sx1);

        double d2 = sxx * (szz * b1 - bz * sz1)
                  - sxz * (sxz * b1 - bz * sx1)
                  + bx * (sxz * sz1 - szz * sx1);

        c0 = (float)(d0 / det);
        c1 = (float)(d1 / det);
        c2 = (float)(d2 / det);
        return true;
    }

    /// <summary>Mean fit error, so a bad transform is visible instead of silent.</summary>
    private static float Residual(List<Vector3> world, List<Vector3> local)
    {
        float sum = 0f;
        for (int i = 0; i < world.Count; i++)
        {
            var predicted = ToMapLocal(world[i]);
            sum += Vector3.Distance(predicted, local[i]);
        }
        return sum / world.Count;
    }

    private static Vector3 ToMapLocal(Vector3 worldPos)
    {
        var v = Vector3.zero;
        SetAxis(ref v, _axisA, _ax * worldPos.x + _az * worldPos.z + _a0);
        SetAxis(ref v, _axisB, _bx * worldPos.x + _bz * worldPos.z + _b0);
        SetAxis(ref v, _axisN, _planeN);
        return v;
    }

    private static Vector3 Spread(List<Vector3> pts)
    {
        var min = pts[0];
        var max = pts[0];
        foreach (var p in pts)
        {
            min = Vector3.Min(min, p);
            max = Vector3.Max(max, p);
        }
        return max - min;
    }

    private static float Axis(Vector3 v, int i) => i == 0 ? v.x : i == 1 ? v.y : v.z;

    private static void SetAxis(ref Vector3 v, int i, float value)
    {
        if (i == 0) v.x = value;
        else if (i == 1) v.y = value;
        else v.z = value;
    }

    private static Pin Get(string playerId)
    {
        if (Pins.TryGetValue(playerId, out var existing) && existing.Root != null) return existing;

        var mat = MarkerVisuals.NewMaterial(true);
        if (mat == null)
        {
            Diagnostic = "no usable shader for pins";
            return null;
        }

        var root = new GameObject($"Pin_{playerId}");
        root.transform.SetParent(_map.transform, false);
        root.hideFlags = HideFlags.HideAndDontSave;

        var filter = root.AddComponent<MeshFilter>();
        filter.sharedMesh = MarkerVisuals.Quad();

        var renderer = root.AddComponent<MeshRenderer>();
        renderer.material = mat;
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;

        // Lay the quad flat in the map's plane. Culling is off on the material, so
        // it reads from either side of the prop.
        var normal = Vector3.zero;
        var up = Vector3.zero;
        SetAxis(ref normal, _axisN, 1f);
        SetAxis(ref up, _axisB, 1f);
        root.transform.localRotation = Quaternion.LookRotation(normal, up);
        root.transform.localScale = Vector3.one * _pinSize;

        var pin = new Pin { Root = root, Mat = renderer.material };
        root.SetActive(false);
        Pins[playerId] = pin;
        return pin;
    }

    private static void Sweep()
    {
        List<string> dead = null;
        foreach (var kv in Pins)
        {
            if (kv.Value.Seen) continue;
            (dead ??= new List<string>()).Add(kv.Key);
        }

        if (dead == null) return;
        foreach (var id in dead)
        {
            if (Pins[id].Root != null) UnityEngine.Object.Destroy(Pins[id].Root);
            Pins.Remove(id);
        }
    }
}
