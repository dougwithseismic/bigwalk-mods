using System;
using System.Collections.Generic;
using UnityEngine;

namespace BigWalk.DevMenu;

/// <summary>
/// In-world markers floating above the head of whoever is talking: a camera-facing
/// quad plus a point light, both coloured green (close) to red (edge of audibility)
/// and faded by the same ratio.
///
/// The light matters more than it looks. If shader stripping leaves us without a
/// usable material - the one failure mode we cannot test for from outside the game
/// - the light still renders, so the feature degrades to "a coloured glow over the
/// speaker" instead of degrading to nothing.
///
/// The self-test marker and the per-player markers are built and posed by the SAME
/// two functions (Build/Pose). The self-test is known to render, so any per-player
/// failure has to be in finding players or in the activation gate - both of which
/// SkipReport now accounts for - never in a divergent rendering path.
///
/// Driven from DevOverlay.Update rather than its own MonoBehaviour: markers must
/// follow heads every frame, and there is no reason to inject a second type into
/// the IL2CPP domain to get a second Update call.
/// </summary>
internal static class SpeakerMarkers
{
    private const float HeadHeight = 2.4f;

    // ARV's real scale is a property of the game's mixer, not something we can
    // read off the binary - so stop guessing at it. Track the loudest value we
    // have actually seen and normalise against that. It self-calibrates within a
    // sentence of anyone speaking, and decays so one loud noise does not
    // permanently desensitise every marker.
    private const float MinPeak = 0.0005f;
    private static float _observedPeak = MinPeak;

    /// <summary>Auto-calibrated amplitude ceiling, for the diagnostics panel.</summary>
    public static float ObservedPeak => _observedPeak;

    private static float Level(float arv)
    {
        if (arv > _observedPeak) _observedPeak = arv;
        return Mathf.Clamp01(arv / Mathf.Max(_observedPeak, MinPeak));
    }

    private const float BaseScale = 0.55f;
    private const float LightRange = 6f;

    private sealed class Marker
    {
        public GameObject Root;
        public Transform Quad;
        public Material Mat;
        public Light Glow;
        public float Level;      // smoothed 0..1 amplitude
        public bool Seen;
    }

    private static readonly Dictionary<int, Marker> Markers = new Dictionary<int, Marker>();
    private static GameObject _parent;

    /// <summary>Live marker count, for the diagnostics panel.</summary>
    public static int Active { get; private set; }

    /// <summary>What the markers are actually made of, for the diagnostics panel.</summary>
    public static string Diagnostic
    {
        get
        {
            int quads = 0, lights = 0;
            foreach (var kv in Markers)
            {
                if (kv.Value.Quad != null) quads++;
                if (kv.Value.Glow != null) lights++;
            }
            return $"{Markers.Count} marker(s): {quads} quad, {lights} light";
        }
    }

    // ── Shared construction and posing ─────────────────────────────────────
    //
    // Everything visual lives in these two functions, used by the self-test and
    // the per-player path alike. Keeping them identical is the point: the test
    // proves this exact code renders.

    private static Marker Build(string name, Transform parent, bool forceLight)
    {
        var root = new GameObject(name);
        root.hideFlags = HideFlags.HideAndDontSave;
        if (parent != null) root.transform.SetParent(parent, false);
        else UnityEngine.Object.DontDestroyOnLoad(root);

        var marker = new Marker { Root = root };

        var mat = MarkerVisuals.NewMaterial(Plugin.Instance.MarkersThroughWalls.Value);
        if (mat != null)
        {
            var quad = new GameObject("quad");
            quad.transform.SetParent(root.transform, false);
            quad.AddComponent<MeshFilter>().sharedMesh = MarkerVisuals.Quad();

            var renderer = quad.AddComponent<MeshRenderer>();
            renderer.material = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            marker.Quad = quad.transform;
            marker.Mat = renderer.material;
        }

        // Realtime point lights are the expensive part of this feature - in URP
        // forward rendering each one adds per-object shading cost and can push a
        // crowd of speakers past the per-object light limit. Keep them when the
        // quad failed (they are then the only visual), otherwise let them be
        // switched off. The self-test forces one so "quad works" and "only the
        // light ever worked" stay distinguishable.
        if (forceLight || mat == null || Plugin.Instance.MarkerLights.Value)
        {
            var lightGo = new GameObject("glow");
            lightGo.transform.SetParent(root.transform, false);
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = LightRange;
            light.intensity = 0f;
            light.shadows = LightShadows.None;
            marker.Glow = light;
        }

        return marker;
    }

    private static void Pose(Marker m, Vector3 world, Vector3 eye, Color colour, float quadScale, float lightIntensity)
    {
        m.Root.transform.position = world;

        if (m.Quad != null)
        {
            m.Quad.rotation = Quaternion.LookRotation(m.Quad.position - eye, Vector3.up);
            m.Quad.localScale = Vector3.one * quadScale;
        }

        MarkerVisuals.Tint(m.Mat, colour);

        if (m.Glow != null)
        {
            m.Glow.color = colour;
            m.Glow.intensity = lightIntensity;
            m.Glow.range = LightRange;
        }
    }

    // ── Self-test ──────────────────────────────────────────────────────────
    //
    // "Is the quad rendering?" cannot be answered by staring at a marker that
    // only appears when somebody talks. This puts one permanently in front of the
    // camera at full brightness, so the question becomes a yes/no you can see.

    private static Marker _test;

    public static bool TestActive => _test != null;

    public static void ToggleTest()
    {
        if (_test != null)
        {
            UnityEngine.Object.Destroy(_test.Root);
            _test = null;
            return;
        }

        _test = Build("BigWalk.MarkerTest", null, forceLight: true);
        Plugin.Trace.LogInfo($"Marker self-test on. shader={MarkerVisuals.ResolvedShader}, quad={(_test.Quad != null ? "built" : "NOT BUILT")}");
    }

    private static void TickTest(Camera cam)
    {
        if (_test == null || cam == null) return;

        var t = cam.transform;

        // Cycle the colour so a static magenta "missing shader" square is instantly
        // distinguishable from a marker that is genuinely being tinted.
        var c = MarkerVisuals.DistanceColour(Mathf.PingPong(Time.unscaledTime * 0.5f, 1f));
        c.a = 1f;

        Pose(_test, t.position + t.forward * 3f, t.position, c, 0.6f, 3f);
    }

    /// <summary>
    /// Ignore amplitude entirely and show a marker for every voice control. The
    /// self-test proved the quad renders, so if markers are still invisible with
    /// this on, the fault is in finding players - not in drawing them.
    /// </summary>
    public static bool ForceShow;

    /// <summary>Where the per-player path bailed out on the last tick.</summary>
    public static string SkipReport { get; private set; } = "not ticked yet";

    public static void Tick()
    {
        try
        {
            var cam = Camera.main;
            TickTest(cam);

            var controls = PlayerVoicePlaybackControl.controls;
            if (cam == null)
            {
                SkipReport = "Camera.main is null";
                HideAll();
                return;
            }

            if (controls == null)
            {
                SkipReport = "PlayerVoicePlaybackControl.controls is null";
                HideAll();
                return;
            }

            int seen = 0, nullControl = 0, noCharacter = 0, tooQuiet = 0, shown = 0;
            float maxArv = 0f;

            foreach (var kv in Markers) kv.Value.Seen = false;

            var eye = cam.transform.position;
            int active = 0;

            foreach (var c in controls)
            {
                seen++;
                if (c == null) { nullControl++; continue; }

                // Remote players may not expose a character, but the playback
                // control itself sits at the speaker (it has to, for spatial
                // audio) - so fall back to its own transform rather than
                // silently dropping the player.
                var character = c.playerCharacter;
                Transform anchor;
                if (character != null) anchor = character.transform;
                else { noCharacter++; anchor = c.transform; }
                if (anchor == null) continue;

                int id = c.GetInstanceID();
                var marker = Get(id);
                if (marker == null) continue;

                marker.Seen = true;

                float arv = c.SmoothedARV;
                if (arv > maxArv) maxArv = arv;
                float target = ForceShow ? 1f : (arv < MinPeak ? 0f : Level(arv));

                // Ease toward the target so markers breathe with speech instead of
                // strobing on every syllable gap.
                marker.Level = Mathf.MoveTowards(marker.Level, target, Time.deltaTime * (target > marker.Level ? 8f : 3f));

                if (marker.Level <= 0.001f)
                {
                    tooQuiet++;
                    if (marker.Root.activeSelf) marker.Root.SetActive(false);
                    continue;
                }

                if (!marker.Root.activeSelf) marker.Root.SetActive(true);
                active++;
                shown++;

                var world = anchor.position + Vector3.up * HeadHeight;

                float distance = Vector3.Distance(eye, world);
                float far = AudibleRange(c);
                float t = far > 0f ? Mathf.Clamp01(distance / far) : 0f;

                var colour = MarkerVisuals.DistanceColour(t);
                // Alpha keeps a floor: a quiet talker should still be findable, and
                // tying visibility directly to volume was most likely why markers
                // stopped showing at all once the light was no longer carrying them.
                colour.a = Mathf.Lerp(1f, 0.3f, t) * Mathf.Lerp(0.55f, 1f, marker.Level);

                // Grow slightly with distance so a far speaker stays visible
                // instead of shrinking to a pixel.
                float grow = 1f + Mathf.Clamp(distance, 0f, 200f) * 0.02f;
                float pulse = 0.85f + marker.Level * 0.5f;

                Pose(marker, world, eye, colour, BaseScale * grow * pulse, marker.Level * 3f);
            }

            Active = active;
            SkipReport = $"{seen} control(s): {nullControl} null, {noCharacter} no character (anchored to control), " +
                         $"{tooQuiet} below threshold, {shown} shown, max ARV this tick={maxArv:0.######}";
            Sweep();
        }
        catch (Exception e)
        {
            Plugin.Trace.LogError($"Speaker markers failed: {e}");
        }
    }

    /// <summary>Tears down every marker. Called when the feature is switched off.</summary>
    public static void Clear()
    {
        foreach (var kv in Markers)
            if (kv.Value.Root != null)
                UnityEngine.Object.Destroy(kv.Value.Root);

        Markers.Clear();
        Active = 0;
    }

    private static void HideAll()
    {
        foreach (var kv in Markers)
            if (kv.Value.Root != null && kv.Value.Root.activeSelf)
                kv.Value.Root.SetActive(false);

        Active = 0;
    }

    /// <summary>Drops markers whose voice control has gone away (player left).</summary>
    private static void Sweep()
    {
        List<int> dead = null;
        foreach (var kv in Markers)
        {
            if (kv.Value.Seen) continue;
            (dead ??= new List<int>()).Add(kv.Key);
        }

        if (dead == null) return;
        foreach (var id in dead)
        {
            if (Markers[id].Root != null) UnityEngine.Object.Destroy(Markers[id].Root);
            Markers.Remove(id);
        }
    }

    private static Marker Get(int id)
    {
        if (Markers.TryGetValue(id, out var existing) && existing.Root != null) return existing;

        if (_parent == null)
        {
            _parent = new GameObject("BigWalk.SpeakerMarkers");
            _parent.hideFlags = HideFlags.HideAndDontSave;
            UnityEngine.Object.DontDestroyOnLoad(_parent);
        }

        var marker = Build($"Speaker_{id}", _parent.transform, forceLight: false);
        marker.Root.SetActive(false);
        Markers[id] = marker;
        return marker;
    }

    /// <summary>
    /// Distance at which this voice goes silent, read off the live attenuation
    /// curve so the colour ramp tracks VoiceRangeScaler automatically.
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
}
