using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace BigWalk.VoiceSettings;

/// <summary>
/// Extends received voice range by rescaling the distance axis of the game's
/// client-side playback curves. The original curves are captured once, so applying
/// a setting repeatedly never compounds it.
/// </summary>
internal static class VoiceRangeScaler
{
    private sealed class Baseline
    {
        public Keyframe[] Attenuation;
        public Keyframe[] SpatialVolume;
        public Keyframe[] FilterDistance;
        public float SourceMaxDistance = -1f;
    }

    public const float MaxScale = 20f;

    private static readonly Dictionary<int, Baseline> Baselines = new Dictionary<int, Baseline>();
    private static float _scale = 1f;

    public static float Scale
    {
        get => _scale;
        set => _scale = Mathf.Clamp(value, 1f, MaxScale);
    }

    public static int LastApplied { get; private set; }

    public static void Apply()
    {
        var controls = PlayerVoicePlaybackControl.controls;
        LastApplied = 0;
        if (controls == null) return;

        var live = new HashSet<int>();

        foreach (var control in controls)
        {
            if (control == null) continue;

            int id = control.GetInstanceID();
            live.Add(id);

            if (!Baselines.TryGetValue(id, out var baseline))
            {
                baseline = Capture(control);
                Baselines[id] = baseline;
            }

            control.AttenuationCurve = Rescale(baseline.Attenuation, _scale);
            control.SpatialVolCurve = Rescale(baseline.SpatialVolume, _scale);
            control.FilterDistanceCurve = Rescale(baseline.FilterDistance, _scale);

            var source = SourceOf(control);
            if (baseline.SourceMaxDistance <= 0f && source != null)
                baseline.SourceMaxDistance = source.maxDistance;
            if (baseline.SourceMaxDistance > 0f && source != null)
                source.maxDistance = baseline.SourceMaxDistance * _scale;

            LastApplied++;
        }

        if (Baselines.Count <= live.Count) return;

        var departed = new List<int>();
        foreach (var pair in Baselines)
            if (!live.Contains(pair.Key)) departed.Add(pair.Key);
        foreach (int id in departed) Baselines.Remove(id);
    }

    private static Baseline Capture(PlayerVoicePlaybackControl control)
    {
        var baseline = new Baseline
        {
            Attenuation = Copy(control.AttenuationCurve),
            SpatialVolume = Copy(control.SpatialVolCurve),
            FilterDistance = Copy(control.FilterDistanceCurve),
        };

        var source = SourceOf(control);
        if (source != null) baseline.SourceMaxDistance = source.maxDistance;
        return baseline;
    }

    private static AudioSource SourceOf(PlayerVoicePlaybackControl control)
    {
        try
        {
            var sourceController = control.SourceController;
            return sourceController != null ? sourceController.AudioSource : null;
        }
        catch
        {
            // SourceController is unavailable until this voice has played once.
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
            var key = baseline[i];
            key.time *= scale;
            key.inTangent /= scale;
            key.outTangent /= scale;
            scaled[i] = key;
        }

        return new AnimationCurve(scaled);
    }
}
