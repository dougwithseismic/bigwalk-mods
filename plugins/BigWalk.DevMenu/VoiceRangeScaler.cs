using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BigWalk.DevMenu;

/// <summary>
/// Stretches proximity voice audibility client-side by rescaling the distance
/// (time) axis of every PlayerVoicePlaybackControl's attenuation curves.
///
/// Why this and not the trigger Range: the Dissonance room grid is keyed by
/// Range, so changing Range puts you in rooms nobody else is in and you hear
/// NOBODY. The 2D-mode experiment proved voice packets for players across the
/// map already reach us - what silences them is the purely client-side curve
/// evaluation in PlayerVoicePlaybackControl.Update. Multiplying key times by S
/// means "the volume you used to get at distance d, you now get at d*S", which
/// no other client can see and cannot desync anything.
///
/// Tangents are dv/dt, so they get divided by S to keep the curve shape.
/// </summary>
internal static class VoiceRangeScaler
{
    private sealed class Baseline
    {
        public Keyframe[] Attenuation;
        public Keyframe[] SpatialVol;
        public Keyframe[] FilterDistance;
        public float SourceMaxDistance = -1f;
    }

    // Keyed by instance ID: controls are destroyed and respawned as players come
    // and go, and holding the component itself would keep dead wrappers alive.
    private static readonly Dictionary<int, Baseline> Baselines = new Dictionary<int, Baseline>();

    private static float _scale = 1f;

    /// <summary>Distance multiplier. 1 = vanilla.</summary>
    public static float Scale
    {
        get => _scale;
        set => _scale = Mathf.Clamp(value, 1f, 50f);
    }

    /// <summary>How many controls the last Apply() actually touched.</summary>
    public static int LastApplied { get; private set; }

    /// <summary>
    /// Reapplies the current scale to every live control. Safe to call every
    /// refresh: baselines are captured once per control, so this is idempotent
    /// rather than compounding.
    /// </summary>
    public static void Apply()
    {
        var controls = PlayerVoicePlaybackControl.controls;
        LastApplied = 0;
        if (controls == null) return;

        var live = new HashSet<int>();

        foreach (var c in controls)
        {
            if (c == null) continue;

            int id = c.GetInstanceID();
            live.Add(id);

            if (!Baselines.TryGetValue(id, out var b))
            {
                b = Capture(c);
                Baselines[id] = b;
            }

            c.AttenuationCurve = Rescale(b.Attenuation, _scale);
            c.SpatialVolCurve = Rescale(b.SpatialVol, _scale);
            c.FilterDistanceCurve = Rescale(b.FilterDistance, _scale);

            // The AudioSource's own rolloff still gates the spatialised signal,
            // and AudioSourceController hibernates sources it thinks are out of
            // range - push maxDistance out by the same factor.
            if (b.SourceMaxDistance > 0f)
            {
                var src = SourceOf(c);
                if (src != null) src.maxDistance = b.SourceMaxDistance * _scale;
            }

            LastApplied++;
        }

        // Drop baselines for controls that no longer exist.
        if (Baselines.Count > live.Count)
        {
            var dead = new List<int>();
            foreach (var kv in Baselines)
                if (!live.Contains(kv.Key)) dead.Add(kv.Key);
            foreach (var id in dead) Baselines.Remove(id);
        }
    }

    /// <summary>Puts every control back on its captured vanilla curves.</summary>
    public static void Reset()
    {
        _scale = 1f;
        Apply();
    }

    private static Baseline Capture(PlayerVoicePlaybackControl c)
    {
        var b = new Baseline
        {
            Attenuation = Copy(c.AttenuationCurve),
            SpatialVol = Copy(c.SpatialVolCurve),
            FilterDistance = Copy(c.FilterDistanceCurve),
        };

        var src = SourceOf(c);
        if (src != null) b.SourceMaxDistance = src.maxDistance;

        return b;
    }

    private static AudioSource SourceOf(PlayerVoicePlaybackControl c)
    {
        try
        {
            var sc = c.SourceController;
            return sc != null ? sc.AudioSource : null;
        }
        catch
        {
            // SourceController throws until the voice has played at least once.
            return null;
        }
    }

    private static Keyframe[] Copy(AnimationCurve curve)
    {
        if (curve == null) return null;
        var keys = curve.keys;
        if (keys == null || keys.Length == 0) return null;

        var copy = new Keyframe[keys.Length];
        for (int i = 0; i < keys.Length; i++) copy[i] = keys[i];
        return copy;
    }

    private static AnimationCurve Rescale(Keyframe[] baseline, float scale)
    {
        if (baseline == null || baseline.Length == 0) return null;

        var scaled = new Il2CppStructArray<Keyframe>(baseline.Length);
        for (int i = 0; i < baseline.Length; i++)
        {
            var k = baseline[i];
            k.time *= scale;
            k.inTangent /= scale;
            k.outTangent /= scale;
            scaled[i] = k;
        }

        return new AnimationCurve(scaled);
    }
}
